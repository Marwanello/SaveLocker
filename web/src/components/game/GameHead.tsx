import { useState } from 'react';
import type { RefObject } from 'react';
import { api, errorText } from '../../api';
import type { GameSummary, Machine } from '../../types';
import { ago, fmtSize, plural } from '../../format';
import { toast, toastError } from '../../toast';
import { PageHead } from '../ui/PageHead';
import { Chip } from '../ui/Chip';
import { Dot } from '../ui/Dot';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';
import { InlineConfirm } from '../ui/InlineConfirm';
import { GameCover } from './GameCover';
import { POLICY_LABEL } from './gameState';
import type { GameStanding } from './gameState';

interface Props {
  summary: GameSummary;
  standing: GameStanding;
  versionCount: number;
  /** Who Push / Pull can go to: the machines that have this game (all of them, if none does yet). */
  machines: Machine[];
  allMachines: Machine[];
  artOpen: boolean;
  onToggleArt: () => void;
  penRef: RefObject<HTMLButtonElement | null>;
  onRefresh: () => void;
}

const STATE_TONE = { ok: 'ok', warn: 'warn', crit: 'crit', idle: 'default' } as const;

/** plan.md Phase 10.3: the cover (its pen opens the art picker), the name, the facts line, the state /
 *  policy / keep chips, Push and Pull for one machine, and Refresh art. */
export function GameHead({ summary, standing, versionCount, machines, allMachines, artOpen, onToggleArt, penRef, onRefresh }: Props) {
  const { game, head, totalStorageBytes } = summary;
  const [machineId, setMachineId] = useState<string>(machines[0]?.id ?? '');
  const [queuing, setQueuing] = useState(false);
  // The list can change under the picker (a poll, a machine removed): fall back to the first.
  const target = machines.find(m => m.id === machineId) ?? machines[0];

  const policy = game.conflictPolicy ?? 'Manual';
  const preferred = policy === 'PreferMachine'
    ? allMachines.find(m => m.id === game.preferredMachineId)?.name
    : undefined;

  // Unforced, both: a pull still refuses to overwrite unpushed local work and a diverged push still
  // becomes a conflict. The forced variants live on each machine's row under Save folders.
  async function queue(type: 'Push' | 'Pull') {
    if (!target) return;
    setQueuing(true);
    try {
      await api.queueCommand(target.id, game.id, type, false);
      onRefresh();
      toast(`Queued a ${type.toLowerCase()} on ${target.name}. It runs on that agent's next poll.`, 4000);
    } catch (e) {
      toastError(`Could not queue the ${type.toLowerCase()}: ` + errorText(e));
    } finally {
      setQueuing(false);
    }
  }

  async function refreshArt() {
    try {
      await api.refreshArt(game.id);
      onRefresh();
      toast('Fetched SteamGridDB’s default cover and icon again.');
    } catch (e) { toastError('Could not refresh the art: ' + errorText(e)); }
  }

  return (
    <div className="flex gap-4 items-start">
      {/* The pen over the cover opens the cover/icon picker. Shown on hover and on keyboard focus, and
          always on touch screens, which have no hover to reveal it. */}
      <div className="group relative shrink-0">
        <GameCover game={game} size="l" />
        <button
          ref={penRef}
          type="button"
          onClick={onToggleArt}
          aria-label="Change cover and icon"
          aria-expanded={artOpen}
          title="Change cover and icon"
          className="absolute inset-0 rounded-[11px] border-0 p-0 cursor-pointer bg-transparent
            transition-colors duration-150 ease-[var(--ease)]
            group-hover:bg-black/45 focus-visible:bg-black/45
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
        >
          <span className="absolute right-1.5 bottom-1.5 grid place-items-center w-7 h-7 rounded-full bg-black/70 text-white
            opacity-0 transition-opacity duration-150 ease-[var(--ease)]
            group-hover:opacity-100 group-focus-within:opacity-100 [@media(hover:none)]:opacity-100">
            <Icon name="pencil" size={13} />
          </span>
        </button>
      </div>

      <div className="flex-1 min-w-0">
        <PageHead
          title={game.name}
          sub={[
            plural(versionCount, 'version'),
            fmtSize(totalStorageBytes),
            head ? `latest ${ago(head.createdAt)}` : 'no saves yet',
          ].join(' · ')}
          actions={<>
            <Chip tone={STATE_TONE[standing.tone]}><Dot tone={standing.tone} />{standing.label}</Chip>
            <Chip>{preferred ? `Prefer ${preferred}` : POLICY_LABEL[policy] ?? policy}</Chip>
            <Chip>{game.retainVersions != null ? `Keep ${game.retainVersions}` : 'Keep: server default'}</Chip>

            {target && (
              <span className="inline-flex items-center gap-1.5">
                {machines.length > 1 && (
                  <select
                    value={target.id}
                    onChange={e => setMachineId(e.target.value)}
                    aria-label="Machine to push from or pull to"
                    className="bg-raise border border-line rounded-full pl-3 pr-2 py-[7px] text-[12.5px] text-fg cursor-pointer
                      focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
                  >
                    {machines.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
                  </select>
                )}
                <Button disabled={queuing} onClick={() => void queue('Push')}
                  title={`Upload ${target.name}'s save. If it has diverged from Latest, it becomes a conflict rather than overwriting.`}>
                  Push
                </Button>
                <Button disabled={queuing} onClick={() => void queue('Pull')}
                  title={`Bring Latest down to ${target.name}. It will not overwrite local changes that have not been pushed.`}>
                  Pull
                </Button>
              </span>
            )}

            <InlineConfirm
              label="Refresh art"
              tone="default"
              consequence="Fetches SteamGridDB's default cover and icon again. A cover or icon you picked by hand is replaced."
              confirmLabel="Replace with the defaults"
              onConfirm={refreshArt}
              size="default"
            />
          </>}
        />
      </div>
    </div>
  );
}
