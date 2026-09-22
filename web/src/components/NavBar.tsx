import { useCallback, useState } from 'react';
import { api } from '../api';
import type { AgentEvent, Conflict, Machine, ServerBuildInfo } from '../types';
import { Mark } from './ui/Mark';
import { Button } from './ui/Button';
import { Toast } from './ui/Toast';
import { NotificationsMenu } from './NotificationsMenu';
import { SyncAllProgress } from './SyncAllProgress';
import type { SyncAllOutcome } from './SyncAllProgress';

type View = 'games' | 'config' | 'audit' | 'help' | 'whats-new';

const NAV_ITEMS: { key: View; label: string }[] = [
  { key: 'games', label: 'Games' },
  { key: 'config', label: 'Configuration' },
  { key: 'audit', label: 'Audit Log' },
  { key: 'help', label: 'Help' },
  { key: 'whats-new', label: "What's New" },
];

/** An agent polls every 20 s and the server stamps `lastSeen` on each contact, so three minutes of
 *  silence means the machine is off or unreachable, not merely between polls. Decided server-side
 *  (`skipMachinesUnseenForSeconds`) so the browser's clock never enters into it. */
const ONLINE_WINDOW_SECONDS = 180;

interface Props {
  view: View;
  onViewChange: (v: View) => void;
  onRefresh: () => void;
  /** Forgets the session and returns to SignIn — plan.md's "lock button". Absent when the server has
   *  no admin password: there is nothing to lock, and a Lock button that does nothing is a lie. */
  onLock?: () => void;
  machines: Machine[];
  /** What this console is running. Undefined until /api/admin/status answers. */
  build?: ServerBuildInfo;
  /** True when the running release's notes have not been opened yet. */
  unreadNotes?: boolean;
  /** Open problems reported by agents, worst first. This is the only way a headless Deck's
   *  failures reach a human — it cannot toast, so the console has to (Decisions.md §2). */
  problems?: AgentEvent[];
  escalatedConflicts?: Conflict[];
  onDismissProblems?: (ids: string[]) => Promise<void>;
  /** Navigate to Games and select a specific game — used by the notifications menu's deep links. */
  onOpenGame: (gameId: string | null) => void;
}

const plural = (n: number, noun: string) => `${n} ${noun}${n === 1 ? '' : 's'}`;
const shorten = (s: string, max = 90) => (s.length > max ? s.slice(0, max - 1) + '…' : s);

