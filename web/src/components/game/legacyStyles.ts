import type { CSSProperties } from 'react';

/**
 * The game page's inline styles, lifted out of GameDetail.tsx unchanged when it was split into one
 * component per card (Group 8b). Temporary: the Group 8c re-layout replaces every one of them with the
 * `ui/` kit, and this file goes with them.
 */
export const card = { background: 'var(--color-panel)', border: '1px solid var(--color-line)', borderRadius: 8, overflow: 'hidden' } as const;
export const cardHeader = { padding: '11px 18px', borderBottom: '1px solid var(--color-line)' } as const;
export const sectionLabel = { fontSize: 10, fontWeight: 700, color: 'var(--color-safe-ink)', letterSpacing: '0.12em', textTransform: 'uppercase' as const };
export const thStyle = { padding: '8px 18px', textAlign: 'left' as const, fontSize: 11, color: 'var(--color-dim)', fontWeight: 500 };
export const tdStyle = { padding: '11px 18px', fontSize: 13, fontWeight: 500 };
export const tdMono = { padding: '11px 18px', fontSize: 11, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace" };
export const rowSep = { borderTop: '1px solid var(--color-line)' };

export const ghostBtn = (extra?: CSSProperties): CSSProperties => ({
  padding: '2px 8px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent',
  borderRadius: 4, fontSize: 10, cursor: 'pointer', ...extra,
});
export const amberBtn: CSSProperties = { padding: '2px 8px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', background: 'transparent', borderRadius: 4, fontSize: 10, cursor: 'pointer' };
export const pillBtn = (active: boolean): CSSProperties => ({
  padding: '3px 10px', borderRadius: 4, fontSize: 11, fontWeight: 600, cursor: 'pointer',
  // The current tab is `selected` (neutral tile, not the accent) — plan.md: the accent stays the one action.
  border: '1px solid var(--color-line)',
  background: active ? 'var(--color-tile)' : 'transparent',
  color: active ? 'var(--color-fg)' : 'var(--color-dim)',
});
