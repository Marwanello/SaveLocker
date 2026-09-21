import { useSyncExternalStore } from 'react'
import { api } from './api'
import type { Activity, SyncActivitySnapshot } from './types'

/**
 * One shared poll of /api/activity for everything that shows what the agent is doing — the status
 * header's progress and the Overview's "Recent" card. It is cheap (an in-memory read on the agent's
 * side), so 1.5 s is fine, but two components each running their own timer would double it for no
 * reason, and the header is mounted on every page.
 *
 * plan.md Motion: "Progress updates must not re-render the surrounding view." The store is what
 * makes that structural rather than a matter of care. Each hook below subscribes to ONE slice, and a
 * poll keeps the previous object for whichever slice did not change — so a byte-progress tick
 * re-renders the header's progress and nothing else, the "Recent" list does not re-render at all, and
 * an idle agent (the same snapshot every tick) re-renders nothing.
 */
const POLL_MS = 1500

let snapshot: Activity | null = null
const listeners = new Set<() => void>()
let timer: ReturnType<typeof setInterval> | undefined
let running: Promise<void> | null = null
let generation = 0

const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b)

/** One fetch, folded into the snapshot. Never rejects. */
async function fetchOnce() {
  const started = generation
  try {
    const next = await api.activity()
    // Asked again since (refreshActivity): this answer was produced before the change the caller
    // knows about, and applying it would put the old state back on screen until the next poll.
    if (started !== generation) return
    const prev = snapshot
    const merged: Activity = {
      current: prev && same(prev.current, next.current) ? prev.current : next.current,
      recent: prev && same(prev.recent, next.recent) ? prev.recent : next.recent,
    }
    snapshot = merged
    if (!prev || merged.current !== prev.current || merged.recent !== prev.recent) listeners.forEach(l => l())
  } catch {
    // The agent restarts under this page while it applies an update; the next tick recovers.
  }
}

/** A tick that lands on a poll already in flight joins it. `force` is for a caller that knows the
 *  answer just changed: the poll in flight predates that, so its answer is discarded and a fresh one
 *  is asked for once it settles. Dropping the request instead left a finished sync reading as
 *  "Pushing…" until the next tick. */
function poll(force = false): Promise<void> {
  // A hidden window has nobody to show it to; the visibilitychange handler catches up on return.
  if (document.hidden) return Promise.resolve()
  if (force) generation++
  if (running) return force ? running.then(() => poll()) : running
  running = fetchOnce().finally(() => { running = null })
  return running
}

const onVisible = () => { if (!document.hidden) void poll() }

function subscribe(listener: () => void) {
  listeners.add(listener)
  if (listeners.size === 1) {
    void poll()
    timer = setInterval(() => void poll(), POLL_MS)
    document.addEventListener('visibilitychange', onVisible)
  }
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0) {
      clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }
}

/** Poll now rather than waiting for the next tick — for the moment a caller knows it just changed
 *  what the answer is (a sync it started has finished). */
export const refreshActivity = () => { void poll(true) }

/** What is syncing right now, with byte progress for a push. Re-renders on every progress tick. */
export function useActivityCurrent(): SyncActivitySnapshot | undefined {
  return useSyncExternalStore(subscribe, () => snapshot?.current)
}

/** Whether anything is syncing. A boolean, so a component that only needs this (the Sync all button)
 *  re-renders when it flips and not on every byte of progress in between. */
export function useActivityBusy(): boolean {
  return useSyncExternalStore(subscribe, () => (snapshot?.current.phase ?? 'Idle') !== 'Idle')
}

/** The rolling history, newest first. Unchanged by a progress tick. */
export function useActivityRecent(): Activity['recent'] | undefined {
  return useSyncExternalStore(subscribe, () => snapshot?.recent)
}
