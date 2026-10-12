import type { GameSummary, Machine } from '../../types';
import { toMs } from '../../format';
import { standingKey } from './gameState';
import type { StandingKey } from './gameState';

/**
 * The Games tab's arrange state (Option A of docs/tasks/console-games-sort-group/mockup.html):
 * two compact dropdowns that group and sort the list/wall. Everything here is pure and view-only —
 * grouping changes nothing but what is drawn; Sync all, commands and counts never look at it.
 *
 * v1 deliberately has no "Date added": `Game` carries no created column, so the only honest date is
 * the head's — "Date synced". Adding the column is the mockup's known follow-up.
 */
export type SortBy = 'name' | 'synced' | 'size';
export type SortDir = 1 | -1;
export type GroupBy = 'none' | 'device' | 'platform' | 'source' | 'synced' | 'status';

export interface OrganizeState { groupBy: GroupBy; sortBy: SortBy; dir: SortDir }

export const DEFAULT_ORGANIZE: OrganizeState = { groupBy: 'none', sortBy: 'name', dir: 1 };

export const GROUP_OPTIONS: { value: GroupBy; label: string }[] = [
  { value: 'none', label: 'None' },
  { value: 'device', label: 'Device' },
  { value: 'platform', label: 'Platform' },
  { value: 'source', label: 'Source' },
  { value: 'synced', label: 'Date synced' },
  { value: 'status', label: 'Status' },
];

export const SORT_OPTIONS: { value: SortBy; label: string }[] = [
  { value: 'name', label: 'Name' },
  { value: 'synced', label: 'Recently synced' },
  { value: 'size', label: 'Size' },
];

/** The natural direction each sort starts at; picking a sort resets to its own default. */
export const DEFAULT_DIR: Record<SortBy, SortDir> = { name: 1, synced: -1, size: -1 };

export function dirLabel(sortBy: SortBy, dir: SortDir): string {
  if (sortBy === 'name') return dir === 1 ? 'A–Z' : 'Z–A';
  if (sortBy === 'size') return dir === 1 ? 'Smallest first' : 'Largest first';
  return dir === 1 ? 'Oldest first' : 'Newest first';
}

export const groupLabel = (g: GroupBy) => GROUP_OPTIONS.find(o => o.value === g)?.label ?? g;
export const sortLabel = (s: SortBy) => SORT_OPTIONS.find(o => o.value === s)?.label ?? s;

/** One heading's games; `label` is '' when the group has no emulator split. */
export interface OrganizeSubGroup { id: string; label: string; entries: GameSummary[] }
export interface OrganizeGroup { id: string; label: string; subGroups: OrganizeSubGroup[] }

export interface OrganizeContext {
  machines: Machine[];
  /** Games a machine has an open problem about — the Status grouping's "Needs attention" bucket. */
  problems: Set<string>;
}

const SOURCE_ORDER = ['steam', 'heroic', 'playnite', 'emulator', 'save-folder', 'manual'];
const SOURCE_LABEL: Record<string, string> = {
  steam: 'Steam', heroic: 'Heroic', playnite: 'Playnite',
  emulator: 'Emulator', 'save-folder': 'Save-folder scan', manual: 'Added by hand',
};
const STATUS_ORDER: StandingKey[] = ['conflict', 'attention', 'synced', 'none', 'disabled'];
const STATUS_LABEL: Record<StandingKey, string> = {
  conflict: 'Conflict', attention: 'Needs attention', synced: 'Synced',
  none: 'No saves yet', disabled: 'Disabled',
};
const DATE_BUCKETS = ['Today', 'Yesterday', 'Earlier this week', 'This month', 'Earlier'];

/** Rolling windows, not calendar weeks (the maintainer's pick): Today, Yesterday, ≤7 days, ≤30, older. */
function bucketOf(ms: number): string {
  const days = Math.floor((Date.now() - ms) / 86400e3);
  return days <= 0 ? 'Today' : days === 1 ? 'Yesterday' : days < 7 ? 'Earlier this week' : days < 31 ? 'This month' : 'Earlier';
}

/** Total storage for size, head's upload time for synced; a game with no saves is null either way. */
function sortValueOf(s: GameSummary, sortBy: SortBy): string | number | null {
  if (sortBy === 'name') return s.game.name;
  if (sortBy === 'synced') return s.head ? toMs(s.head.createdAt) : null;
  // `totalStorageBytes` is a plain number (0 without saves), so keying off `head` is what makes the
  // no-saves-last rule above hold for Size too, in both directions.
  return s.head ? s.totalStorageBytes : null;
}

