import { useEffect, useState } from 'react';
import { api, errorText } from '../../api';
import type { CopyTarget, Game, Version } from '../../types';
import { ago, fmtSize } from '../../format';
import { emulatorIcon } from '../../platformIcon';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { Button } from '../ui/Button';
import { PlatformIcon } from '../ui/PlatformIcon';

interface Props {
  game: Game;
  /** This tree's emulator, as its tab names it. */
  emulator: string;
  version: Version;
  onClose: () => void;
  /** The copy is made: open the target tree's tab. */
  onCopied: (targetId: string) => void;
}

/**
 * Copy this save to another emulator (tasks/multi-emulator-games Phase 6), the console's side of the agent's dialog. Only
 * emulators whose formats match are offered; the copy becomes the other tree's Latest, its old one kept, and every machine
 * that syncs it there is told to pull. This tree is not touched.
 */
export function CopyPanel({ game, emulator, version, onClose, onCopied }: Props) {
  const [targets, setTargets] = useState<CopyTarget[] | null>(null);
  const [target, setTarget] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let live = true;
    api.copyTargets(game.id, version.id)
      .then(t => { if (live) { setTargets(t); setTarget(t.find(x => !x.refusal)?.gameId ?? null); } })
      .catch(e => { if (live) setTargets([]); toastError('Could not list where it can go: ' + errorText(e)); });
    return () => { live = false; };
  }, [game.id, version.id]);

  const chosen = targets?.find(t => t.gameId === target) ?? null;
  const name = chosen?.emulator ?? chosen?.gameName ?? '';

  async function copy() {
    if (!chosen) return;
    setBusy(true);
    try {
      await api.copyToEmulator(game.id, version.id, chosen.gameId);
      toast(`Copied ${emulator}'s save to ${name}. It is ${name}'s Latest now; every machine that plays it there was told to pull.`, 5000);
      onCopied(chosen.gameId);
    } catch (e) { toastError('Could not copy it: ' + errorText(e)); setBusy(false); }
  }

  return (
    <Card title={`Copy ${emulator}'s save from ${ago(version.createdAt)} to another emulator`}
      headerRight={<Button size="sm" variant="quiet" onClick={onClose}>Close</Button>}>
      <div className="flex flex-col gap-3 text-[12.5px]">
        {targets === null && <p className="text-dim">Asking the server…</p>}
        {targets?.length === 0 && <p className="text-dim">No other emulator's tree of this game is on the server.</p>}
        <div className="grid gap-2 grid-cols-[repeat(auto-fill,minmax(220px,1fr))]" role="radiogroup" aria-label="Emulator to copy to">
          {targets?.map(t => (
            <button key={t.gameId} type="button" role="radio" aria-checked={t.gameId === target} disabled={!!t.refusal}
              onClick={() => setTarget(t.gameId)}
              className={`flex flex-col gap-1 text-left rounded-xl border px-3.5 py-3 cursor-pointer disabled:cursor-not-allowed disabled:border-dashed
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
                ${t.gameId === target ? 'bg-accent-soft border-accent-line' : 'bg-raise border-line'}`}>
              <span className="flex items-center gap-2 font-semibold text-[13.5px]">
                <PlatformIcon id={emulatorIcon(t.emulator)} size={1.1} />{t.emulator ?? t.gameName}
              </span>
              <span className={t.refusal ? 'text-watch-ink' : 'text-safe-ink'}>{t.refusal ?? t.how}</span>
              <span className="text-dim text-[11.5px]">{t.targetHead ? `Latest ${ago(t.targetHead.createdAt)} from ${t.targetHead.machineName}` : 'No save yet'}</span>
            </button>
          ))}
        </div>
        {chosen && (
          <>
            <div className="font-mono text-[11.5px] bg-ink border border-line rounded-lg px-3 py-2 flex flex-col gap-1">
              {chosen.files.map(f => (
                <div key={f.from} className="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto] gap-2 text-dim">
                  <span className="truncate">{f.from}</span><span className="text-accent-ink">→</span>
                  <span className="truncate text-fg">{f.to}</span><span>{fmtSize(f.size)}</span>
                </div>
              ))}
            </div>
            <p className="text-dim">
              {chosen.targetHead ? <>{name}'s latest save is <b className="text-fg">kept as an older version</b>. </> : null}
              The copy becomes {name}'s new Latest. <b className="text-fg">{emulator}'s save is not changed.</b>
            </p>
            <div className="flex justify-end gap-2">
              <Button variant="quiet" onClick={onClose}>Cancel</Button>
              <Button variant="primary" disabled={busy} onClick={() => void copy()}>{busy ? 'Copying…' : `Copy to ${name}`}</Button>
            </div>
          </>
        )}
      </div>
    </Card>
  );
}
