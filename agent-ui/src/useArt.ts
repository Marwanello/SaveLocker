import { useEffect, useState } from 'react'
import { api } from './api'

/**
 * Cover and icon art through the agent's own art route. The browser cannot use the route as an
 * `<img src>` — the local API wants a token header an image request will not carry — so each image is
 * fetched with it and shown from a blob URL.
 *
 * One promise per (game, kind, width) for the life of the page: a grid of covers and the game page
 * that follows it ask for the same few images, and an image that failed (no art, server down) is
 * dropped from the cache so the next mount asks again rather than showing a blank for good.
 */
const cache = new Map<string, Promise<string | null>>()

function load(id: string, kind: 'grid' | 'icon', w: number): Promise<string | null> {
  const key = `${id}|${kind}|${w}`
  let hit = cache.get(key)
  if (!hit) {
    const p: Promise<string | null> = api.art(id, kind, w)
      .then(blob => (blob ? URL.createObjectURL(blob) : null))
      .catch(() => null)
    hit = p
    cache.set(key, p)
    void p.then(url => { if (!url && cache.get(key) === p) cache.delete(key) })
  }
  return hit
}

/** A blob URL for the game's art, or `null` while loading and when it has none. */
export function useArt(id: string, kind: 'grid' | 'icon', w: number): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let live = true
    setUrl(null)
    void load(id, kind, w).then(u => { if (live) setUrl(u) })
    return () => { live = false }
  }, [id, kind, w])
  return url
}
