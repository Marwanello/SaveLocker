import { useSyncExternalStore } from 'react'
import { detectGlyphSet, type ResolvedGlyphSet } from './glyphs'

/**
 * One input funnel for the controller layer: the Gamepad API (polled, because that is the only way
 * a browser exposes button state) and a keyboard fallback on the same action vocabulary, so every
 * pad binding is testable from a keyboard and drives the exact same code path.
 *
 * The keyboard listener is capture-phase and stops propagation for keys it consumes — otherwise a
 * component with its own arrow handling (PathBrowserModal) would act on the same key a second time.
 */
export type PadAction = 'up' | 'down' | 'left' | 'right' | 'a' | 'b' | 'x' | 'y' | 'lb' | 'rb' | 'lt' | 'rt' | 'l3' | 'view' | 'start' | 'startHold' | 'r3' | 'scrollUp' | 'scrollDown'
export type PadUiMode = 'auto' | 'on' | 'off'

export interface PadSnapshot {
  connected: boolean
  controller: string
  detected: ResolvedGlyphSet
  uiMode: PadUiMode
  lastAction: PadAction | null
  seq: number
}

const REPEAT_DELAY_MS = 400
const REPEAT_EVERY_MS = 90
const STICK_DEADZONE = 0.55
const STICK_REPEAT_DELAY_MS = 380
const SCROLL_DEADZONE = 0.18
/** Start is two bindings in Add Games: a tap and a hold. A tap fires on release, a hold once this long in. */
const HOLD_MS = 500
/** The hint bar's Auto/On/Off is a device preference like the glyph ones, so it survives a restart. */
const UI_MODE_KEY = 'savelocker.pad.uiMode'

const listeners = new Set<() => void>()
let snapshot: PadSnapshot = { connected: false, controller: '', detected: 'xbox', uiMode: readUiMode(), lastAction: null, seq: 0 }
let actionHandler: ((a: PadAction) => void) | null = null
let scrollHandler: ((rx: number, ry: number, dtMs: number) => void) | null = null
let started = false

function readUiMode(): PadUiMode {
  try {
    const raw = localStorage.getItem(UI_MODE_KEY)
    if (raw === 'on' || raw === 'off' || raw === 'auto') return raw
  } catch { /* private window: the default */ }
  return 'auto'
}

function emit(patch: Partial<PadSnapshot>) {
  snapshot = { ...snapshot, ...patch }
  listeners.forEach(l => l())
}

export function setActionHandler(fn: ((a: PadAction) => void) | null): void {
  actionHandler = fn
}

/** Right-stick scrolling is continuous, so it gets its own sink: (x, y, dtMs) every polled frame. */
export function setScrollHandler(fn: ((rx: number, ry: number, dtMs: number) => void) | null): void {
  scrollHandler = fn
}

export function setPadUiMode(mode: PadUiMode): void {
  try { localStorage.setItem(UI_MODE_KEY, mode) } catch { /* private window: lasts this visit */ }
  emit({ uiMode: mode })
}

export function usePad(): PadSnapshot {
  return useSyncExternalStore(
    cb => { listeners.add(cb); return () => listeners.delete(cb) },
    () => snapshot,
  )
}

export function usePadVisible(): boolean {
  const pad = usePad()
  if (pad.uiMode === 'on') return true
  if (pad.uiMode === 'off') return false
  return pad.connected
}

function fire(action: PadAction) {
  emit({ lastAction: action, seq: snapshot.seq + 1 })
  actionHandler?.(action)
}

/* ------------------------------------------------------------------ keyboard */

const KEYMAP: Record<string, PadAction> = {
  ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right',
  Enter: 'a', ' ': 'a',
  Backspace: 'b', Escape: 'b',
  x: 'x', X: 'x', y: 'y', Y: 'y',
  q: 'lb', Q: 'lb', e: 'rb', E: 'rb',
  t: 'start', T: 'start',
  v: 'r3', V: 'r3',
  z: 'lt', Z: 'lt',
  r: 'rt', R: 'rt',
  f: 'view', F: 'view',
  g: 'l3', G: 'l3',
  PageDown: 'scrollDown', PageUp: 'scrollUp',
}

