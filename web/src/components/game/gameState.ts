import type { AgentHealth, Command, GameSummary } from '../../types';
import type { DotTone } from '../ui/Dot';
import { withdrawable } from '../../syncAll';

/** `AgentEventCodes.Conflict` — shown as the conflict itself, never as a separate "problem". */
const CONFLICT_CODE = 'sync.conflict';

/** Games some machine has an open Warning or Error about (a missing folder, a failed push…). */
export function problemGameIds(health: AgentHealth[]): Set<string> {
  const ids = new Set<string>();
  for (const h of health)
    for (const e of h.openEvents)
      if (e.gameId && e.severity !== 'Info' && e.code !== CONFLICT_CODE) ids.add(e.gameId);
  return ids;
}

export interface GameStanding { tone: DotTone; label: string }

/**
 * One game's standing, by plan.md's colour rule: accent only while a decision waits (a conflict),
 * amber when a machine reported a problem that will retry, olive when server and machines agree,
 * and faint for a game nothing is expected of (disabled, or no save yet).
 */
export function standing(s: GameSummary, problems: Set<string>): GameStanding {
  if (s.hasOpenConflict) return { tone: 'crit', label: 'Conflict' };
  if (!s.game.enabled) return { tone: 'idle', label: 'Disabled' };
  if (problems.has(s.game.id)) return { tone: 'warn', label: 'Needs attention' };
  if (!s.head) return { tone: 'idle', label: 'No saves yet' };
  return { tone: 'ok', label: 'Synced' };
}

/**
 * A command the console queued for THIS game that has not finished — a Push or Pull from its page.
 * A console Sync all names no game (one command per machine), so it never lights a row: the console
 * cannot know which games a machine tracks (Group 8 decision, 2026-09-28). A claim whose lease lapsed
 * reads as queued again, because the next poll hands it out anew.
 */
export function liveCommand(gameId: string, commands: Command[]): { label: 'Queued' | 'Syncing'; command: Command } | null {
  const c = commands.find(x => x.gameId === gameId && (x.status === 'Pending' || x.status === 'Dispatched'));
  if (!c) return null;
  return { label: withdrawable(c) ? 'Queued' : 'Syncing', command: c };
}

/** plan.md "Games: sidebar list by default, grid wall as an alternative". Remembered per browser. */
export type Layout = 'list' | 'grid';
export const LAYOUT_OPTIONS = [
  { value: 'list' as const, label: 'List', icon: 'list' as const },
  { value: 'grid' as const, label: 'Grid', icon: 'layout-grid' as const },
];

export const POLICY_LABEL: Record<string, string> = {
  Manual: 'Manual',
  NewestWins: 'Newest wins',
  PreferMachine: 'Prefer machine',
};
