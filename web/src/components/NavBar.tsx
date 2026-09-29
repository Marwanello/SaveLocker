import { useState } from 'react';
import { api, errorText } from '../api';
import type { AgentEvent, Conflict, GameIntent, Machine, ServerBuildInfo } from '../types';
import { age, plural, toMs } from '../format';
import { syncAll, useSyncAllActive, useSyncAllProgress } from '../syncAll';
import type { SyncAllOutcome } from '../syncAll';
import { Mark } from './ui/Mark';
import { Button } from './ui/Button';
import { Dot } from './ui/Dot';
import { Icon } from './ui/Icon';
import { toast, toastError } from '../toast';
import { NotificationsMenu } from './NotificationsMenu';

export type View = 'games' | 'config' | 'audit' | 'backups' | 'help' | 'whats-new';

const NAV_ITEMS: { key: View; label: string }[] = [
  { key: 'games', label: 'Games' },
  { key: 'config', label: 'Configuration' },
  { key: 'audit', label: 'Audit log' },
  { key: 'backups', label: 'Backups' },
  { key: 'help', label: 'Help' },
  { key: 'whats-new', label: 'What’s new' },
];

/** An agent polls every 20 s and the server stamps `lastSeen` on each contact, so three minutes of
 *  silence means the machine is off or unreachable, not merely between polls. Decided server-side
 *  (`skipMachinesUnseenForSeconds`) so the browser's clock never enters into it. */
const ONLINE_WINDOW_SECONDS = 180;

interface Props {
  view: View;
  onViewChange: (v: View) => void;
  /** Reload the console's data — after a Sync all finishes, or a notification action queued work. */
  onRefresh: () => void;
  /** Forgets the session and returns to SignIn — plan.md's "lock button". Absent when the server has
   *  no admin password: there is nothing to lock, and a Lock button that does nothing is a lie. */
  onLock?: () => void;
  /** Signed out (or not yet known): only the brand and version show — no tabs, Sync all, bell or rail. The
   *  lock screen must not offer controls that act on the fleet, nor show its problems. */
  locked?: boolean;
  machines: Machine[];
  /** What this console is running. Undefined until /api/admin/status answers. */
  build?: ServerBuildInfo;
  /** True when the running release's notes have not been opened yet. */
  unreadNotes?: boolean;
  /** Open problems reported by agents, worst first. This is the only way a headless Deck's
   *  failures reach a human — it cannot toast, so the console has to (Decisions.md §2). */
  problems?: AgentEvent[];
  /** Every open conflict — the pill shows for any of them, not only the escalated ones. */
  conflicts?: Conflict[];
  onDismissProblems?: (ids: string[]) => Promise<void>;
  /** Navigate to Games and open one game, optionally at a specific place on its page. */
  onOpenGame: (gameId: string, intent?: GameIntent) => void;
}

const shorten = (s: string, max = 90) => (s.length > max ? s.slice(0, max - 1) + '…' : s);

/** Past tense, per plan.md "Voice": what happened, not "completed successfully!". */
function outcomeText(r: SyncAllOutcome): { text: string; ms: number } {
  const ran = r.total - r.cancelled;
  if (r.timedOut) {
    const left = r.total - r.succeeded - r.failures.length - r.cancelled;
    return { text: `${plural(left, 'machine')} still working — they will finish in the background.`, ms: 7000 };
  }
  if (ran === 0) return { text: 'Sync all cancelled before any machine started.', ms: 4000 };
  const cancelled = r.cancelled > 0 ? ` ${r.cancelled} cancelled before starting.` : '';
  if (r.failures.length > 0) {
    const first = r.failures[0];
    return {
      text: `Synced ${r.succeeded} of ${plural(ran, 'machine')} — ${first.machine} failed: ${shorten(first.reason)}` +
        (r.failures.length > 1 ? ` (+${r.failures.length - 1} more)` : '') + '.' + cancelled,
      ms: 9000,
    };
  }
  return { text: `Synced ${plural(r.succeeded, 'machine')}.${cancelled}`, ms: r.cancelled > 0 ? 4500 : 3200 };
}