function onKeyDown(ev: KeyboardEvent) {
  if ((ev as { __slSynthetic?: boolean }).__slSynthetic) return
  // Browser shortcuts (Ctrl+R, Ctrl+L, …) are never ours to consume.
  if (ev.ctrlKey || ev.metaKey || ev.altKey) return
  const t = ev.target as HTMLElement | null
  const tag = t?.tagName
  // Only a field INSIDE the app counts, and only a TEXT field: a checkbox must not swallow the
  // binding keys, or X over a candidate row would do nothing.
  const editable = t instanceof HTMLInputElement
    ? !['checkbox', 'radio', 'range', 'color', 'file', 'submit', 'button', 'reset', 'image'].includes(t.type)
    : tag === 'TEXTAREA' || t?.isContentEditable === true
  // A dialog is portalled to <body>, outside .sl-app, but its text fields are the app's too (Backspace must edit, not go back).
  const inAppField = !!t && !!t.closest?.('.sl-app, [role="dialog"]') && editable

  if (ev.key === 'Escape') { consume(ev); fire('b'); return }
  if (inAppField) {
    // Up/Down LEAVE a single-line field for navigation (Left/Right keep the caret), or the ring
    // could never get past a search box — the one focus trap a browser gives you for free.
    if ((ev.key === 'ArrowUp' || ev.key === 'ArrowDown') && t instanceof HTMLInputElement) {
      consume(ev)
      fire(ev.key === 'ArrowUp' ? 'up' : 'down')
    }
    return
  }
  // A native button already activates on Enter/Space; intercepting would double-fire it.
  if ((ev.key === 'Enter' || ev.key === ' ') && (tag === 'BUTTON' || tag === 'A') && t?.closest?.('.sl-app')) return
  const action = KEYMAP[ev.key]
  if (!action) return
  consume(ev)
  // Shift+T is the keyboard's Start hold (a held key only repeats, so it can't be timed the way a button is).
  fire(action === 'start' && ev.shiftKey ? 'startHold' : action)
}

function consume(ev: KeyboardEvent) {
  ev.preventDefault()
  ev.stopPropagation()
}

/* ------------------------------------------------------------------ gamepad */

const BUTTONS: Array<[number, PadAction]> = [
  [0, 'a'], [1, 'b'], [2, 'x'], [3, 'y'],
  [4, 'lb'], [5, 'rb'], [6, 'lt'], [7, 'rt'], [8, 'view'], [9, 'start'],
  [10, 'l3'], [11, 'r3'],
  [12, 'up'], [13, 'down'], [14, 'left'], [15, 'right'],
]
const DIRECTIONS: PadAction[] = ['up', 'down', 'left', 'right']

const prev: Partial<Record<PadAction, boolean>> = {}
const heldSince: Partial<Record<PadAction, number>> = {}
const nextRepeat: Partial<Record<PadAction, number>> = {}
let startHoldFired = false
let stickDir: PadAction | null = null
let stickSince = 0
let stickNext = 0
let lastFrame = 0
let raf = 0

