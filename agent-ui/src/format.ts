export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

/** "4h 12m" / "35m" / "20s" for a span in milliseconds — how long something has been waiting. */
export function formatSpan(ms: number): string {
  const s = Math.max(0, Math.round(ms / 1000))
  if (s < 60) return `${s}s`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m`
  const h = Math.floor(m / 60)
  return m % 60 === 0 ? `${h}h` : `${h}h ${m % 60}m`
}

/** Server timestamps have no zone suffix but are UTC (System.Text.Json's default) — without this a
 *  browser in a non-UTC zone parses them as local time and every "when" is wrong by the offset. */
export const asUtc = (t: string) => /[Z+]|-\d\d:\d\d$/.test(t) ? t : t + 'Z'

/** "12m ago" / "2h ago" / "3d ago" for a server timestamp. */
export function formatAgo(t: string): string {
  const mins = Math.round((Date.now() - new Date(asUtc(t)).getTime()) / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.round(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.round(hours / 24)}d ago`
}

export const formatDateTime = (t: string) => new Date(asUtc(t)).toLocaleString()

/** A wall-clock time for an activity entry. The agent serialises UTC without a zone suffix on some
 *  paths, so a bare timestamp is treated as UTC rather than local — the same reading `formatTime`
 *  in the old activity card applied. */
export function formatTime(iso: string): string {
  return new Date(asUtc(iso)).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
