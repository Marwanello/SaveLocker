import { useEffect, useRef, useState } from 'react';
import { api, errorText } from '../api';
import type { AgentEvent, GameIntent } from '../types';
import { ago } from '../format';
import { Chip } from './ui/Chip';
import { Button } from './ui/Button';
import { Icon } from './ui/Icon';
import { toast, toastError } from '../toast';

/** `AgentEventCodes` on the server — the fixed vocabulary this menu gives a title and an action to. */
const CODE = {
  conflict: 'sync.conflict',
  saveDirMissing: 'savedir.missing',
  pushFailed: 'push.failed',
} as const;

/** A short title per known code. The agent's own message becomes the line under it. */
function titleOf(p: AgentEvent): string {
  const game = p.gameName ?? 'A game';
  switch (p.code) {
    case 'sync.conflict': return `${game} — sync paused`;
    case 'push.failed': return `${game} push failed`;
    case 'savedir.missing': return `${game} save folder is missing`;
    case 'savedir.unsafe': return `${game} save folder refused`;
    case 'pull.blocked': return `${game} pull held back`;
    case 'pull.blocked_running': return `${game} pull waiting for the game to close`;
    case 'lease.held_elsewhere': return `${game} is checked out elsewhere`;
    case 'launch.blocked_conflict': return `${game} launch blocked by a conflict`;
    case 'settle.timeout': return `${game} kept writing past the settle wait`;
    case 'server.unreachable': return `${p.machineName} lost the server`;
    case 'update.staged': return `Agent update staged on ${p.machineName}`;
    case 'update.failed': return `Agent update failed on ${p.machineName}`;
    case 'update.rolled_back': return `Agent update rolled back on ${p.machineName}`;
    case 'plugin.updated': return `Decky plugin updated on ${p.machineName}`;
    case 'plugin.update_failed': return `Decky plugin update failed on ${p.machineName}`;
    default: return p.gameName ? `${p.gameName} — ${p.machineName}` : p.machineName;
  }
}

const SEV_DOT: Record<AgentEvent['severity'], string> = {
  Error: 'bg-accent',
  Warning: 'bg-watch',
  Info: 'bg-faint',
};

interface Props {
  problems: AgentEvent[];
  onOpenGame: (gameId: string, intent?: GameIntent) => void;
  /** Dismiss several events as one action: one reload afterwards and one error report, not one of each per event. */
  onDismissProblems?: (ids: string[]) => Promise<void>;
  onOpenAudit: () => void;
  /** Reload after an action that queued something, so its command shows without waiting for the poll. */
  onRefresh: () => void;
}

/**
 * plan.md Phase 9.4: the bell, as drawn — and ALWAYS present. Group 2 hid it on a quiet fleet; the
 * prototype's empty state ("Nothing to report") is itself the reassurance, and a control that appears
 * only when something is wrong cannot be found in advance.
 */