/** Sorted by the chosen key; games with no saves always sort last, whichever direction is chosen. */
export function sortGames(games: GameSummary[], sortBy: SortBy, dir: SortDir): GameSummary[] {
  return [...games].sort((a, b) => {
    const av = sortValueOf(a, sortBy);
    const bv = sortValueOf(b, sortBy);
    if (av === null || bv === null) return av === null && bv === null ? 0 : av === null ? 1 : -1;
    const cmp = typeof av === 'string' && typeof bv === 'string'
      ? av.localeCompare(bv, undefined, { sensitivity: 'base' })
      : (av as number) - (bv as number);
    return cmp * dir;
  });
}

function flat(id: string, label: string, entries: GameSummary[]): OrganizeGroup {
  return { id, label, subGroups: [{ id, label: '', entries }] };
}

const primarySource = (s: GameSummary): string | null => {
  const kinds = new Set((s.sources ?? []).map(x => x.kind));
  return SOURCE_ORDER.find(k => kinds.has(k)) ?? (s.game.emulators?.length ? 'emulator' : null);
};

/** The games as headings, already sorted within each. Sorting runs over the full list first, so the
 *  filters that build each heading preserve the order. */
export function organize(games: GameSummary[], state: OrganizeState, ctx: OrganizeContext): OrganizeGroup[] {
  const sorted = sortGames(games, state.sortBy, state.dir);

  if (state.groupBy === 'none') return [flat('all', '', sorted)];

  if (state.groupBy === 'device') {
    // Overlap on purpose (maintainer's pick): a game on two machines appears under both, so group
    // counts sum above the library size — "what is on my Deck" is true by looking.
    const groups: OrganizeGroup[] = [];
    const placed = new Set<string>();
    for (const m of ctx.machines) {
      const entries = sorted.filter(s => s.machineIds?.includes(m.id) || s.head?.machineId === m.id);
      entries.forEach(s => placed.add(s.game.id));
      if (entries.length) groups.push(flat('dev:' + m.id, m.name, entries));
    }
    const orphans = sorted.filter(s => !placed.has(s.game.id));
    if (orphans.length) groups.push(flat('dev:none', 'No machine yet', orphans));
    return groups;
  }

  if (state.groupBy === 'platform') {
    const byConsole = new Map<string, GameSummary[]>();
    for (const s of sorted) {
      // An emulator game's console is its release's; anything else is a plain PC game.
      const consoleName = s.game.release?.console ?? (s.game.emulators?.length ? 'Emulator' : 'PC');
      const list = byConsole.get(consoleName);
      if (list) list.push(s); else byConsole.set(consoleName, [s]);
    }
    const names = [...byConsole.keys()].sort((a, b) => a === 'PC' ? -1 : b === 'PC' ? 1 : a.localeCompare(b));
    return names.map(consoleName => {
      const entries = byConsole.get(consoleName)!;
      // When a console was reached through more than one emulator (Dolphin and PrimeHack for
      // GameCube), the group splits into emulator sub-headings; a game whose emulator nobody
      // recorded lands in its own "Unknown emulator" heading rather than silently vanishing.
      const emulators = [...new Set(entries.map(s => s.game.emulators?.[0] ?? 'Unknown emulator'))];
      const subGroups = emulators.length > 1
        ? emulators.map(e => ({
            id: `plat:${consoleName}:${e}`, label: e,
            entries: entries.filter(s => (s.game.emulators?.[0] ?? 'Unknown emulator') === e),
          }))
        : [{ id: `plat:${consoleName}`, label: '', entries }];
      return { id: 'plat:' + consoleName, label: consoleName, subGroups };
    });
  }

  if (state.groupBy === 'source') {
    const bySource = new Map<string, GameSummary[]>();
    const unknown: GameSummary[] = [];
    for (const s of sorted) {
      const kind = primarySource(s);
      if (!kind) { unknown.push(s); continue; }
      const list = bySource.get(kind);
      if (list) list.push(s); else bySource.set(kind, [s]);
    }
    const groups = SOURCE_ORDER.filter(k => bySource.has(k))
      .map(k => flat('src:' + k, SOURCE_LABEL[k], bySource.get(k)!));
    if (unknown.length) groups.push(flat('src:unknown', 'No source recorded', unknown));
    return groups;
  }

  if (state.groupBy === 'synced') {
    const groups: OrganizeGroup[] = [];
    for (const bucket of DATE_BUCKETS) {
      const entries = sorted.filter(s => s.head && bucketOf(toMs(s.head.createdAt)) === bucket);
      if (entries.length) groups.push(flat('syn:' + bucket, bucket, entries));
    }
    const never = sorted.filter(s => !s.head);
    if (never.length) groups.push(flat('syn:never', 'Never synced', never));
    return groups;
  }

  return STATUS_ORDER
    .map(key => ({ key, entries: sorted.filter(s => standingKey(s, ctx.problems) === key) }))
    .filter(x => x.entries.length > 0)
    .map(x => flat('st:' + x.key, STATUS_LABEL[x.key], x.entries));
}
