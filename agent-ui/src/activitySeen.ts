import { asUtc } from './format'
import type { ActivityLogEntry } from './types'

const KEY = 'savelocker.activity.seenAt'

/** The same reading the feed's dots apply: a failure, a refusal or a conflict is a warning; the rest
 *  is plain history. One rule so the sidebar count and the dots on the page cannot disagree. */
export const isWarning = (message: string) =>
  /conflict|refused|failed|unreachable|blocked|error/i.test(message)

/** When the Activity page was last opened, or 0 if never (or if storage is unavailable — a private
 *  window, blocked site data — in which case every warning counts, which errs toward telling). */
function seenAt(): number {
  try { return Number(localStorage.getItem(KEY)) || 0 } catch { return 0 }
}

export function markActivitySeen(): void {
  try { localStorage.setItem(KEY, String(Date.now())) } catch { /* the badge just stays; harmless */ }
}

/** Warnings logged since the Activity page was last opened — the sidebar's count for it. */
export function unseenWarnings(recent: readonly ActivityLogEntry[] | undefined): number {
  const since = seenAt()
  return (recent ?? []).filter(e => isWarning(e.message) && new Date(asUtc(e.timestampUtc)).getTime() > since).length
}
