import type { View } from './types'

/**
 * Where a URL hash points. The one place a hash becomes a view — used at startup and on every
 * `hashchange`, which is what lets something outside the page (a toast button, the tray reopening a
 * window that is already up) take it to an exact screen instead of only ever working on a fresh load.
 *
 *   #overview  #games  #addGames  #conflicts  #settings   the sidebar's own views
 *   #conflicts:queue                                        the conflicts view, with the one-at-a-time
 *                                                           chooser raised straight away
 *   #game:<id>                                              one game's page
 *
 * Anything else lands on Games, as it always has. Never throws: this reads a value a person or another
 * process typed into a URL.
 */
export interface Route {
  view: View
  /** Set only by `#game:<id>`; the id is not checked here — an unknown one simply finds no game. */
  gameId: string | null
  /** `#conflicts:queue`: raise the chooser rather than just showing the list. */
  queue: boolean
}

const VIEWS: readonly View[] = ['overview', 'games', 'addGames', 'conflicts', 'settings']

export function parseRoute(hash: string): Route {
  const [base = '', rest = ''] = hash.replace(/^#/, '').split(/:(.*)/s)

  if (base === 'game' && rest) return { view: 'games', gameId: rest, queue: false }
  const view = (VIEWS as readonly string[]).includes(base) ? (base as View) : 'games'
  return { view, gameId: null, queue: view === 'conflicts' && rest === 'queue' }
}
