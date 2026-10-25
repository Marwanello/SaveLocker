import type { GameSummary } from '../../types';
import { toMs } from '../../format';

/**
 * One console entry per game, one save tree per emulator (docs/tasks/multi-emulator-games). The server groups the games
 * an emulated title is saved as under one `titleId`; the console draws that title once, as a summary of its trees, so
 * the list, the wall, sort and group work on it unchanged. Every tree is still its own server game and syncs as one.
 */
export interface TitleSummary extends GameSummary {
  /** The title's id, or the game's own for a game shown alone. */
  titleKey: string;
  /** Its games, ordered by emulator then edition. One for a game shown alone. */
  trees: GameSummary[];
}

/** The emulator a tree is, as its tab and badges call it. */
export const emulatorOf = (s: GameSummary): string | null =>
  s.emulator ?? s.game.emulators?.[0] ?? s.sources?.find(x => x.kind === 'emulator')?.detail ?? null;

/**
 * "RetroArch · Rev 0": a tree's name inside its title. When two trees would read alike (two saves of one emulator), what
 * else the game's name says tells them apart — "RetroArch" and "RetroArch · USA" for "… (RetroArch)" and "… (USA) (RetroArch)".
 */
export function treeLabel(tree: GameSummary, trees: GameSummary[]): string {
  const base = (t: GameSummary) => [emulatorOf(t) ?? 'Game', t.game.titleEdition].filter(Boolean).join(' · ');
  if (!trees.some(t => t !== tree && base(t) === base(tree))) return base(tree);
  const title = tree.game.titleName ?? '';
  const emulator = (emulatorOf(tree) ?? '').toLowerCase();
  const rest = (tree.game.name.toLowerCase().startsWith(title.toLowerCase()) ? tree.game.name.slice(title.length) : tree.game.name)
    .replace(/\(([^()]*)\)/g, (_, inner: string) => inner.split(',')[0].trim().toLowerCase() === emulator ? '' : inner)
    .replace(/\s+/g, ' ').trim();
  return rest ? `${base(tree)} · ${rest}` : base(tree);
}

const newest = (a: GameSummary['head'], b: GameSummary['head']) =>
  !a ? b : !b ? a : toMs(b.createdAt) > toMs(a.createdAt) ? b : a;

/** The overview as titles: a game with no title is its own, one tree. Order follows the input's. */
export function groupTitles(games: GameSummary[]): TitleSummary[] {
  const byTitle = new Map<string, GameSummary[]>();
  const order: string[] = [];
  for (const s of games) {
    const key = s.game.titleId ?? s.game.id;
    const list = byTitle.get(key);
    if (list) list.push(s);
    else { byTitle.set(key, [s]); order.push(key); }
  }
  return order.map(key => {
    const trees = [...byTitle.get(key)!].sort((a, b) =>
      (emulatorOf(a) ?? '').localeCompare(emulatorOf(b) ?? '', undefined, { sensitivity: 'base' })
      || (a.game.titleEdition ?? '').localeCompare(b.game.titleEdition ?? ''));
    if (trees.length === 1) return { ...trees[0], titleKey: key, trees };
    const lead = trees.find(t => t.game.titleLead) ?? trees[0];
    const emulators = [...new Set(trees.map(t => emulatorOf(t)).filter((e): e is string => !!e))];
    const machineIds = [...new Set(trees.flatMap(t => t.machineIds ?? []))];
    const sources = trees.flatMap(t => t.sources ?? []);
    return {
      ...lead,
      titleKey: key,
      trees,
      game: { ...lead.game, name: lead.game.titleName ?? lead.game.name, emulators, enabled: trees.some(t => t.game.enabled) },
      head: trees.reduce<GameSummary['head']>((h, t) => newest(h, t.head), null),
      hasOpenConflict: trees.some(t => t.hasOpenConflict),
      totalStorageBytes: trees.reduce((n, t) => n + t.totalStorageBytes, 0),
      machineIds: machineIds.length ? machineIds : null,
      sources: sources.length ? sources : null,
      emulator: null,
    };
  });
}

/** The trees of a title (or a plain game), for code that may get either. */
export const treesOf = (s: GameSummary): GameSummary[] => (s as Partial<TitleSummary>).trees ?? [s];

/** Is this game, or any tree of this title, the one with `id`? */
export const holds = (s: GameSummary, id: string | null) => !!id && treesOf(s).some(t => t.game.id === id);

/** The emulators of a title, or a game's one; "Unknown emulator" for a console game none named. */
export const emulatorsOf = (s: GameSummary): string[] => {
  const list = [...new Set(treesOf(s).map(t => emulatorOf(t) ?? 'Unknown emulator'))];
  return list.length ? list : ['Unknown emulator'];
};
