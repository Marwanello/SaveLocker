import type { AgentHealth, Command, GameRelease, GameSummary } from '../../types';
import type { DotTone } from '../ui/Dot';
import { withdrawable } from '../../syncAll';
import { treesOf } from './titles';

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
 * The five standing buckets, by plan.md's colour rule: accent only while a decision waits (a conflict),
 * amber when a machine reported a problem that will retry, olive when server and machines agree, and
 * faint for a game nothing is expected of (disabled, or no save yet).
 */
export type StandingKey = 'conflict' | 'attention' | 'synced' | 'none' | 'disabled';

const STANDING: Record<StandingKey, GameStanding> = {
  conflict: { tone: 'crit', label: 'Conflict' },
  attention: { tone: 'warn', label: 'Needs attention' },
  synced: { tone: 'ok', label: 'Synced' },
  none: { tone: 'idle', label: 'No saves yet' },
  disabled: { tone: 'idle', label: 'Disabled' },
};

/** The bucket behind a game's standing — the console's Status grouping keys off this, so a group's
 *  label and the game's own dot can never tell two different stories. */
export function standingKey(s: GameSummary, problems: Set<string>): StandingKey {
  // A title stands as its worst tree (tasks/multi-emulator-games): a conflict in any emulator's save shows.
  const trees = treesOf(s);
  if (trees.some(t => t.hasOpenConflict)) return 'conflict';
  if (!trees.some(t => t.game.enabled)) return 'disabled';
  if (trees.some(t => problems.has(t.game.id))) return 'attention';
  if (!s.head) return 'none';
  return 'synced';
}

/** One game's standing — `STANDING[standingKey(s, problems)]`. */
export function standing(s: GameSummary, problems: Set<string>): GameStanding {
  return STANDING[standingKey(s, problems)];
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

/** "Europe, Australia" → "Europe": a release's first region, for a chip or a list row; the details name them all. */
export const regionLabel = (r: GameRelease) => r.region?.split(',')[0].trim() || null;
