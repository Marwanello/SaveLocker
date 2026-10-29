import type { TrackedGame } from './types'

/**
 * One entry per game, one save tree per emulator (docs/tasks/_done/multi-emulator-games Phase 6) — the agent's side of the
 * console's titles. The server groups the games an emulated title is saved as; this machine lists such a title once,
 * and its page gets a tab per tree. Every tree is still its own game, and syncs as one.
 */
export interface GameTitle {
  /** The title's id, or the game's own for a game shown alone. */
  key: string
  name: string
  /** The game whose art the title shows. */
  lead: TrackedGame
  /** Its games, by emulator then edition. One for a game shown alone. */
  trees: TrackedGame[]
}

/** The emulator a tree is: how this machine found it, else what the server says. */
export const emulatorOf = (g: TrackedGame): string | null =>
  (g.source?.kind === 'emulator' ? g.source.detail : null) ?? g.emulators?.[0] ?? null

/** "RetroArch · Rev 0" — and, for two trees that would read alike, what else the game's name says ("RetroArch · USA"). */
export function treeLabel(tree: TrackedGame, trees: TrackedGame[]): string {
  const base = (t: TrackedGame) => [emulatorOf(t) ?? 'Game', t.title?.edition].filter(Boolean).join(' · ')
  if (!trees.some(t => t !== tree && base(t) === base(tree))) return base(tree)
  const title = tree.title?.name ?? ''
  const emulator = (emulatorOf(tree) ?? '').toLowerCase()
  const rest = (tree.name.toLowerCase().startsWith(title.toLowerCase()) ? tree.name.slice(title.length) : tree.name)
    .replace(/\(([^()]*)\)/g, (_, inner: string) => inner.split(',')[0].trim().toLowerCase() === emulator ? '' : inner)
    .replace(/\s+/g, ' ').trim()
  return rest ? `${base(tree)} · ${rest}` : base(tree)
}

/** The tracked games as titles, in input order. */
export function groupTitles(games: TrackedGame[]): GameTitle[] {
  const byKey = new Map<string, TrackedGame[]>()
  for (const g of games) {
    const key = g.title?.id ?? g.id
    const list = byKey.get(key)
    if (list) list.push(g); else byKey.set(key, [g])
  }
  return [...byKey.entries()].map(([key, list]) => {
    const trees = [...list].sort((a, b) =>
      (emulatorOf(a) ?? '').localeCompare(emulatorOf(b) ?? '', undefined, { sensitivity: 'base' })
      || (a.title?.edition ?? '').localeCompare(b.title?.edition ?? ''))
    const lead = trees.find(t => t.title?.lead) ?? trees[0]
    return { key, name: trees.length > 1 ? lead.title?.name ?? lead.name : lead.name, lead, trees }
  })
}

/** The title a game is a tree of, among `games`; a one-tree title for a game shown alone. */
export function titleOf(game: TrackedGame, games: TrackedGame[]): GameTitle {
  return groupTitles(games).find(t => t.trees.some(g => g.id === game.id)) ?? { key: game.id, name: game.name, lead: game, trees: [game] }
}
