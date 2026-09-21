export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

const asUtc = (t: string) => /[Z+]|-\d\d:\d\d$/.test(t) ? t : t + 'Z'

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
  const normalized = /[Z+]/.test(iso.slice(-6)) ? iso : iso + 'Z'
  return new Date(normalized).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
