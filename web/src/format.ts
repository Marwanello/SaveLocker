/**
 * Time and size, formatted one way across the console. The server's timestamps are UTC but some arrive
 * without a zone designator (SQLite round-trips drop the `Z`), and `new Date()` reads a bare ISO string
 * as LOCAL time — off by the viewer's UTC offset. Everything below normalises first.
 */
export const asUtc = (t: string) => /[Z+]|-\d\d:\d\d$/.test(t) ? t : t + 'Z';

export const when = (t: string | null | undefined) => t ? new Date(asUtc(t)).toLocaleString() : '—';

const minutesSince = (t: string) => Math.max(0, Math.round((Date.now() - new Date(asUtc(t)).getTime()) / 60000));

/** "just now", "5m ago", "3h ago", "2d ago". */
export function ago(t: string | null | undefined): string {
  if (!t) return '—';
  const mins = minutesSince(t);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.round(hours / 24)}d ago`;
}

/** How long something has been open, compact: "12m", "4h", "4h 12m", "2d". */
export function age(t: string): string {
  const mins = minutesSince(t);
  if (mins < 60) return `${mins}m`;
  const h = Math.floor(mins / 60), m = mins % 60;
  if (h < 24) return m > 0 && h < 10 ? `${h}h ${m}m` : `${h}h`;
  return `${Math.floor(h / 24)}d`;
}

/**
 * Adaptive, because this is a decision aid. A fixed "MB" renders every small save as "0.00 MB",
 * which is exactly the case where a size is being read to tell two saves apart.
 */
export const fmtSize = (n: number) =>
  n < 1024 ? `${n} B`
    : n < 1024 * 1024 ? (n / 1024).toFixed(1) + ' KB'
      : n < 1024 * 1024 * 1024 ? (n / (1024 * 1024)).toFixed(2) + ' MB'
        : (n / (1024 * 1024 * 1024)).toFixed(2) + ' GB';

/** A version or command id, short enough to read and still unique in one game's list. */
export const shortId = (id: string | null | undefined) => id ? id.replace(/-/g, '').slice(0, 8) : '—';

export const plural = (n: number, noun: string, many = noun + 's') => `${n} ${n === 1 ? noun : many}`;
