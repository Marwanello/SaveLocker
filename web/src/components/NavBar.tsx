import { useState } from 'react';
import { getPassword, setPassword as persistPassword } from '../api';
import type { AgentEvent, Conflict, ServerBuildInfo } from '../types';
import logoUrl from '../assets/SaveLocker_Logo_crop.png';
import { Button } from './ui/Button';
import { Chip } from './ui/Chip';

type View = 'games' | 'config' | 'audit' | 'help' | 'whats-new';

/**
 * `AgentEventCodes.Conflict` on the server. The one reported condition that never self-heals: every
 * other event auto-closes when the machine syncs that game cleanly again, so it is treated
 * differently below.
 */
const CONFLICT_CODE = 'sync.conflict';

interface Props {
  view: View;
  onViewChange: (v: View) => void;
  onConnect: () => void;
  onRefresh: () => void;
  /** What this console is running. Undefined until /api/admin/status answers. */
  build?: ServerBuildInfo;
  /** True when the running release's notes have not been opened yet. */
  unreadNotes?: boolean;
  /** Open problems reported by agents, worst first. This is the only way a headless Deck's
   *  failures reach a human — it cannot toast, so the console has to (Decisions.md §2). */
  problems?: AgentEvent[];
  escalatedConflicts?: Conflict[];
  onDismissProblem?: (id: string) => void;
}

const NAV_ITEMS: { key: View; label: string }[] = [
  { key: 'games', label: 'Games' },
  { key: 'config', label: 'Configuration' },
  { key: 'audit', label: 'Audit Log' },
  { key: 'help', label: 'Help' },
  { key: 'whats-new', label: "What's New" },
];

const asUtcTime = (t: string) => /[Z+]/.test(t.slice(-6)) ? t : t + 'Z';

function ago(t: string): string {
  const mins = Math.max(0, Math.round((Date.now() - new Date(asUtcTime(t)).getTime()) / 60000));
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

/** plan.md colour rule: healthy → safe, retrying → watch, a decision waiting → accent. Info-only
 *  problems are neither retrying nor a decision, so they read as "healthy, just for your info". */
type Tone = 'ok' | 'warn' | 'crit';

function severityTone(s: AgentEvent['severity']): Tone {
  return s === 'Error' ? 'crit' : s === 'Warning' ? 'warn' : 'ok';
}
const severityLabel = (s: AgentEvent['severity']) =>
  s === 'Error' ? 'ERROR' : s === 'Warning' ? 'WARN' : 'INFO';

// Full literal class strings — Tailwind's build-time scanner reads source text, not runtime
// template interpolation, so a `border-${tone}` string would silently generate no CSS.
const BADGE_TONE: Record<Tone, string> = {
  ok: 'border-safe text-safe',
  warn: 'border-watch text-watch',
  crit: 'border-accent text-accent',
};

export function NavBar({
  view,
  onViewChange,
  onConnect,
  onRefresh,
  build,
  unreadNotes = false,
  problems = [],
  escalatedConflicts = [],
  onDismissProblem,
}: Props) {
  const [keyInput, setKeyInput] = useState(getPassword());
  const [showProblems, setShowProblems] = useState(false);

  function handleConnect() {
    persistPassword(keyInput.trim());
    onConnect();
  }

  // Info events (e.g. "an update was applied") are routine confirmations, not problems — they
  // never drive the badge's tone or count it up as alarming.
  const actionable = problems.filter(p => p.severity !== 'Info');
  const errorCount = actionable.filter(p => p.severity === 'Error').length;
  const badgeTone: Tone = actionable.length === 0 ? 'ok' : errorCount > 0 ? 'crit' : 'warn';

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

        {/* API Key composite input */}
        <div className="flex items-center bg-ink border border-line rounded-md overflow-hidden">
          <span className="px-2.5 py-[5px] text-[10px] text-faint font-mono border-r border-line select-none">
            PASSWORD
          </span>
          <input
            type="password"
            value={keyInput}
            onChange={e => setKeyInput(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleConnect()}
            className="px-2.5 py-[5px] bg-transparent text-fg border-0 text-[11px] font-mono w-40"
          />
        </div>

        <Button variant="primary" size="sm" onClick={handleConnect}>Connect</Button>

        <Button variant="default" size="sm" style={{ fontSize: 14, lineHeight: 1 }} onClick={onRefresh} title="Refresh">
          ↻
        </Button>

        {escalatedConflicts.length > 0 && (
          <Button
            variant="alert"
            size="sm"
            onClick={() => onViewChange('games')}
            title="These conflicts have been unresolved for more than six hours"
          >
            Overdue conflicts: {escalatedConflicts.length}
          </Button>
        )}

        {/* Agent problems. Absent when there are none — a healthy fleet should be quiet. */}
        {problems.length > 0 && (
          <div className="relative">
            <button
              onClick={() => setShowProblems(v => !v)}
              title="Problems reported by agents"
              className={`flex items-center gap-1.5 px-3 py-[5px] rounded-md border text-xs font-semibold bg-transparent ${BADGE_TONE[badgeTone]}`}
            >
              {actionable.length > 0 ? '⚠' : 'ⓘ'} {problems.length}
            </button>

            {showProblems && (
              <div className="absolute right-0 top-[calc(100%+8px)] w-[460px] bg-panel border border-line rounded-lg shadow-2xl z-30 overflow-hidden">
                <div className="px-3.5 py-2.5 border-b border-line flex justify-between items-center">
                  <span className="text-[12.5px] font-semibold text-fg">Agent problems</span>
                  <span className="text-[11px] text-dim">reported by the machines themselves</span>
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
                        <div className="text-[10.5px] text-faint mt-[3px] font-mono">
                          {p.code} · {ago(p.lastSeen)}{p.count > 1 ? ` · ×${p.count}` : ''}
                        </div>
                      </div>

                      {/* A conflict is NOT dismissible, and this is the difference that cost a real
                          user a day of play. Every other agent event self-heals — a machine that
                          recovers auto-closes it — so Dismiss is honest for them. A conflict does
                          not: it sits until a human resolves it. Offering the same grey Dismiss
                          button made "I made the warning go away" indistinguishable from "I fixed
                          it". Send them to the one place it can actually be resolved instead. */}
                      {p.code === CONFLICT_CODE ? (
                        <Button
                          variant="alert"
                          size="sm"
                          className="flex-shrink-0"
                          onClick={() => { setShowProblems(false); onViewChange('games'); }}
                          title="A conflict does not clear on its own — it has to be resolved on the game."
                        >
                          Resolve
                        </Button>
                      ) : onDismissProblem && (
                        <Button
                          variant="quiet"
                          size="sm"
                          className="flex-shrink-0"
                          style={{ borderColor: 'var(--color-line)' }}
                          onClick={() => onDismissProblem(p.id)}
                          title="Dismiss. If the condition still holds, the agent will report it again."
                        >
                          Dismiss
                        </Button>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </div>
    </header>
  );
}
