import { useState } from 'react';
import { api } from '../api';
import type { AgentEvent, Conflict, Machine, ServerBuildInfo } from '../types';
import logoUrl from '../assets/SaveLocker_Logo_crop.png';
import { Button } from './ui/Button';
import { NotificationsMenu } from './NotificationsMenu';
import { SyncAllProgress } from './SyncAllProgress';

type View = 'games' | 'config' | 'audit' | 'help' | 'whats-new';

const NAV_ITEMS: { key: View; label: string }[] = [
  { key: 'games', label: 'Games' },
  { key: 'config', label: 'Configuration' },
  { key: 'audit', label: 'Audit Log' },
  { key: 'help', label: 'Help' },
  { key: 'whats-new', label: "What's New" },
];

interface Props {
  view: View;
  onViewChange: (v: View) => void;
  onRefresh: () => void;
  /** Forgets the stored credential and returns to SignIn — plan.md's "lock button". */
  onLock: () => void;
  machines: Machine[];
  /** What this console is running. Undefined until /api/admin/status answers. */
  build?: ServerBuildInfo;
  /** True when the running release's notes have not been opened yet. */
  unreadNotes?: boolean;
  /** Open problems reported by agents, worst first. This is the only way a headless Deck's
   *  failures reach a human — it cannot toast, so the console has to (Decisions.md §2). */
  problems?: AgentEvent[];
  escalatedConflicts?: Conflict[];
  onDismissProblem?: (id: string) => void;
  /** Navigate to Games and select a specific game — used by the notifications menu's deep links. */
  onOpenGame: (gameId: string | null) => void;
}

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
  onDismissProblem,
  onOpenGame,
}: Props) {
  const [syncCommandIds, setSyncCommandIds] = useState<string[]>([]);
  const [syncing, setSyncing] = useState(false);

  async function handleSyncAll() {
    if (machines.length === 0 || syncing) return;
    setSyncing(true);
    try {
      const queued = await api.queueSyncAll(machines.map(m => m.id));
      setSyncCommandIds(queued.map(c => c.id));
    } catch (e) {
      alert('Could not start Sync all: ' + (e as Error).message);
    } finally {
      setSyncing(false);
    }
  }

  return (
    <header className="bg-panel border-b border-line px-5 h-[72px] flex items-center justify-between sticky top-0 z-20">
      {/* Brand + version */}
      <div className="flex items-center gap-2.5">
        <a
          href="#"
          onClick={e => { e.preventDefault(); onViewChange('games'); }}
          className="flex items-center gap-[9px] select-none"
        >
          <img src={logoUrl} className="h-16 w-auto rounded-md flex-shrink-0" alt="SaveLocker" />
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
      <div className="flex items-center gap-1.5">
        {NAV_ITEMS.map(item => (
          <Button
            key={item.key}
            variant={view === item.key ? 'primary' : 'default'}
            size="sm"
            onClick={() => onViewChange(item.key)}
          >
            {item.label}
          </Button>
        ))}

        <div className="w-px h-5 bg-line mx-1" aria-hidden />

        {/* plan.md Surfaces: "Sync all primary". One filled button per view, per plan.md
            Components — this is the thing you most likely came to do. */}
        <Button variant="primary" size="sm" onClick={handleSyncAll} disabled={syncing || machines.length === 0}>
          {syncing ? 'Starting…' : 'Sync all'}
        </Button>
        {syncCommandIds.length > 0 && (
          <SyncAllProgress commandIds={syncCommandIds} onDone={() => setSyncCommandIds([])} />
        )}

        <Button variant="default" size="sm" style={{ fontSize: 14, lineHeight: 1 }} onClick={onRefresh} title="Refresh">
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
        <NotificationsMenu problems={problems} onOpenGame={onOpenGame} onDismissProblem={onDismissProblem} />

        <Button variant="quiet" size="sm" onClick={onLock} title="Lock — forget the password and sign in again">
          🔒
        </Button>
      </div>
    </header>
  );
}