export function NavBar({
  view,
  onViewChange,
  onRefresh,
  onLock,
  machines,
  build,
  unreadNotes = false,
  problems = [],
  escalatedConflicts = [],
  onDismissProblems,
  onOpenGame,
}: Props) {
  const [syncCommandIds, setSyncCommandIds] = useState<string[]>([]);
  const [syncing, setSyncing] = useState(false);
  const [toast, setToast] = useState<{ text: string; ms: number } | null>(null);
  const dismissToast = useCallback(() => setToast(null), []);

  // A batch still being tracked counts as "busy" too — pressing again mid-batch used to queue a second
  // sync behind the first on every machine.
  const busy = syncing || syncCommandIds.length > 0;

  async function handleSyncAll() {
    if (machines.length === 0 || busy) return;
    setSyncing(true);
    try {
      const res = await api.queueSyncAll(machines.map(m => m.id), ONLINE_WINDOW_SECONDS);
      const offline = res.skipped.map(s => s.machineName);
      if (res.queued.length === 0) {
        setToast({
          text: offline.length > 0
            ? `Nothing was queued — ${offline.join(', ')} ${offline.length === 1 ? 'is' : 'are'} offline.`
            : 'Nothing to sync.',
          ms: 6000,
        });
      } else {
        setSyncCommandIds(res.queued.map(c => c.id));
        if (offline.length > 0) setToast({ text: `Left out ${offline.join(', ')} — offline.`, ms: 5000 });
      }
    } catch (e) {
      setToast({ text: 'Could not start Sync all: ' + (e as Error).message, ms: 7000 });
    } finally {
      setSyncing(false);
    }
  }

  // Past tense, per plan.md "Voice": what happened, not "completed successfully!".
  function handleSyncDone(r: SyncAllOutcome) {
    setSyncCommandIds([]);
    onRefresh(); // the games list should show what just synced without waiting for the next poll
    if (r.timedOut) {
      setToast({ text: `${plural(r.total - r.done, 'machine')} still working — they will finish in the background.`, ms: 7000 });
    } else if (r.failures.length > 0) {
      const first = r.failures[0];
      setToast({
        text: `Synced ${r.total - r.failures.length} of ${plural(r.total, 'machine')} — ${first.machine} failed: ${shorten(first.reason)}` +
          (r.failures.length > 1 ? ` (+${r.failures.length - 1} more)` : ''),
        ms: 9000,
      });
    } else {
      setToast({ text: `Synced ${plural(r.total, 'machine')}.`, ms: 3200 });
    }
  }

  return (
    // min-h + wrap, not a fixed height: with the progress rail, "Overdue conflicts" and the
    // notifications badge all showing, a single row is wider than a 1024 px window, and because the
    // page is overflow-hidden the last controls — Lock included — were pushed off-screen with no way
    // to scroll to them. Extra controls now wrap onto a second row instead.
    <header className="bg-panel border-b border-line px-5 py-1 min-h-[72px] flex flex-wrap items-center justify-between gap-x-4 gap-y-2 sticky top-0 z-20">
      {/* Brand + version */}
      <div className="flex items-center gap-2.5">
        <a
          href="#"
          onClick={e => { e.preventDefault(); onViewChange('games'); }}
          className="flex items-center gap-[9px] select-none"
        >
          <Mark size={40} />
          <span className="text-[17px] font-bold tracking-[-0.4px] text-fg">
            Save<span className="text-accent">Locker</span>
          </span>
        </a>

        {/* What the console is running, always on screen. Answering "is my fix deployed?" should
            not require opening a page, let alone reading a Docker tag on another machine. */}
        <button
          onClick={() => onViewChange('whats-new')}
          title={
            build
              ? `SaveLocker console ${build.version}` +
                (build.commit ? ` (commit ${build.commit})` : '') +
                (build.builtAt ? ` — built ${new Date(build.builtAt).toLocaleString()}` : '') +
                `\nClick for release notes.`
              : 'Release notes'
          }
          className={`flex items-center gap-1.5 px-2.5 py-0.5 rounded-full border border-line text-[11px] font-mono
            ${view === 'whats-new' ? 'bg-raise' : 'bg-transparent'}
            ${build?.isRelease === false ? 'text-watch' : 'text-dim'}`}
        >
          {build ? (build.version === 'dev' ? 'dev' : `v${build.version}`) : '—'}
          {unreadNotes && (
            <span title="New release notes" className="w-1.5 h-1.5 rounded-full bg-safe flex-shrink-0" />
          )}
        </button>
      </div>

      {/* Controls */}
      <div className="flex flex-wrap items-center justify-end gap-1.5">
        {NAV_ITEMS.map(item => (
          <Button
            key={item.key}
            variant={view === item.key ? 'selected' : 'default'}
            size="sm"
            aria-current={view === item.key ? 'page' : undefined}
            onClick={() => onViewChange(item.key)}
          >
            {item.label}
          </Button>
        ))}

        <div className="w-px h-5 bg-line mx-1" aria-hidden />

        {/* plan.md Surfaces: "Sync all primary". One filled button per view, per plan.md
            Components — this is the thing you most likely came to do, so it alone holds the accent. */}
        <Button variant="primary" size="sm" onClick={handleSyncAll} disabled={busy || machines.length === 0}>
          {syncing ? 'Starting…' : busy ? 'Syncing…' : 'Sync all'}
        </Button>
        {syncCommandIds.length > 0 && (
          <SyncAllProgress commandIds={syncCommandIds} onDone={handleSyncDone} />
        )}

        <Button variant="default" size="sm" style={{ fontSize: 14, lineHeight: 1 }} onClick={onRefresh} title="Refresh" aria-label="Refresh">
          ↻
        </Button>

        {escalatedConflicts.length > 0 && (
          <Button
            variant="alert"
            size="sm"
            onClick={() => onOpenGame(escalatedConflicts[0].gameId)}
            title="These conflicts have been unresolved for more than six hours"
          >
            Overdue conflicts: {escalatedConflicts.length}
          </Button>
        )}

        {/* Absent when there are none — a healthy fleet should be quiet. */}
        <NotificationsMenu problems={problems} onOpenGame={onOpenGame} onDismissProblems={onDismissProblems} />

        {onLock && (
          <Button variant="quiet" size="sm" onClick={onLock} title="Lock — end this session and sign in again" aria-label="Lock">
            🔒
          </Button>
        )}
      </div>

      {toast && (
        <div className="fixed bottom-5 left-1/2 -translate-x-1/2 z-40 w-max max-w-[min(92vw,560px)]">
          <Toast key={toast.text} dwellMs={toast.ms} onDismiss={dismissToast}>{toast.text}</Toast>
        </div>
      )}
    </header>
  );
}