export function NotificationsMenu({ problems, onOpenGame, onDismissProblems, onOpenAudit, onRefresh }: Props) {
  const [open, setOpen] = useState(false);
  const [retrying, setRetrying] = useState<string | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  // Outside click and Escape close it — a popover that only its own button can dismiss stays over
  // the page while you work underneath it. Escape hands focus back to the bell.
  useEffect(() => {
    if (!open) return;
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(false); buttonRef.current?.focus(); } };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey); };
  }, [open]);

  // Info events (e.g. "an update was applied") are routine confirmations, not problems — they never
  // colour the badge or count toward it.
  const actionable = problems.filter(p => p.severity !== 'Info');
  const errorCount = actionable.filter(p => p.severity === 'Error').length;
  // A conflict is never included: it does not clear on its own, see the row below.
  const dismissableIds = problems.filter(p => p.code !== CODE.conflict).map(p => p.id);

  function go(gameId: string, intent?: GameIntent) {
    setOpen(false);
    onOpenGame(gameId, intent);
  }

  async function retryPush(p: AgentEvent) {
    if (!p.gameId) return;
    setRetrying(p.id);
    try {
      // Unforced, like the push the agent itself gave up on: a push that has diverged still becomes a
      // conflict rather than riding over another machine's save.
      await api.queueCommand(p.machineId, p.gameId, 'Push', false);
      toast(`Queued a push of ${p.gameName ?? 'the game'} on ${p.machineName}.`);
      onRefresh();
    } catch (e) {
      toastError('Could not queue the push: ' + errorText(e));
    } finally {
      setRetrying(null);
    }
  }

  function action(p: AgentEvent) {
    if (p.code === CODE.conflict && p.gameId) {
      return (
        <Button variant="alert" size="sm" onClick={() => go(p.gameId!, { kind: 'resolve' })}
          title="A conflict does not clear on its own — it has to be resolved on the game.">
          Resolve
        </Button>
      );
    }
    if (p.code === CODE.saveDirMissing && p.gameId) {
      return (
        <Button size="sm" onClick={() => go(p.gameId!, { kind: 'folder', machineId: p.machineId })}
          title={`Open the game at ${p.machineName}'s save folder`}>
          Set folder
        </Button>
      );
    }
    if (p.code === CODE.pushFailed && p.gameId) {
      return (
        <Button size="sm" disabled={retrying === p.id} onClick={() => void retryPush(p)}
          title={`Queue a push of this game on ${p.machineName}`}>
          {retrying === p.id ? 'Queuing…' : 'Retry'}
        </Button>
      );
    }
    // `update.staged` and the rest: the console cannot restart an agent or fix a folder remotely, so
    // there is nothing honest to offer beyond the facts and Dismiss.
    return null;
  }

  return (
    <div className="relative" ref={rootRef}>
      <Button
        ref={buttonRef}
        size="sm"
        className="relative !px-[9px]"
        onClick={() => setOpen(v => !v)}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={actionable.length > 0 ? `Notifications, ${actionable.length} need attention` : 'Notifications'}
        title="Notifications from your agents"
      >
        <Icon name="bell" size={15} />
        {actionable.length > 0 && (
          <span
            aria-hidden
            className={`absolute -top-[5px] -right-[5px] min-w-[17px] h-[17px] px-1 rounded-full grid place-items-center
              text-[10px] font-bold border-2 border-panel tabular-nums
              ${errorCount > 0 ? 'bg-accent text-on-accent' : 'bg-watch text-ink'}`}
          >
            {actionable.length}
          </span>
        )}
      </Button>

      {open && (
        <div
          role="dialog"
          aria-label="Notifications"
          className="animate-drop absolute right-0 top-[calc(100%+10px)] w-[392px] max-w-[92vw] bg-panel border border-line rounded-[14px] shadow-2xl z-30 overflow-hidden"
        >
          <header className="flex items-center justify-between gap-2.5 px-3.5 py-3 border-b border-line">
            <h4 className="text-[13.5px] font-semibold tracking-[-0.02em] text-fg">Notifications</h4>
            <div className="flex gap-[7px] items-center">
              <Chip>{problems.length} open</Chip>
              {dismissableIds.length > 0 && onDismissProblems && (
                <Button size="sm" onClick={() => void onDismissProblems(dismissableIds)}
                  title="Dismiss everything except conflicts. Anything still true is reported again.">
                  Dismiss all
                </Button>
              )}
            </div>
          </header>

          <div className="max-h-[388px] overflow-y-auto">
            {problems.length === 0 ? (
              <div className="px-5 py-[34px] text-center">
                <b className="block text-sm font-semibold text-fg mb-[5px]">Nothing to report</b>
                <span className="text-[12.5px] text-dim">
                  A healthy fleet is quiet. Warnings clear themselves once a machine syncs cleanly again.
                </span>
              </div>
            ) : problems.map(p => (
              <div key={p.id} className="grid grid-cols-[9px_minmax(0,1fr)_auto] gap-[11px] px-3.5 py-3 border-b border-row last:border-b-0 hover:bg-raise transition-colors duration-150">
                <span className={`w-[9px] h-[9px] rounded-full mt-[5px] ${SEV_DOT[p.severity]}`}
                  role="img" aria-label={p.severity} title={p.severity} />
                <div className="min-w-0">
                  <div className="text-[13px] font-semibold tracking-[-0.015em] text-fg">{titleOf(p)}</div>
                  <div className="text-xs text-dim mt-[3px] leading-[1.45]">{p.message}</div>
                  <div className="text-[10.5px] text-dim mt-1.5 flex gap-2 flex-wrap tabular-nums">
                    <span className="font-mono">{p.code}</span>
                    <span>{p.machineName}</span>
                    <span>{ago(p.lastSeen)}</span>
                    {p.count > 1 && <span>×{p.count}</span>}
                  </div>
                </div>
                <div className="flex flex-col items-end gap-[7px]">
                  {/* A conflict is NOT dismissible, and this is the difference that cost a real user a
                      day of play. Every other agent event self-heals — a machine that recovers closes
                      it — so Dismiss is honest for them. A conflict sits until a human resolves it. */}
                  {p.code !== CODE.conflict && onDismissProblems && (
                    <button
                      type="button"
                      onClick={() => void onDismissProblems([p.id])}
                      aria-label={`Dismiss: ${titleOf(p)}`}
                      title="Dismiss. If the condition still holds, the agent reports it again."
                      className="w-6 h-6 grid place-items-center rounded-[7px] border border-transparent bg-transparent text-dim
                        hover:bg-tile hover:text-fg hover:opacity-100 transition-colors duration-150
                        focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
                    >
                      <Icon name="x" size={13} />
                    </button>
                  )}
                  {action(p)}
                </div>
              </div>
            ))}
          </div>

          <footer className="flex items-center justify-between gap-2.5 px-3.5 py-2.5 border-t border-line bg-raise">
            <span className="text-[10.5px] text-dim">Agents report these. Info never colours the badge.</span>
            <Button size="sm" onClick={() => { setOpen(false); onOpenAudit(); }}>Open audit log</Button>
          </footer>
        </div>
      )}
    </div>
  );
}
