import { useTreeWord } from './treeContext';
import { useEffect, useState } from 'react';
import { api, errorText } from '../../api';
import type { Game, GameSummary, MergePreview } from '../../types';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { Button } from '../ui/Button';
import { Select } from '../ui/Select';

interface Props {
  game: Game;
  onRefresh: () => void;
}

const OUTCOME: Record<string, string> = {
  'already-syncs': 'already syncs the other game; its copy of this one stops',
  moved: 'moves to the other game with its folder and its own file names; if its save differs from the other game’s Latest, its next sync asks which to keep',
  're-add': 'stops syncing this one; add the other game there from Add games',
};

const fieldCls = `bg-tile text-fg border border-line rounded-lg px-2 py-[5px] text-[12.5px] w-full max-w-[22rem]
  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1`;

/**
 * Merging this game into another (tasks/emulator-saves Phase 17): one save the fleet keeps as two games — another dump's
 * save, or a Steam ROM Manager shortcut tracked beside its emulator game. Nothing happens before a preview says what will,
 * and the merge needs this game's name typed: it cannot be undone. Its versions are kept as read-only history of the other.
 */
export function MergeCard({ game, onRefresh }: Props) {
  const tw = useTreeWord();
  const [open, setOpen] = useState(false);
  const [games, setGames] = useState<GameSummary[]>([]);
  const [into, setInto] = useState<string | null>(null);
  const [preview, setPreview] = useState<MergePreview | null>(null);
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    api.overview().then(all => setGames(all.filter(s => s.game.id !== game.id))).catch(e => toastError('Could not list games: ' + errorText(e)));
  }, [open, game.id]);

  useEffect(() => {
    setPreview(null);
    setTyped('');
    if (!into) return;
    let live = true;
    api.mergePreview(game.id, into).then(p => { if (live) setPreview(p); }).catch(e => toastError('Could not preview the merge: ' + errorText(e)));
    return () => { live = false; };
  }, [game.id, into]);

  async function merge() {
    if (!preview) return;
    setBusy(true);
    try {
      await api.mergeGames(game.id, preview.intoId, typed);
      toast(`Merged ${preview.fromName} into ${preview.intoName}. Its versions are kept there as read-only history.`, 5000);
      onRefresh();
    } catch (e) { toastError('Could not merge: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  return (
    <Card title="Merge">
      {!open ? (
        <div className="flex items-center justify-between gap-3 flex-wrap">
          <span className="text-[11.5px] text-dim">The same save kept as two games — another dump, or a Steam shortcut beside its emulator game.</span>
          <Button size="sm" variant="quiet" onClick={() => setOpen(true)}>Merge into another game…</Button>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <Select
            label="Merge into"
            variant="field"
            placeholder="Pick the game to keep"
            searchable
            value={into}
            options={games.map(s => ({ value: s.game.id, label: s.game.name, sub: s.game.emulators?.join(', ') ?? undefined }))}
            onChange={setInto}
          />
          {preview && (
            <div className="flex flex-col gap-2 text-[12.5px]">
              <p>
                <b>{preview.fromName}</b>’s {preview.versions} version{preview.versions === 1 ? '' : 's'} become read-only history of{' '}
                <b>{preview.intoName}</b>, and {preview.fromName} is deleted.
              </p>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                {[[preview.fromName, preview.fromFiles], [preview.intoName, preview.intoFiles]].map(([name, files]) => (
                  <div key={name as string} className="rounded-lg border border-line bg-raise px-3 py-2">
                    <div className="text-[11px] text-dim">{name as string} keeps</div>
                    <ul className="font-mono text-[11.5px] break-all">{(files as string[]).map(f => <li key={f}>{f}</li>)}</ul>
                  </div>
                ))}
              </div>
              {preview.machines.length > 0 && (
                <ul className="list-disc pl-5">
                  {preview.machines.map(m => <li key={m.machineId}><b>{m.machineName}</b> {OUTCOME[m.outcome] ?? m.outcome}.</li>)}
                </ul>
              )}
              {preview.warnings.map(w => <p key={w} className="text-watch-ink">{w}</p>)}
              {preview.refusal ? (
                <p role="alert" className="text-accent-ink">{preview.refusal}</p>
              ) : (
                <form className="flex flex-col gap-1.5" onSubmit={e => { e.preventDefault(); if (typed.trim() === preview.fromName.trim()) void merge(); }}>
                  <label htmlFor={`merge-${game.id}`} className="text-[11px] text-dim">
                    Type <b className="text-fg">{preview.fromName}</b> to merge it. This cannot be undone.{tw && <> Only the{tw} tree is merged.</>}
                  </label>
                  <div className="flex gap-1.5 items-center flex-wrap">
                    <input id={`merge-${game.id}`} value={typed} onChange={e => setTyped(e.target.value)} className={fieldCls} autoComplete="off" />
                    <Button size="sm" variant="primary" type="submit" disabled={busy || typed.trim() !== preview.fromName.trim()}>
                      Merge into {preview.intoName}
                    </Button>
                  </div>
                </form>
              )}
            </div>
          )}
          <div><Button size="sm" variant="quiet" onClick={() => { setOpen(false); setInto(null); }}>Cancel</Button></div>
        </div>
      )}
    </Card>
  );
}
