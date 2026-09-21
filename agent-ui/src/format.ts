export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / (1024 * 1024)).toFixed(1)} MB`
}

/** A wall-clock time for an activity entry. The agent serialises UTC without a zone suffix on some
 *  paths, so a bare timestamp is treated as UTC rather than local — the same reading `formatTime`
 *  in the old activity card applied. */
export function formatTime(iso: string): string {
  const normalized = /[Z+]/.test(iso.slice(-6)) ? iso : iso + 'Z'
  return new Date(normalized).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
