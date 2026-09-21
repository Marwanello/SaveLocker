import { useArt } from '../useArt'

interface Props {
  id: string
  name: string
  /** `grid` is the 2:3 cover; `icon` is the square one, used in list rows. */
  kind: 'grid' | 'icon'
  /** One of the server's thumbnail widths (48, 64, 96, 128, 192, 256, 384). */
  w: number
}

/** A game's art through the agent, with the game's first letter on a tile while it loads and when
 *  there is none — a game with no SteamGridDB match must still be a clickable, findable thing. */
export function GameArt({ id, name, kind, w }: Props) {
  const url = useArt(id, kind, w)
  if (url) return <img className="sl-art" src={url} alt="" />
  return <span className="sl-art--fallback" aria-hidden="true">{(name.trim()[0] ?? '?').toUpperCase()}</span>
}