export function NavBar({
  view, onViewChange, onRefresh, onLock, locked = false, machines, build, unreadNotes = false,
  problems = [], conflicts = [], onDismissProblems, onOpenGame,
}: Props) {
  const [starting, setStarting] = useState(false);
  // Flips twice a batch — a progress tick never re-renders this bar (see syncAll.ts).
  const active = useSyncAllActive();
  const busy = starting || active;

  async function handleSyncAll() {
    if (machines.length === 0 || busy) return;
    setStarting(true);
    try {
      const res = await api.queueSyncAll(machines.map(m => m.id), ONLINE_WINDOW_SECONDS);
      const offline = res.skipped.map(s => s.machineName);
      if (res.queued.length === 0) {
        toast(offline.length > 0
          ? `Nothing was queued — ${offline.join(', ')} ${offline.length === 1 ? 'is' : 'are'} offline.`
          : 'Nothing to sync.', 6000);
      } else {
        syncAll.start(res.queued.map(c => c.id), r => {
          onRefresh(); // the games list should show what just synced without waiting for the next poll
          const t = outcomeText(r);
          toast(t.text, t.ms);
        });
        onRefresh();
        if (offline.length > 0) toast(`Left out ${offline.join(', ')} — offline.`, 5000);
      }
    } catch (e) {
      toastError('Could not start Sync all: ' + errorText(e));
    } finally {
      setStarting(false);
    }
  }

  // The oldest open conflict drives the pill: it is the one that has waited longest for a decision.
  const oldest = conflicts.length > 0
    ? conflicts.reduce((a, b) => (toMs(a.createdAt) <= toMs(b.createdAt) ? a : b))
    : null;
  const anyEscalated = conflicts.some(c => c.escalated);

  return (
    <div className="sticky top-0 z-20">
      {/* min-h + wrap, not a fixed height: with the conflict pill, the progress chip and the bell all
          showing, one row is wider than a 1024 px window, and because the page is overflow-hidden the
          last controls — Lock included — were pushed off-screen with no way to scroll to them. */}
      <header className="bg-panel border-b border-line px-5 py-2 min-h-16 flex flex-wrap items-center gap-x-3.5 gap-y-2">
        <div className="flex items-center gap-[11px]">
          <a
            href="#"
            onClick={e => { e.preventDefault(); onViewChange('games'); }}
            className="flex items-center gap-[11px] select-none rounded-lg
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
          >
            <Mark size={30} />
            <span className="text-lg font-bold tracking-[-0.035em] text-fg">
              Save<span className="text-accent">Locker</span>
            </span>
          </a>

          {/* What the console is running, always on screen. Answering "is my fix deployed?" should
              not require opening a page, let alone reading a Docker tag on another machine. */}
          <button
            type="button"
            onClick={() => onViewChange('whats-new')}
            title={
              build
                ? `SaveLocker console ${build.version}` +
                  (build.commit ? ` (commit ${build.commit})` : '') +
                  (build.builtAt ? ` — built ${new Date(build.builtAt).toLocaleString()}` : '') +
                  `\nClick for release notes.`
                : 'Release notes'
            }
            className={`inline-flex items-center gap-1.5 px-2.5 py-[3px] rounded-full border border-line bg-raise text-[10.5px] font-mono
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
              ${build?.isRelease === false ? 'text-watch-ink' : 'text-dim'}`}
          >
            {build ? (build.version === 'dev' ? 'dev' : `v${build.version}`) : '—'}
            {/* `--safe`, not the prototype's accent: unread notes are not a decision waiting. */}
            {unreadNotes && <span title="New release notes" className="w-1.5 h-1.5 rounded-full bg-safe shrink-0" />}
          </button>
        </div>

        {locked
          ? (view === 'help' || view === 'whats-new') && (
              <div className="ml-auto">
                <Button onClick={() => onViewChange('games')}>Back to sign in</Button>
              </div>
            )
          : <>
        {/* plan.md Phase 9.3: pill tabs — transparent at rest, raised on hover, and the current one in
            the soft accent with its accent line. */}
        <nav aria-label="Console" className="flex flex-wrap gap-[3px]">
          {NAV_ITEMS.map(item => {
            const current = view === item.key;
            return (
              <button
                key={item.key}
                type="button"
                aria-current={current ? 'page' : undefined}
                onClick={() => onViewChange(item.key)}
                className={`text-[13.5px] px-3.5 py-2 rounded-full border cursor-pointer
                  transition-[background-color,color,transform] duration-150 ease-[var(--ease)] active:scale-[.97]
                  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
                  ${current
                    ? 'bg-accent-soft border-accent-line text-accent-ink font-semibold'
                    : 'bg-transparent border-transparent text-dim font-medium hover:bg-raise hover:text-fg hover:opacity-100'}`}
              >
                {item.label}
              </button>
            );
          })}
        </nav>

        <div className="ml-auto flex flex-wrap items-center justify-end gap-[9px]">
          {oldest && (
            <Button
              variant="alert"
              onClick={() => onOpenGame(oldest.gameId, { kind: 'resolve', conflictId: oldest.id })}
              title={anyEscalated
                ? 'Unresolved for more than six hours. Sync is paused for these games until you choose.'
                : 'Sync is paused for these games until you choose which save to keep.'}
            >
              {plural(conflicts.length, 'conflict')} · {age(oldest.createdAt)}
            </Button>
          )}

          {/* plan.md Surfaces: "Sync all primary" — the one filled button, since it is the thing you
              most likely came to do. While a batch runs it becomes the progress chip and Cancel. */}
          {active
            ? <SyncAllStatus />
            : (
              <Button variant="primary" onClick={() => void handleSyncAll()} disabled={busy || machines.length === 0}
                title={machines.length === 0 ? 'No machines are registered yet.' : 'Sync every game on every connected machine'}>
                <Icon name="refresh-cw" size={13} strokeWidth={2.3} />
                {starting ? 'Starting…' : 'Sync all'}
              </Button>
            )}

          <NotificationsMenu
            problems={problems}
            onOpenGame={onOpenGame}
            onDismissProblems={onDismissProblems}
            onOpenAudit={() => onViewChange('audit')}
            onRefresh={onRefresh}
          />

          {onLock && (
            <Button className="!px-[9px]" onClick={onLock} title="Lock — end this session and sign in again" aria-label="Lock the console">
              <Icon name="lock" size={14} strokeWidth={2.1} />
            </Button>
          )}
        </div>
          </>}
      </header>

      {!locked && <SyncAllRail />}
    </div>
  );
}

