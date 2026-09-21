// The look of this agent window: theme, accent, app mark. Three short ids — the console's (pushed on the
// heartbeat) or, when "Follow the console" is off, this machine's own — see docs/tasks/checkpoint-ui/plan.md.
// This module is the one place the ids mean anything visual.
//
// HAND-KEPT COPY of web/src/appearance.ts (the source of truth for the ids, accents and mark geometry) and,
// for the accent triples, of src/Agent.Core/AppearancePalette.cs — three packages, no shared build. Change all
// of them together; tests/run-appearance-consistency-tests.ps1 fails when they drift.

import { useSyncExternalStore } from 'react'

export type Theme = 'system' | 'dark' | 'light'
export type AccentId = 'ember' | 'coolant' | 'arcade' | 'cobalt' | 'stealth'
export type MarkId = 'pixel' | 'cartridge' | 'memcard'

export interface Look {
  theme: Theme
  accent: AccentId
  mark: MarkId
}

export const DEFAULT_LOOK: Look = { theme: 'system', accent: 'ember', mark: 'pixel' }

export const THEMES: { id: Theme; name: string }[] = [
  { id: 'system', name: 'System' },
  { id: 'dark', name: 'Dark' },
  { id: 'light', name: 'Light' },
]

/** `dark`/`light` are the accent on that surface, `dOn`/`lOn` the ink that sits on it (plan.md tokens). */
export const ACCENTS: Record<AccentId, { name: string; dark: string; dOn: string; light: string; lOn: string }> = {
  ember:   { name: 'Ember',   dark: '#e0533c', dOn: '#160f0e', light: '#c0432c', lOn: '#fff8f6' },
  coolant: { name: 'Coolant', dark: '#35a5bd', dOn: '#08171b', light: '#12768d', lOn: '#f2fbfd' },
  arcade:  { name: 'Arcade',  dark: '#d4589b', dOn: '#1a0d15', light: '#b23c7c', lOn: '#fff5fa' },
  cobalt:  { name: 'Cobalt',  dark: '#5b81d6', dOn: '#0a0f1c', light: '#3a5cbe', lOn: '#f6f8ff' },
  stealth: { name: 'Stealth', dark: '#dcd7cc', dOn: '#141416', light: '#2e2b28', lOn: '#faf8f5' },
}

/** The three marks' geometry in a 32-unit box, exactly web/src/assets/marks/*.svg. `body` fills the
 *  shape, `punch` is the detail cut into it. Strings, not JSX, so the favicon can be built from the same
 *  source as the on-page <Mark>; they contain only constants and the two colours passed in. */
export const MARKS: { id: MarkId; name: string; draw: (body: string, punch: string) => string }[] = [
  {
    id: 'pixel', name: 'Pixel lock',
    draw: (a, o) =>
      `<rect x="12" y="3" width="8" height="4" fill="${a}"/><rect x="8" y="7" width="4" height="5" fill="${a}"/>` +
      `<rect x="20" y="7" width="4" height="5" fill="${a}"/><rect x="5" y="12" width="22" height="16" fill="${a}"/>` +
      `<rect x="14" y="16" width="4" height="4" fill="${o}"/><rect x="15" y="20" width="2" height="5" fill="${o}"/>`,
  },
  {
    id: 'cartridge', name: 'Cartridge',
    draw: (a, o) =>
      `<rect x="5" y="5" width="22" height="22" rx="4" fill="${a}"/><rect x="8.5" y="8.5" width="15" height="8" rx="2" fill="${o}"/>` +
      `<circle cx="16" cy="12.5" r="2.1" fill="${a}"/>` +
      `<path d="M10.5 27v-3.4M16 27v-3.4M21.5 27v-3.4" stroke="${o}" stroke-width="2" stroke-linecap="round" opacity=".65"/>`,
  },
  {
    id: 'memcard', name: 'Memory card',
    draw: (a, o) =>
      `<path d="M11 3.6h13.4A3.6 3.6 0 0 1 28 7.2v17.6a3.6 3.6 0 0 1-3.6 3.6H7.6A3.6 3.6 0 0 1 4 24.8V10.6z" fill="${a}"/>` +
      `<rect x="7.4" y="11.4" width="5.6" height="12.6" rx="1.6" fill="${o}"/>` +
      `<path d="M7.4 15.2h5.6M7.4 18.6h5.6M7.4 22h5.6" stroke="${a}" stroke-width="1.5"/>` +
      `<circle cx="20.6" cy="15.4" r="2.6" fill="${o}"/><path d="M19.3 17.4h2.6l.9 5.4h-4.4z" fill="${o}"/>`,
  },
]

const has = <T extends string>(ids: readonly T[], v: unknown): v is T => typeof v === 'string' && (ids as readonly string[]).includes(v)

/** A known-good Look from anything: each field lower-cased and, when it is not a recognised id, the
 *  default for that field. A value from the server or storage never needs defending against downstream. */
