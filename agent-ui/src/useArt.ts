import { useEffect, useState } from 'react'
import { api } from './api'

/**
 * Cover and icon art through the agent's own art route. The browser cannot use the route as an
 * `<img src>` — the local API wants a token header an image request will not carry — so each image is
 * fetched with it and shown from a blob URL.
 *
 * A grid of covers is one request per game, all wanted at once, so three things keep it from hurting
 * the rest of the page:
 *  - At most `MAX_IN_FLIGHT` at a time. A browser gives one origin only a handful of connections, and
 *    the same origin carries the 1.5 s activity poll and the Sync buttons. When the server is
 *    unreachable each art request can sit for seconds; unbounded, they would queue those out.
 *  - A request nobody is waiting for any more (the page changed) is not started if still queued, and
 *    is aborted if already in flight — the abort reaches the agent, which stops asking the server.
 *  - Results expire (`FRESH_MS`), so a cover re-picked in the console shows up without restarting
 *    the agent window, and a failure (no art, server unreachable) is asked again sooner (`RETRY_MS`).
 *
 * One shared entry per (game, kind, width): a grid and the game page that follows it ask for the same
 * few images. An entry's blob URL is revoked once it has been replaced and nothing on screen uses it.
 */
const FRESH_MS = 5 * 60_000
const RETRY_MS = 30_000
const MAX_IN_FLIGHT = 4

interface Entry {
  key: string
  promise: Promise<string | null>
  url: string | null
  /** When the request finished, or null while it is queued or in flight. */
  settledAt: number | null
  /** Mounted components using this entry. */
  waiters: number
  abort: AbortController
}

const cache = new Map<string, Entry>()
const queue: (() => void)[] = []
let inFlight = 0

const expired = (e: Entry) => e.settledAt !== null && Date.now() - e.settledAt > (e.url ? FRESH_MS : RETRY_MS)

function pump() {
  while (inFlight < MAX_IN_FLIGHT && queue.length > 0) queue.shift()!()
}

/** Forget an entry; free its blob unless something on screen still shows it (`release` frees it then). */
function drop(e: Entry) {
  if (cache.get(e.key) === e) cache.delete(e.key)
  if (e.url && e.waiters === 0) URL.revokeObjectURL(e.url)
}

function create(key: string, id: string, kind: 'grid' | 'icon', w: number): Entry {
  const entry: Entry = { key, promise: Promise.resolve(null), url: null, settledAt: null, waiters: 0, abort: new AbortController() }
  entry.promise = new Promise<string | null>(resolve => {
    queue.push(() => {
      if (entry.waiters === 0) {
        entry.settledAt = Date.now()
        drop(entry)
        resolve(null)
        return
      }
      inFlight++
      api.art(id, kind, w, entry.abort.signal)
        .then(blob => (blob ? URL.createObjectURL(blob) : null), () => null)
        .then(url => {
          inFlight--
          entry.url = url
          entry.settledAt = Date.now()
          resolve(url)
          pump()
        })
    })
  })
  return entry
}

function acquire(id: string, kind: 'grid' | 'icon', w: number): Entry {
  const key = `${id}|${kind}|${w}`
  let entry = cache.get(key)
  if (entry && expired(entry)) { drop(entry); entry = undefined }
  if (!entry) {
    entry = create(key, id, kind, w)
    cache.set(key, entry)
  }
  entry.waiters++
  pump()
  return entry
}

function release(entry: Entry) {
  if (--entry.waiters > 0) return
  if (entry.settledAt !== null) {
    if (cache.get(entry.key) !== entry && entry.url) URL.revokeObjectURL(entry.url)
    return
  }
  // Still queued or in flight, with nobody left to want it. A tick's grace first: React's dev-mode
  // double effect unmounts and remounts in the same commit, and that must not abort and re-ask.
  setTimeout(() => {
    if (entry.waiters === 0 && entry.settledAt === null) {
      entry.abort.abort()
      drop(entry)
    }
  }, 0)
}

/** A blob URL for the game's art, or `null` while loading and when it has none. */
export function useArt(id: string, kind: 'grid' | 'icon', w: number): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    let live = true
    setUrl(null)
    const entry = acquire(id, kind, w)
    void entry.promise.then(u => { if (live) setUrl(u) })
    return () => { live = false; release(entry) }
  }, [id, kind, w])
  return url
}
