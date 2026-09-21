import { useEffect, useRef, useState } from 'react';
import type { AgentEvent } from '../types';
import { Chip } from './ui/Chip';
import { Button } from './ui/Button';

/** `AgentEventCodes.Conflict` on the server — the one reported condition that never self-heals. */
const CONFLICT_CODE = 'sync.conflict';
/** `AgentEventCodes.SaveDirMissing` — the other event worth deep-linking to a specific game for. */
const SAVEDIR_MISSING_CODE = 'savedir.missing';

type Tone = 'ok' | 'warn' | 'crit';
const severityTone = (s: AgentEvent['severity']): Tone =>
  s === 'Error' ? 'crit' : s === 'Warning' ? 'warn' : 'ok';
const severityLabel = (s: AgentEvent['severity']) =>
  s === 'Error' ? 'ERROR' : s === 'Warning' ? 'WARN' : 'INFO';

const BADGE_TONE: Record<Tone, string> = {
  ok: 'border-safe text-safe',
  warn: 'border-watch text-watch',
  crit: 'border-accent text-accent',
};

const asUtcTime = (t: string) => /[Z+]/.test(t.slice(-6)) ? t : t + 'Z';
function ago(t: string): string {
  const mins = Math.max(0, Math.round((Date.now() - new Date(asUtcTime(t)).getTime()) / 60000));
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

interface Props {
  problems: AgentEvent[];
  /** Navigate to Games, and — when the event names one — select that specific game. Conflicts
   *  already show an always-open resolve card on GameDetail, so selecting the game IS opening it;
   *  `savedir.missing` lands on the game's page too, though not scrolled/focused to the folder
   *  field specifically — that finer targeting isn't built yet. */
  onOpenGame: (gameId: string | null) => void;
  /** Dismiss several events as one action: one reload afterwards and one error report, not one of each per event. */
  onDismissProblems?: (ids: string[]) => Promise<void>;
}

export function NotificationsMenu({ problems, onOpenGame, onDismissProblems }: Props) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  // `open` must not outlive the list it shows: this component renders nothing while there are no
  // problems but stays mounted, so a stale `true` would spring the popover open by itself the next
  // time an agent reports something.
  useEffect(() => { if (problems.length === 0) setOpen(false); }, [problems.length]);

  // Outside click and Escape close it — a popover that only its own button can dismiss stays over
  // the page while you work underneath it.
  useEffect(() => {
    if (!open) return;
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey); };
  }, [open]);

  // Info events (e.g. "an update was applied") are routine confirmations, not problems — they
  // never drive the badge's tone or count it up as alarming.
  const actionable = problems.filter(p => p.severity !== 'Info');
  const errorCount = actionable.filter(p => p.severity === 'Error').length;
  const badgeTone: Tone = actionable.length === 0 ? 'ok' : errorCount > 0 ? 'crit' : 'warn';

  // Every dismissible (non-conflict) problem at once — a conflict is never included, the same
  // reason a single Dismiss is withheld from it below.
  const dismissableIds = problems.filter(p => p.code !== CONFLICT_CODE).map(p => p.id);

  if (problems.length === 0) return null;

  return (
    <div className="relative" ref={rootRef}>
      <button
        onClick={() => setOpen(v => !v)}
        title="Problems reported by agents"
        aria-haspopup="true"
        aria-expanded={open}
        className={`flex items-center gap-1.5 px-3 py-[5px] rounded-md border text-xs font-semibold bg-transparent ${BADGE_TONE[badgeTone]}`}
      >
        {actionable.length > 0 ? '⚠' : 'ⓘ'} {problems.length}
      </button>

      {open && (
        <div className="absolute right-0 top-[calc(100%+8px)] w-[460px] max-w-[92vw] bg-panel border border-line rounded-lg shadow-2xl z-30 overflow-hidden">
          <div className="px-3.5 py-2.5 border-b border-line flex justify-between items-center">
            <span className="text-[12.5px] font-semibold text-fg">Agent problems</span>
            {dismissableIds.length > 0 && onDismissProblems && (
              <button
                onClick={() => void onDismissProblems(dismissableIds)}
                className="text-[11px] text-dim underline hover:text-fg"
              >
                Dismiss all
              </button>
            )}
          </div>

          <div className="max-h-[360px] overflow-y-auto">
            {problems.map(p => (
              <div key={p.id} className="px-3.5 py-[11px] border-t border-row flex gap-2.5 items-start">
                <Chip tone={severityTone(p.severity)} className="mt-0.5 flex-shrink-0 font-bold tracking-[0.3px]">
                  {severityLabel(p.severity)}
                </Chip>

                <div className="flex-1 min-w-0">
                  <div className="text-[12.5px] font-semibold text-fg">
                    {p.machineName}{p.gameName ? ` — ${p.gameName}` : ''}
                  </div>
                  <div className="text-xs text-dim leading-[1.45] mt-0.5">{p.message}</div>
                  <div className="text-[10.5px] text-dim mt-[3px] font-mono">
                    {p.code} · {ago(p.lastSeen)}{p.count > 1 ? ` · ×${p.count}` : ''}
                  </div>
                </div>

                {/* A conflict is NOT dismissible, and this is the difference that cost a real
                    user a day of play. Every other agent event self-heals — a machine that
                    recovers auto-closes it — so Dismiss is honest for them. A conflict does
                    not: it sits until a human resolves it. */}
                {p.code === CONFLICT_CODE ? (
                  <Button
                    variant="alert"
                    size="sm"
                    className="flex-shrink-0"
                    onClick={() => { setOpen(false); onOpenGame(p.gameId ?? null); }}
                    title="A conflict does not clear on its own — it has to be resolved on the game."
                  >
                    Resolve
                  </Button>
                ) : (
                  <div className="flex gap-1.5 flex-shrink-0">
                    {p.code === SAVEDIR_MISSING_CODE && p.gameId && (
                      <Button
                        variant="default"
                        size="sm"
                        onClick={() => { setOpen(false); onOpenGame(p.gameId); }}
                        title="Open this game"
                      >
                        Open game
                      </Button>
                    )}
                    {onDismissProblems && (
                      <Button
                        variant="quiet"
                        size="sm"
                        style={{ borderColor: 'var(--color-line)' }}
                        onClick={() => void onDismissProblems([p.id])}
                        title="Dismiss. If the condition still holds, the agent will report it again."
                      >
                        Dismiss
                      </Button>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
