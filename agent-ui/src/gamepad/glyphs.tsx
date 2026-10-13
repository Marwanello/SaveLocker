import { useSyncExternalStore } from 'react'
import { GLYPH_BOUNDS } from './pack/bounds'

/**
 * Controller glyphs, drawn from Julio Cacko's "Free Input Pack" (Universal Version) — the P5Gamepad
 * set for PlayStation, the XGamepad set for Xbox, each in its five style variants (Default, Light,
 * Alt, Alt 2, Retro). The pack's White files are the default because the coloured ones fight the
 * Checkpoint palette; a Color tone is available per-device for anyone who prefers it.
 *
 * Detection is best-effort from `gamepad.id` (a Linux Steam Input pad reports as an X360-class
 * device, a DualSense reports its own name) with a manual override — all three preferences are kept
 * per device, because they describe the controller in someone's hands, not the fleet.
 */
export type GlyphSet = 'auto' | 'xbox' | 'ps'
export type ResolvedGlyphSet = 'xbox' | 'ps'
export type GlyphVariant = 'default' | 'light' | 'alt' | 'alt2' | 'retro'
export type GlyphTone = 'white' | 'color'
export type GlyphId = 'a' | 'b' | 'x' | 'y' | 'lb' | 'rb' | 'lt' | 'rt' | 'l3' | 'view' | 'start' | 'move' | 'stick' | 'rstick' | 'r3'

export const GLYPH_VARIANTS: { id: GlyphVariant; label: string }[] = [
  { id: 'default', label: 'Default' },
  { id: 'light', label: 'Light' },
  { id: 'alt', label: 'Alt' },
  { id: 'alt2', label: 'Alt 2' },
  { id: 'retro', label: 'Retro' },
]

export interface GlyphPrefs {
  set: GlyphSet
  variant: GlyphVariant
  tone: GlyphTone
  /** Size in px for glyphs drawn on controls and previews. The hint bar is fixed at 15 px. */
  px: number
}

const SET_KEY = 'savelocker.pad.glyphSet'
const VARIANT_KEY = 'savelocker.pad.glyphVariant'
const TONE_KEY = 'savelocker.pad.glyphTone'
/** `.v2`: the default moved 37 → 30 → 18; a stored old value would hide the new default. */
const PX_KEY = 'savelocker.pad.glyphPx.v2'

export const GLYPH_SIZE_MIN = 18
export const GLYPH_SIZE_MAX = 56
export const GLYPH_SIZE_DEFAULT = 18
/** The hint bar's glyphs — smaller, so the strip stays compact. */
export const GLYPH_BAR_SIZE = 15

function read<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    return allowed.includes(raw as T) ? (raw as T) : fallback
  } catch { return fallback }
}

function readPx(): number {
  try {
    const raw = Number.parseFloat(localStorage.getItem(PX_KEY) ?? '')
    if (Number.isFinite(raw)) return clampPx(raw)
  } catch { /* below */ }
  return GLYPH_SIZE_DEFAULT
}

function clampPx(v: number): number {
  return Math.round(Math.min(GLYPH_SIZE_MAX, Math.max(GLYPH_SIZE_MIN, v)))
}

const listeners = new Set<() => void>()
let prefs: GlyphPrefs = {
  set: read<GlyphSet>(SET_KEY, ['auto', 'xbox', 'ps'], 'auto'),
  variant: read<GlyphVariant>(VARIANT_KEY, ['default', 'light', 'alt', 'alt2', 'retro'], 'default'),
  tone: read<GlyphTone>(TONE_KEY, ['white', 'color'], 'white'),
  px: readPx(),
}

function emit() { listeners.forEach(l => l()) }

export function setGlyphOverride(set: GlyphSet): void {
  prefs = { ...prefs, set }
  try { localStorage.setItem(SET_KEY, set) } catch { /* private window: lasts this visit */ }
  emit()
}

export function setGlyphVariant(variant: GlyphVariant): void {
  prefs = { ...prefs, variant }
  try { localStorage.setItem(VARIANT_KEY, variant) } catch { /* above */ }
  emit()
}