function poll() {
  raf = requestAnimationFrame(poll)
  if (document.hidden) return

  let pad: Gamepad | null = null
  try {
    for (const p of navigator.getGamepads?.() ?? []) {
      if (p && p.connected) { pad = p; break }
    }
  } catch { /* getGamepads can throw in odd embedding contexts */ }

  if (!pad) {
    if (snapshot.connected) emit({ connected: false, controller: '', detected: 'xbox' })
    // A pad unplugged mid-hold must not leave a button "pressed" for the next one: the initial
    // press would be swallowed (pressed && was) or an immediate repeat would fire on reconnect.
    for (const key of [...Object.keys(prev), ...Object.keys(heldSince), ...Object.keys(nextRepeat)] as PadAction[]) {
      delete prev[key]
      delete heldSince[key]
      delete nextRepeat[key]
    }
    stickDir = null
    startHoldFired = false
    lastFrame = 0
    return
  }
  if (!snapshot.connected || snapshot.controller !== pad.id) {
    emit({ connected: true, controller: pad.id, detected: detectGlyphSet(pad.id) })
  }

  // The browser reports a pad to every window, focused or not: input meant for a game or another app
  // must not drive this one. What is held meanwhile is recorded as already pressed, so focusing the
  // window with a button down doesn't fire it.
  if (!document.hasFocus()) {
    for (const [index, action] of BUTTONS) prev[action] = pad.buttons[index]?.pressed ?? false
    startHoldFired = true
    stickDir = null
    lastFrame = 0
    return
  }

  const now = performance.now()
  const dt = lastFrame === 0 ? 16 : Math.min(64, now - lastFrame)
  lastFrame = now
  for (const [index, action] of BUTTONS) {
    const pressed = pad.buttons[index]?.pressed ?? false
    const was = prev[action] ?? false
    if (action === 'start') {
      if (pressed && !was) { heldSince.start = now; startHoldFired = false }
      else if (pressed && !startHoldFired && now - (heldSince.start ?? now) >= HOLD_MS) { startHoldFired = true; fire('startHold') }
      else if (!pressed && was && !startHoldFired) fire('start')
      prev.start = pressed
      continue
    }
    if (pressed && !was) {
      heldSince[action] = now
      fire(action)
    } else if (pressed && was && DIRECTIONS.includes(action)
      && now - (heldSince[action] ?? now) > REPEAT_DELAY_MS
      && now > (nextRepeat[action] ?? 0)) {
      nextRepeat[action] = now + REPEAT_EVERY_MS
      fire(action)
    } else if (!pressed) {
      heldSince[action] = 0
    }
    prev[action] = pressed
  }

  const lx = pad.axes[0] ?? 0
  const ly = pad.axes[1] ?? 0
  const dir: PadAction | null =
    Math.abs(lx) > Math.abs(ly)
      ? (lx > STICK_DEADZONE ? 'right' : lx < -STICK_DEADZONE ? 'left' : null)
      : (ly > STICK_DEADZONE ? 'down' : ly < -STICK_DEADZONE ? 'up' : null)
  if (dir === null) {
    stickDir = null
  } else if (dir !== stickDir) {
    stickDir = dir
    stickSince = now
    stickNext = 0
    fire(dir)
  } else if (now - stickSince > STICK_REPEAT_DELAY_MS && now > stickNext) {
    stickNext = now + REPEAT_EVERY_MS
    fire(dir)
  }

  // Right stick: continuous scrolling. Shaped so a light push creeps and a full push flies.
  if (pad.axes.length >= 4) {
    const shape = (v: number) => {
      const a = Math.abs(v)
      if (a <= SCROLL_DEADZONE) return 0
      return Math.sign(v) * Math.pow((a - SCROLL_DEADZONE) / (1 - SCROLL_DEADZONE), 1.4)
    }
    const sx = shape(pad.axes[2] ?? 0)
    const sy = shape(pad.axes[3] ?? 0)
    if (sx !== 0 || sy !== 0) scrollHandler?.(sx, sy, dt)
  }
}

export function startInput(): void {
  if (started) return
  started = true
  document.addEventListener('keydown', onKeyDown, true)
  raf = requestAnimationFrame(poll)
}

export function stopInput(): void {
  if (!started) return
  started = false
  document.removeEventListener('keydown', onKeyDown, true)
  cancelAnimationFrame(raf)
}

/** Test seam: drive the exact action path a pad button would. */
export function fireForTest(action: PadAction): void {
  fire(action)
}