export function normalizeLook(v: Partial<Record<keyof Look, unknown>> | null | undefined): Look {
  const low = (x: unknown) => (typeof x === 'string' ? x.trim().toLowerCase() : x)
  const theme = low(v?.theme), accent = low(v?.accent), mark = low(v?.mark)
  return {
    theme: has(THEMES.map(t => t.id), theme) ? theme : DEFAULT_LOOK.theme,
    accent: has(Object.keys(ACCENTS) as AccentId[], accent) ? accent : DEFAULT_LOOK.accent,
    mark: has(MARKS.map(m => m.id), mark) ? mark : DEFAULT_LOOK.mark,
  }
}

const prefersLight = () => window.matchMedia?.('(prefers-color-scheme: light)').matches ?? false

/** Whether the palette in force is the light one: an explicit choice, else what the OS asks for. */
export function isLight(theme: Theme): boolean {
  return theme === 'light' || (theme === 'system' && prefersLight())
}

/** The mark as a standalone SVG document string, on an accent tile — the favicon. Solid: the tile is the
 *  accent and the mark is drawn in its ink, with the punch cut back to the tile colour. */
export function faviconSvg(look: Look): string {
  const c = ACCENTS[look.accent]
  const tile = isLight(look.theme) ? c.light : c.dark
  const ink = isLight(look.theme) ? c.lOn : c.dOn
  const mark = MARKS.find(m => m.id === look.mark)!
  return `<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">` +
    `<rect width="32" height="32" rx="6.5" fill="${tile}"/>${mark.draw(ink, tile)}</svg>`
}

let stopWatchingOs: (() => void) | null = null

/**
 * Put a look on the page: the theme attribute, the accent variables and the favicon. Idempotent, and
 * cheap enough to call on every poll that returns the same look.
 *
 * Theme: 'system' removes the attribute so the CSS follows `prefers-color-scheme` on its own, with no
 * flash and no script needed to keep it right. Accent: Ember is the stylesheet's own default and needs
 * nothing set; any other accent is written as two inline custom properties on <html> — which beat the
 * stylesheet — so they must be the value for the palette actually in force, and are re-derived when the
 * OS flips scheme while the theme is 'system'.
 */
export function applyLook(input: Look): void {
  const look = normalizeLook(input)
  const root = document.documentElement

  if (look.theme === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', look.theme)

  const paint = () => {
    const c = ACCENTS[look.accent]
    if (look.accent === 'ember') {
      root.style.removeProperty('--color-accent')
      root.style.removeProperty('--color-on-accent')
    } else {
      const light = isLight(look.theme)
      root.style.setProperty('--color-accent', light ? c.light : c.dark)
      root.style.setProperty('--color-on-accent', light ? c.lOn : c.dOn)
    }
    setFavicon(look)
  }
  paint()

  stopWatchingOs?.()
  stopWatchingOs = null
  if (look.theme === 'system' && window.matchMedia) {
    const mq = window.matchMedia('(prefers-color-scheme: light)')
    mq.addEventListener('change', paint)
    stopWatchingOs = () => mq.removeEventListener('change', paint)
  }
}

function setFavicon(look: Look) {
  // Only the SVG link is swapped; the PNG/ICO links stay as the static Ember fallbacks for browsers that
  // ignore SVG icons. A data: URL, because the agent UI is served with no other image origin to point at.
  const href = `data:image/svg+xml,${encodeURIComponent(faviconSvg(look))}`
  let link = document.querySelector<HTMLLinkElement>('link[rel="icon"][type="image/svg+xml"]')
  if (!link) {
    link = document.createElement('link')
    link.rel = 'icon'
    link.type = 'image/svg+xml'
    document.head.appendChild(link)
  }
  link.href = href
}

// A per-viewer convenience, never the source of truth: the server holds the look, this only lets the
// first paint match it before the local API answers.
const KEY = 'sl_agent_look'

export function loadStoredLook(): Look {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? normalizeLook(JSON.parse(raw)) : DEFAULT_LOOK
  } catch {
    return DEFAULT_LOOK
  }
}

export function storeLook(look: Look): void {
  try { localStorage.setItem(KEY, JSON.stringify(look)) } catch { /* private window / blocked storage */ }
}

// ---- the live look -------------------------------------------------------------------------------
// One store, so a <Mark> or anything else that cares re-renders when the look changes, without context or
// prop drilling. The DOM side (theme attribute, accent variables, favicon) is applyLook above.

let current: Look = DEFAULT_LOOK
const listeners = new Set<() => void>()

/** Boot: paint whatever this viewer last saw, before the first render — no flash of the default. */
export function initLook(): void {
  current = loadStoredLook()
  applyLook(current)
}

export function getLook(): Look {
  return current
}

/** Adopt a look (from the server or from the local API): paint it, remember it, tell subscribers. A no-op
 *  when nothing changed, so a poll that keeps returning the same look costs nothing. */
export function setLook(next: Partial<Record<keyof Look, unknown>>): void {
  const look = normalizeLook(next)
  if (look.theme === current.theme && look.accent === current.accent && look.mark === current.mark) return
  current = look
  applyLook(look)
  storeLook(look)
  listeners.forEach(l => l())
}

function subscribe(cb: () => void) {
  listeners.add(cb)
  return () => { listeners.delete(cb) }
}

export function useLook(): Look {
  return useSyncExternalStore(subscribe, getLook)
}