export function setGlyphTone(tone: GlyphTone): void {
  prefs = { ...prefs, tone }
  try { localStorage.setItem(TONE_KEY, tone) } catch { /* above */ }
  emit()
}

export function setGlyphPx(px: number): void {
  prefs = { ...prefs, px: clampPx(px) }
  try { localStorage.setItem(PX_KEY, String(prefs.px)) } catch { /* above */ }
  emit()
}

export function getGlyphPrefs(): GlyphPrefs {
  return prefs
}

export function useGlyphPrefs(): GlyphPrefs {
  return useSyncExternalStore(
    cb => { listeners.add(cb); return () => listeners.delete(cb) },
    () => prefs,
  )
}

export function detectGlyphSet(controllerId: string): ResolvedGlyphSet {
  return /dualsense|dualshock|playstation|054c|wireless controller/i.test(controllerId) ? 'ps' : 'xbox'
}

/* The pack, imported through Vite so the normal build emits hashed files and the mockup build
   inlines them (assetsInlineLimit) into the single HTML. */
const URLS = import.meta.glob('./pack/**/*.png', { eager: true, query: '?url', import: 'default' }) as Record<string, string>

const COLORABLE: readonly GlyphId[] = ['a', 'b', 'x', 'y']

function fileFor(id: GlyphId, tone: GlyphTone): string {
  return tone === 'color' && COLORABLE.includes(id) ? `${id}-color.png` : `${id}.png`
}

function keyFor(set: ResolvedGlyphSet, variant: GlyphVariant, id: GlyphId, tone: GlyphTone): string {
  return `./pack/${set}/${variant}/${fileFor(id, tone)}`
}

/** The pack ships no Xbox menu button; both sets get this neutral chip so Start reads the same. */
function StartGlyph({ size }: { size: number }) {
  return (
    <span className="sl-glyph-box" style={{ width: size, height: size }}>
      <svg
        className="sl-glyph" width={size} height={size} viewBox="2.6 5.2 18.8 13.6"
        aria-hidden="true" data-glyph-id="start" data-glyph-size={String(size)}
      >
        <rect x={2.6} y={5.2} width={18.8} height={13.6} rx={4.4} fill="var(--color-raise)" />
        <path d="M7.6 9.5h8.8M7.6 12h8.8M7.6 14.5h8.8" stroke="var(--color-fg)" strokeWidth={1.7} strokeLinecap="round" />
      </svg>
    </span>
  )
}

export function Glyph({
  id, set, variant = 'default', tone = 'white', size = 16,
}: { id: GlyphId; set: ResolvedGlyphSet; variant?: GlyphVariant; tone?: GlyphTone; size?: number }) {
  if (id === 'start') return <StartGlyph size={size} />
  const key = keyFor(set, variant, id, tone)
  const url = URLS[key]
  if (!url) return null
  const attrs = {
    className: 'sl-glyph', src: url, alt: '', draggable: false,
    'data-glyph-id': id, 'data-glyph-set': set, 'data-glyph-variant': variant,
    'data-glyph-tone': tone, 'data-glyph-size': String(size),
  }
  const b = GLYPH_BOUNDS[key]
  if (!b) {
    return (
      <span className="sl-glyph-box" style={{ width: size, height: size }}>
        <img {...attrs} style={{ width: size, height: size, objectFit: 'contain' }} />
      </span>
    )
  }
  // The sprite's OPAQUE CONTENT is fitted into the box: equal visual size for every glyph,
  // whatever transparent padding the pack put around it.
  const [cw, ch, bx, by, bw, bh] = b
  const scale = size / Math.max(bw, bh)
  const style = {
    width: cw * scale,
    height: ch * scale,
    left: (size - bw * scale) / 2 - bx * scale,
    top: (size - bh * scale) / 2 - by * scale,
  }
  return (
    <span className="sl-glyph-box" style={{ width: size, height: size }}>
      <img {...attrs} style={style} />
    </span>
  )
}
