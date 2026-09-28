import { useState } from 'react';
import { api, errorText } from '../../api';
import type { AgentHealth, Game, Machine } from '../../types';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { KV } from '../ui/KV';
import { Button } from '../ui/Button';
import { InlineConfirm } from '../ui/InlineConfirm';
import { Select } from '../ui/Select';
import { MachineSelect } from './MachineSelect';
import { POLICY_LABEL } from './gameState';

interface Props {
  game: Game;
  machines: Machine[];
  health: AgentHealth[];
  onRefresh: () => void;
}

const fieldCls = `bg-tile text-fg border border-line rounded-lg px-2 py-[5px] text-[12.5px]
  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1`;

const POLICY_HELP: Record<string, string> = {
  Manual: 'Two machines that both saved stop and wait for you to choose.',
  NewestWins: 'The most recent upload always wins; the other is kept as a backup.',
  PreferMachine: 'The chosen machine always wins; the other is kept as a backup.',
};

const POLICY_OPTIONS = (['Manual', 'NewestWins', 'PreferMachine'] as const).map(value => ({
  value,
  label: value === 'PreferMachine' ? 'Prefer a machine' : POLICY_LABEL[value] ?? value,
  sub: POLICY_HELP[value],
}));

/** plan.md Phase 10.5: Rules — the conflict policy and how many versions to keep, both edited in
 *  place; whether the game syncs at all; and deleting it, which names exactly what goes. */
export function RulesCard({ game, machines, health, onRefresh }: Props) {
  const current = game.conflictPolicy ?? 'Manual';
  const currentPreferred = game.preferredMachineId ?? null;
  const currentKeep = game.retainVersions ?? null;
  const [policy, setPolicy] = useState<string>(current);
  const [preferred, setPreferred] = useState<string | null>(currentPreferred);
  const [keep, setKeep] = useState(currentKeep?.toString() ?? '');
  const [saving, setSaving] = useState(false);

  // Follow a change made elsewhere — the conflict panel's "Use Newest wins", another browser — or the
  // draft would keep showing the old value with a Save button beside it, as if it were an edit.
  const [seen, setSeen] = useState({ policy: current, preferred: currentPreferred, keep: currentKeep });
  if (seen.policy !== current || seen.preferred !== currentPreferred) {
    setSeen(s => ({ ...s, policy: current, preferred: currentPreferred }));
    setPolicy(current);
    setPreferred(currentPreferred);
  }
  if (seen.keep !== currentKeep) {
    setSeen(s => ({ ...s, keep: currentKeep }));
    setKeep(currentKeep?.toString() ?? '');
  }

  const policyDirty = policy !== current || (policy === 'PreferMachine' && preferred !== currentPreferred);
  const keepValue = keep.trim() === '' ? null : Number(keep);
  const keepValid = keepValue === null || (Number.isInteger(keepValue) && keepValue >= 1);
  const keepDirty = keepValue !== currentKeep;

  async function savePolicy() {
    setSaving(true);
    try {
      await api.setConflictPolicy(game.id, policy, policy === 'PreferMachine' ? preferred : null);
      onRefresh();
      toast(`${game.name} now uses ${POLICY_LABEL[policy]?.toLowerCase() ?? policy} for conflicts.`);
    } catch (e) { toastError('Could not save the policy: ' + errorText(e)); }
    finally { setSaving(false); }
  }

  async function saveKeep() {
    setSaving(true);
    try {
      await api.setRetention(game.id, keepValue);
      onRefresh();
      toast(keepValue === null ? 'Keeps the server default number of versions.' : `Keeps the newest ${keepValue} versions.`);
    } catch (e) { toastError('Could not save the limit: ' + errorText(e)); }
    finally { setSaving(false); }
  }

  async function setEnabled(value: boolean) {
    try {
      await api.setEnabled(game.id, value);
      onRefresh();
      toast(value ? `${game.name} syncs again.` : `${game.name} is paused on every machine.`);
    } catch (e) { toastError('Could not change it: ' + errorText(e)); }
  }

  async function remove() {
    try {
      await api.deleteGame(game.id);
      toast(`Deleted ${game.name} from the server. Each machine kept its local save.`, 4000);
      onRefresh();
    } catch (e) { toastError('Could not delete the game: ' + errorText(e)); }
  }

  return (
    <Card title="Rules">
      <KV items={[
        {
          label: 'Conflicts',
          value: (
            <div className="flex flex-col gap-1.5">
              <div className="flex gap-1.5 flex-wrap items-center">
                <Select
                  label="Conflict policy"
                  variant="field"
                  value={policy}
                  options={POLICY_OPTIONS}
                  onChange={v => { setPolicy(v); if (v !== 'PreferMachine') setPreferred(null); }}
                />
                {policy === 'PreferMachine' && (
                  <MachineSelect
                    label="Machine that always wins"
                    variant="field"
                    placeholder="Pick a machine"
                    machines={machines}
                    health={health}
                    value={preferred}
                    onChange={setPreferred}
                  />
                )}
                {policyDirty && (
                  <Button size="sm" disabled={saving || (policy === 'PreferMachine' && !preferred)} onClick={() => void savePolicy()}>Save</Button>
                )}
              </div>
              <span className="text-[11.5px] text-dim">{POLICY_HELP[policy]}</span>
            </div>
          ),
        },
        {
          label: 'Keep',
          value: (
            <form className="flex gap-1.5 flex-wrap items-center" onSubmit={e => { e.preventDefault(); if (keepValid && keepDirty) void saveKeep(); }}>
              <input
                type="number" min={1} inputMode="numeric" value={keep} onChange={e => setKeep(e.target.value)}
                placeholder="default" aria-label="Versions to keep (blank for the server default)"
                aria-invalid={!keepValid}
                className={`${fieldCls} w-[84px] tabular-nums`}
              />
              <span className="text-[12.5px] text-dim">newest versions</span>
              {keepDirty && <Button size="sm" type="submit" disabled={saving || !keepValid}>Save</Button>}
              {!keepValid && <span role="alert" className="text-[11.5px] text-accent-ink">A whole number, 1 or more — or blank for the default.</span>}
            </form>
          ),
        },
        {
          label: 'Syncing',
          value: (
            <div className="flex gap-2 items-center flex-wrap">
              <span>{game.enabled ? 'On' : 'Paused'}</span>
              {game.enabled
                ? <InlineConfirm label="Pause" tone="default"
                    consequence="Every machine stops syncing this game until you turn it back on. Nothing is deleted."
                    confirmLabel={`Pause ${game.name}`} onConfirm={() => setEnabled(false)} />
                : <Button size="sm" onClick={() => void setEnabled(true)}>Resume</Button>}
            </div>
          ),
        },
      ]} />

      <div className="mt-4 pt-3.5 border-t border-row flex items-center justify-between gap-3 flex-wrap">
        <span className="text-[11.5px] text-dim">Removes it from the server, not from any machine.</span>
        <InlineConfirm
          label="Delete game"
          consequence={`Deletes ${game.name}, every version of it and its history from the server. Each machine keeps its local save.`}
          confirmLabel={`Delete ${game.name}`}
          onConfirm={remove}
        />
      </div>
    </Card>
  );
}