/** The chip and Cancel that stand in for Sync all while a batch runs. Reads every tick; nothing
 *  around it does. */
function SyncAllStatus() {
  const p = useSyncAllProgress();
  const [cancelling, setCancelling] = useState(false);
  if (!p) return null;

  async function cancel() {
    setCancelling(true);
    try {
      const r = await syncAll.cancel();
      const w = r.withdrawn.length, running = r.alreadyRunning.length;
      toast(
        w === 0
          ? `Nothing left to withdraw — ${plural(running, 'machine')} already running will finish.`
          : `Withdrew ${plural(w, 'machine')}.` + (running > 0 ? ` ${running} already running will finish.` : ''),
        running > 0 ? 5000 : 3200,
      );
    } catch (e) {
      toastError('Could not cancel: ' + errorText(e));
    } finally {
      setCancelling(false);
    }
  }

  return (
    <>
      <span
        role="status"
        className="inline-flex items-center gap-2 text-[12.5px] px-3 py-[7px] rounded-full border border-accent-line bg-accent-soft text-accent-ink tabular-nums"
      >
        <Dot tone="crit" live />
        Syncing {p.total === 1 ? '1 machine' : `· ${p.done} of ${p.total} machines done`}
      </span>
      {/* Only while something can still be withdrawn: a command an agent has claimed cannot be. */}
      {p.pending > 0 && (
        <Button onClick={() => void cancel()} disabled={cancelling}
          title="Withdraw the syncs no machine has started yet. One already running finishes.">
          {cancelling ? 'Cancelling…' : 'Cancel'}
        </Button>
      )}
    </>
  );
}

/** plan.md Phase 9.6: the 3 px rail across the full width under the top bar — width transitions, the
 *  sweep runs while anything is still going. */
function SyncAllRail() {
  const p = useSyncAllProgress();
  if (!p) return null;
  // Half a step for the machines in flight, so the rail moves as soon as the batch starts.
  const pct = Math.round(((p.done + (p.done < p.total ? 0.5 : 0)) / p.total) * 100);
  return (
    <div className="sync-rail" role="progressbar" aria-label="Sync all progress" aria-valuemin={0} aria-valuemax={p.total} aria-valuenow={p.done}>
      <i style={{ width: `${pct}%` }} />
    </div>
  );
}
