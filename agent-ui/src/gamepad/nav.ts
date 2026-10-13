import { useSyncExternalStore } from 'react'
import type { PadAction } from './pad'

/**
 * Spatial navigation over the real DOM, in two shapes:
 *
 * - flat pages: every control is a stop (Buttons/pages/list rows);
 * - tile pages (one game's page, Settings): the big blocks — cards, action rows — are the first
 *   level, A opens a block, B closes it, and arrows stay inside the open one.
 *
 * It also owns the pieces the app cannot give: overlay scoping and restore, remembering where a
 * game page was opened from, the Add Games filter lock, and text entry via the on-screen keyboard.
 */

const FOCUSABLE = [
  'button:not([disabled])',
  '[role="button"]:not([aria-disabled="true"])',
  '[role="switch"]:not([disabled])',
  '[role="checkbox"]:not([aria-disabled="true"])',
  'input:not([type="hidden"]):not([disabled]):not([tabindex="-1"])',
  'textarea:not([disabled])',
  'select:not([disabled])',
  'a[href]',
].join(', ')

const CONTAINER_TAGS = new Set(['DIV', 'SECTION', 'FORM', 'DIALOG', 'MAIN', 'ASIDE'])
const TILE_GRIDS = ['sl-grid2', 'sl-grid3', 'sl-grid4']

export type TileLevel = 'flat' | 'tiles' | 'inside'

export interface NavSnapshot {
  section: string
  contextLabel: string
  focusSeq: number
  hasFocus: boolean
  folderActionAvailable: boolean
  viewToggleAvailable: boolean
  filtersActive: boolean
  tileLevel: TileLevel
}

const navListeners = new Set<() => void>()
let navSnap: NavSnapshot = {
  section: '', contextLabel: 'Select', focusSeq: 0, hasFocus: false,
  folderActionAvailable: false, viewToggleAvailable: false, filtersActive: false, tileLevel: 'flat',
}
let focused: HTMLElement | null = null
let ring: HTMLElement | null = null
/** Add Games only: the filter block is unlocked by X (or View) and locks arrows inside it. */
let filtersActive = false
/** Tile pages: the block A has opened; arrows stay inside it until B. */
let enteredTile: HTMLElement | null = null
/** Element to re-focus after a page or overlay transition (the control an overlay was opened over). */
let pendingRestore: HTMLElement | null = null
/** The game row/tile the current game page was opened from, by title — the element itself is
 *  replaced by React when the page changes, so a reference would disconnect. */
let pendingRestoreName: string | null = null
let lastGameEntryName: string | null = null
/** R3 cycles a candidate row's own actions (folder Change, then server-game Change). */
let cycleRow: HTMLElement | null = null
let cycleIndex = 0
let lastScope: Element | null = null
let lastPageEl: Element | null = null
let lastStructureCheck = 0

let emits = 0

function emitNav(patch: Partial<NavSnapshot>) {
  emits++
  navSnap = { ...navSnap, ...patch }
  navListeners.forEach(l => l())
}

export function useNav(): NavSnapshot {
  return useSyncExternalStore(
    cb => { navListeners.add(cb); return () => navListeners.delete(cb) },
    () => navSnap,
  )
}

/* ------------------------------------------------------------------ scope */

function isVisible(el: HTMLElement): boolean {
  const cs = getComputedStyle(el)
  if (cs.display === 'none' || cs.visibility === 'hidden') return false
  return el.getClientRects().length > 0
}

/** The topmost full-viewport fixed layer, if any — the app's own modals and the path browser. */
function overlayScope(): HTMLElement | null {
  let best: HTMLElement | null = null
  const vw = window.innerWidth
  const vh = window.innerHeight
  for (const el of document.body.querySelectorAll<HTMLElement>('*')) {
    if (!CONTAINER_TAGS.has(el.tagName) || el.childElementCount === 0) continue
    const cs = getComputedStyle(el)
    if (cs.position !== 'fixed' || cs.display === 'none' || cs.visibility === 'hidden') continue
    if (Number.parseInt(cs.zIndex || '0', 10) < 10) continue
    const declaredFull =
      el.style.inset === '0px' || el.style.inset === '0'
      || (el.style.top === '0px' && el.style.left === '0px')
    const r = el.getBoundingClientRect()
    if (!declaredFull && (r.width < vw * 0.72 || r.height < vh * 0.6)) continue
    best = el
  }
  return best
}

function scopeRoot(): HTMLElement | ParentNode {
  return overlayScope() ?? document.querySelector('.sl-app') ?? document.body
}

/* ------------------------------------------------------------------ focusables */

function leafFocusables(root: ParentNode): HTMLElement[] {
  const found: HTMLElement[] = []
  for (const el of root.querySelectorAll<HTMLElement>(FOCUSABLE)) {
    if (!isVisible(el)) continue
    if (el.closest('[hidden]')) continue
    // The side rail is driven by LB/RB only — never by the ring.
    if (el.closest('.sl-aside')) continue
    // A control that carries its own binding glyph is driven by that binding.
    if (el.querySelector('.sl-padglyph')) continue
    if (el.matches('[data-pad-skip]')) continue
    found.push(el)
  }
  return found
}

function currentSection(): string {
  const nav = document.querySelector('.sl-nav[aria-current="page"]')
  return nav?.textContent?.trim().replace(/\d+$/, '').trim() ?? ''
}

export function isGamePage(): boolean {
  return !!document.querySelector('.sl-content .sl-gamehead')
}

function tilePage(): boolean {
  return isGamePage() || currentSection() === 'Settings'
}

function pageRoot(): HTMLElement | null {
  return document.querySelector<HTMLElement>('.sl-content .sl-page')
}

/** One game's page and Settings, split into their big blocks (cards, action rows). */
function tilesOf(root: HTMLElement): HTMLElement[] {
  const out: HTMLElement[] = []
  for (const child of Array.from(root.children) as HTMLElement[]) {
    if (child.classList.contains('sl-back') || child.classList.contains('sl-toast')) continue
    if (child.hidden) continue
    if (child.classList.contains('sl-collapse-empty')) {
      out.push(...(Array.from(child.children) as HTMLElement[]))
      continue
    }
    if (TILE_GRIDS.some(c => child.classList.contains(c))) {
      out.push(...(Array.from(child.children) as HTMLElement[]))
      continue
    }
    out.push(child)
  }
  return out
}

function tileStops(): HTMLElement[] {
  const root = pageRoot()
  if (!root) return []
  const stops: HTMLElement[] = []
  for (const tile of tilesOf(root)) {
    if (!leafFocusables(tile).length && !tile.matches(FOCUSABLE)) continue
    tile.dataset.padTile = '1'
    if (!tile.matches('[tabindex]')) tile.setAttribute('tabindex', '-1')
    stops.push(tile)
  }
  return stops
}

function focusables(): HTMLElement[] {
  // A full-viewport overlay takes precedence over everything below it — including an open tile —
  // or a folder picker opened from inside a game-page card would focus the card behind the modal.
  const scope = overlayScope()
  if (scope) return leafFocusables(scope)
  if (enteredTile && enteredTile.isConnected) {
    return leafFocusables(enteredTile)
  }
  if (tilePage()) return tileStops()

  const els = leafFocusables(scopeRoot())
  const section = currentSection()
  if (section === 'Add Games') {
    // The filter block is a modal step: nothing outside it is reachable while it is open, and it
    // is unreachable at all until X (or View) unlocks it.
    return filtersActive ? els.filter(el => !!el.closest('.sl-filters')) : els.filter(el => !el.closest('.sl-filters'))
  }
  return els
}

/* ------------------------------------------------------------------ ring + focus */

function ringTarget(el: HTMLElement): HTMLElement {
  if (el instanceof HTMLInputElement && el.type === 'checkbox') {
    return (el.closest('label') as HTMLElement | null) ?? el
  }
  return el
}

function setRing(el: HTMLElement | null) {
  const next = el ? ringTarget(el) : null
  if (ring === next) return
  ring?.classList.remove('pad-focused')
  ring = next
  ring?.classList.add('pad-focused')
}

function contextLabelFor(el: HTMLElement): string {
  if (el.dataset.padTile === '1') return 'Open'
  if (el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement) return 'Type'
  if (el.getAttribute('role') === 'switch' || el.getAttribute('role') === 'checkbox') return 'Toggle'
  if (el.dataset.padLabel) return el.dataset.padLabel
  return 'Select'
}

function folderActionFor(el: HTMLElement | null): boolean {
  const row = el?.closest?.('.sl-check-row') as HTMLElement | null
  if (!row) return false
  return [...row.querySelectorAll('button')].some(b => /change|set save folder/i.test(b.textContent ?? ''))
}

function tileLevel(): TileLevel {
  if (!tilePage()) return 'flat'
  return enteredTile ? 'inside' : 'tiles'
}

function emitFocus(el: HTMLElement) {
  emitNav({
    focusSeq: navSnap.focusSeq + 1, hasFocus: true,
    contextLabel: contextLabelFor(el),
    folderActionAvailable: folderActionFor(el),
    viewToggleAvailable: !!document.querySelector('.sl-content .sl-seg'),
    filtersActive,
    tileLevel: tileLevel(),
  })
}

function focusElement(el: HTMLElement, opts?: { silent?: boolean }) {
  enteredTileCheck(el)
  focused = el
  rememberEntry(el)
  try { el.focus({ preventScroll: true }) } catch { /* detached node: the next action re-resolves */ }
  if (typeof el.scrollIntoView === 'function') el.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  setRing(el)
  if (!opts?.silent) emitFocus(el)
}

function enteredTileCheck(el: HTMLElement) {
  // Focus moving into a modal is not "leaving the tile" — the tile context must survive it so
  // closing the modal returns the user where they were.
  if (overlayScope()) return
  if (enteredTile && !enteredTile.contains(el) && el !== enteredTile) enteredTile = null
}

function firstFocusable(): HTMLElement | null {
  const els = focusables()
  return els[0] ?? null
}

/** Public for the structure observer: focus the first control of the current scope. */
export function focusFirstInScope(): void {
  const first = firstFocusable()
  if (first) focusElement(first, { silent: true })
  emitNav({ section: currentSection(), filtersActive, tileLevel: tileLevel() })
}

/* ------------------------------------------------------------------ movement */

function paneOf(el: HTMLElement): string {
  if (el.closest('.sl-aside')) return 'aside'
  if (el.closest('.sl-hero')) return 'hero'
  if (el.closest('.sl-content')) return 'content'
  return 'other'
}

function move(dir: PadAction) {
  const els = focusables()
  if (els.length === 0) return
  if (!focused || !focused.isConnected || !isVisible(focused)) {
    focusElement(els[0], { silent: true })
    return
  }
  const r = focused.getBoundingClientRect()
  const cx = r.left + r.width / 2
  const cy = r.top + r.height / 2
  const vx = dir === 'right' ? 1 : dir === 'left' ? -1 : 0
  const vy = dir === 'down' ? 1 : dir === 'up' ? -1 : 0

  const nearest = (pool: HTMLElement[]): HTMLElement | null => {
    let best: HTMLElement | null = null
    let bestScore = Number.POSITIVE_INFINITY
    for (const el of pool) {
      if (el === focused) continue
      const rr = el.getBoundingClientRect()
      const dx = rr.left + rr.width / 2 - cx
      const dy = rr.top + rr.height / 2 - cy
      const primary = vx ? dx * vx : dy * vy
      if (primary < 8) continue
      const lateral = vx ? Math.abs(dy) : Math.abs(dx)
      const score = primary + lateral * 1.7
      if (score < bestScore) { bestScore = score; best = el }
    }
    return best
  }

  const pane = paneOf(focused)
  const inPane = els.filter(e => paneOf(e) === pane)
  const vertical = vx === 0
  let best = nearest(inPane.length > 0 ? inPane : els)
  if (!best && !vertical) best = nearest(els)
  if (best) focusElement(best)
}

/* ------------------------------------------------------------------ scrolling */

function scrollable(el: Element | null): HTMLElement | null {
  let n: Element | null = el
  while (n instanceof HTMLElement) {
    const cs = getComputedStyle(n)
    if ((cs.overflowY === 'auto' || cs.overflowY === 'scroll') && n.scrollHeight > n.clientHeight + 4) return n
    n = n.parentElement
  }
  return null
}

function findScroller(): HTMLElement | null {
  const fromFocus = scrollable(focused)
  if (fromFocus) return fromFocus
  const scope = overlayScope()
  if (scope) {
    const inScope = scrollable(scope.querySelector('.sl-page, [role="dialog"]') ?? scope)
    if (inScope) return inScope
  }
  const page = pageRoot()
  return scrollable(page) ?? page ?? null
}

export function scrollActive(rx: number, ry: number, dtMs: number) {
  const el = findScroller()
  if (!el) return
  el.scrollTop += ry * 1200 * (dtMs / 1000)
  el.scrollLeft += rx * 800 * (dtMs / 1000)
}

export function scrollStep(dir: 1 | -1) {
  const el = findScroller()
  if (el) el.scrollTop += dir * 220
}

/* ------------------------------------------------------------------ activation */

function rememberEntry(el: HTMLElement) {
  if (!el.matches('.sl-row, .sl-tile')) return
  const title = el.querySelector('.sl-row__title, .sl-tile__name')?.textContent
    ?? el.textContent ?? ''
  lastGameEntryName = title.trim().slice(0, 60)
}

function pressA() {
  if (!focused || !focused.isConnected) { focusFirstInScope(); return }
  if (focused instanceof HTMLInputElement || focused instanceof HTMLTextAreaElement) {
    if (focused instanceof HTMLInputElement && focused.type === 'checkbox') { focused.click(); return }
    openOsk(focused)
    return
  }
  if (focused.dataset.padTile === '1') {
    const inner = leafFocusables(focused)
    if (inner.length > 0) {
      enteredTile = focused
      focusElement(inner[0])
      return
    }
  }
  rememberEntry(focused)
  focused.click()
}

function clickButton(re: RegExp): boolean {
  const scope = overlayScope() ?? document
  const btn = [...scope.querySelectorAll<HTMLButtonElement>('button:not([disabled])')]
    .find(b => re.test((b.textContent ?? '').trim()))
  if (!btn) return false
  btn.click()
  return true
}

function buttonByText(re: RegExp): HTMLButtonElement | null {
  const scope = overlayScope() ?? document
  return [...scope.querySelectorAll<HTMLButtonElement>('button')]
    .find(b => re.test((b.textContent ?? '').trim())) ?? null
}

/** The conflict card's "keep the other one as a backup" checkbox, wherever it is showing. */
function findBackupInput(): HTMLInputElement | null {
  const scope = overlayScope() ?? document
  for (const label of scope.querySelectorAll('label')) {
    if (!/backup/i.test(label.textContent ?? '')) continue
    const input = label.querySelector<HTMLInputElement>('input[type="checkbox"]')
    if (input) return input
  }
  return null
}

export function hasKeepBothOption(): boolean {
  return !!findBackupInput()
}

export function hasResolveButton(): boolean {
  return !!buttonByText(/^Resolve\b/i)
}

function backButton(): HTMLElement | null {
  return document.querySelector<HTMLElement>('.sl-back, [data-pad-back]')
}

function dispatchEscape() {
  const target = (focused && focused.isConnected ? focused : document.body)
  const ev = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
  ;(ev as { __slSynthetic?: boolean }).__slSynthetic = true
  target.dispatchEvent(ev)
}

function pressB() {
  if (oskOpen()) { oskBackspace(); return }
  if (document.querySelector('.sl-release__pop')) { dispatchEscape(); return }
  const scope = overlayScope()
  if (scope) {
    const buttons = [...scope.querySelectorAll<HTMLButtonElement>('button:not([disabled])')]
    const skip = buttons.find(b => /decide later|skip for now|cancel/i.test(b.textContent ?? ''))
    if (skip) { skip.click(); return }
    dispatchEscape()
    return
  }
  if (filtersActive) {
    filtersActive = false
    const row = document.querySelector<HTMLElement>('.sl-content .sl-check-row input:not([type="hidden"])')
      ?? document.querySelector<HTMLElement>('.sl-content .sl-check-row')
    if (row) focusElement(row)
    emitNav({ filtersActive: false, tileLevel: tileLevel() })
    return
  }
  if (enteredTile) {
    const tile = enteredTile
    enteredTile = null
    focusElement(tile)
    return
  }
  const back = backButton()
  if (back) {
    if (lastGameEntryName) pendingRestoreName = lastGameEntryName
    back.click()
  }
}

/* ------------------------------------------------------------------ contextual bindings */

function pressX() {
  const section = currentSection()
  if (section === 'Games') {
    if (isGamePage()) {
      clickButton(/^Sync this game$/i)
      return
    }
    const search = document.querySelector<HTMLInputElement>('input.sl-search')
    if (search) focusElement(search)
  } else if (section === 'Add Games') {
    if (filtersActive) {
      filtersActive = false
      const row = document.querySelector<HTMLElement>('.sl-content .sl-check-row input:not([type="hidden"])')
        ?? document.querySelector<HTMLElement>('.sl-content .sl-check-row')
      if (row) focusElement(row)
      emitNav({ filtersActive: false, tileLevel: tileLevel() })
      return
    }
    filtersActive = true
    const chip = document.querySelector<HTMLButtonElement>('.sl-content .sl-page .sl-fchip')
    if (chip) focusElement(chip)
    emitNav({ filtersActive: true, tileLevel: tileLevel() })
  }
}

/** View/Share: search, on every page that has a search field. */
function pressView() {
  const search = document.querySelector<HTMLInputElement>('.sl-content .sl-page input.sl-search')
  if (search) focusElement(search)
}

/** L3: Rescan in Add Games. */
function pressL3() {
  if (currentSection() === 'Add Games') clickButton(/^Rescan$/i)
}

/** Start: the Hide enrolled switch in Add Games. */
function pressStart() {
  if (currentSection() !== 'Add Games') return
  const toggle = document.querySelector<HTMLButtonElement>('[aria-label="Hide enrolled games"]')
  toggle?.click()
}

function pressY() {
  const syncAll = document.querySelector<HTMLButtonElement>('.sl-hero button.sl-btn--primary')
  syncAll?.click()
}

/** LT: keep-both on a conflict card; Push now on a game page; Clear the selection in Add Games. */
function pressLT() {
  const backup = findBackupInput()
  if (backup) { backup.click(); return }
  if (isGamePage()) { clickButton(/^Push now$/i); return }
  if (currentSection() === 'Add Games') clickButton(/^Clear$/i)
}

/** RT: Resolve (confirm-mode conflict card); Pull latest on a game page; Add games in Add Games. */
function pressRT() {
  if (clickButton(/^Resolve\b/i)) return
  if (isGamePage()) { clickButton(/^Pull latest$/i); return }
  if (currentSection() === 'Add Games') {
    const add = document.querySelector<HTMLButtonElement>('.sl-footbar .sl-btn--primary:not([disabled])')
    add?.click()
  }
}

/** R3: List/Grid on Games; cycle the focused candidate row's own actions (folder Change, then the
 *  server-game Change link) in Add Games. */
function pressR3() {
  const section = currentSection()
  if (section === 'Games') {
    const seg = document.querySelector('.sl-content .sl-seg')
    if (!seg) return
    const next = [...seg.querySelectorAll<HTMLButtonElement>('button')]
      .find(b => b.getAttribute('aria-pressed') === 'false')
    next?.click()
    return
  }
  if (section === 'Add Games') {
    const row = focused?.closest<HTMLElement>('.sl-check-row')
    if (!row) return
    const actions = [...row.querySelectorAll<HTMLButtonElement>('button')]
      .filter(b => /change|set save folder/i.test(b.textContent ?? ''))
    if (actions.length === 0) return
    if (cycleRow !== row) { cycleRow = row; cycleIndex = 0 }
    else cycleIndex = (cycleIndex + 1) % actions.length
    actions[cycleIndex]?.click()
  }
}

function sectionStep(delta: number) {
  const navs = [...document.querySelectorAll<HTMLButtonElement>('.sl-nav')]
  if (navs.length === 0) return
  const index = navs.findIndex(n => n.getAttribute('aria-current') === 'page')
  const next = navs[(index + delta + navs.length) % navs.length]
  next?.click()
}

export function handleAction(action: PadAction) {
  if (action === 'up' || action === 'down' || action === 'left' || action === 'right') move(action)
  else if (action === 'a') pressA()
  else if (action === 'b') pressB()
  else if (action === 'x') pressX()
  else if (action === 'y') pressY()
  else if (action === 'lt') pressLT()
  else if (action === 'rt') pressRT()
  else if (action === 'l3') pressL3()
  else if (action === 'view') pressView()
  else if (action === 'start') pressStart()
  else if (action === 'r3') pressR3()
  else if (action === 'lb') sectionStep(-1)
  else if (action === 'rb') sectionStep(1)
  else if (action === 'scrollUp') scrollStep(-1)
  else if (action === 'scrollDown') scrollStep(1)
}

/* ------------------------------------------------------------------ on-screen keyboard */

interface OskSnapshot { open: boolean; title: string; text: string }
const oskListeners = new Set<() => void>()
let oskState: OskSnapshot = { open: false, title: '', text: '' }
let oskInput: HTMLInputElement | null = null

function emitOsk() { oskListeners.forEach(l => l()) }

export function useOsk(): OskSnapshot {
  return useSyncExternalStore(
    cb => { oskListeners.add(cb); return () => oskListeners.delete(cb) },
    () => oskState,
  )
}

export function oskOpen(): boolean { return oskState.open }

export function openOsk(input: HTMLElement) {
  if (!(input instanceof HTMLInputElement) || input.type === 'checkbox') return
  oskInput = input
  oskState = { open: true, title: input.getAttribute('aria-label') ?? input.placeholder ?? 'Type', text: input.value }
  emitOsk()
  requestAnimationFrame(() => focusFirstInScope())
}

export function oskInsert(text: string) {
  oskState = { ...oskState, text: oskState.text + text }
  emitOsk()
}

export function oskBackspace() {
  oskState = { ...oskState, text: oskState.text.slice(0, -1) }
  emitOsk()
}

export function oskCommit() {
  const input = oskInput
  const text = oskState.text
  if (input) {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
    setter?.call(input, text)
    input.dispatchEvent(new Event('input', { bubbles: true }))
  }
  oskClose()
}

export function oskClose() {
  oskInput = null
  oskState = { open: false, title: '', text: '' }
  emitOsk()
  requestAnimationFrame(() => focusFirstInScope())
}

/** Read-only state for the mockup's test harness. */
export function navDebugState() {
  return {
    section: currentSection(),
    emits,
    lastGameEntryName,
    pendingRestoreName,
    enteredTile: enteredTile ? (enteredTile.className || enteredTile.tagName) : null,
    filtersActive,
    keepBoth: hasKeepBothOption(),
    resolve: hasResolveButton(),
    focused: focused ? ((focused.className || focused.tagName) + ' | ' + (focused.textContent ?? '').trim().slice(0, 40)) : null,
  }
}

/* ------------------------------------------------------------------ structure + focus sync */

function restoreOrFirst() {
  if (pendingRestoreName) {
    const name = pendingRestoreName
    pendingRestoreName = null
    const row = [...document.querySelectorAll<HTMLElement>('.sl-row, .sl-tile')]
      .find(r => ((r.querySelector('.sl-row__title, .sl-tile__name')?.textContent ?? r.textContent ?? '').trim()).startsWith(name))
    if (row) {
      focusElement(row, { silent: true })
      return
    }
  }
  if (pendingRestore && pendingRestore.isConnected && isVisible(pendingRestore)) {
    const el = pendingRestore
    pendingRestore = null
    focusElement(el, { silent: true })
    return
  }
  pendingRestore = null
  focusFirstInScope()
}

export function attachFocusSync(): () => void {
  const onFocusIn = (ev: FocusEvent) => {
    const el = ev.target
    if (!(el instanceof HTMLElement)) return
    if (!el.closest('.sl-app')) return
    if (el.closest('.sl-osk')) return
    if (el.closest('.sl-aside')) return
    if (el !== focused && (el.matches(FOCUSABLE) || el.dataset.padTile === '1')) {
      enteredTileCheck(el)
      focused = el
      cycleRow = null
      // Remember where a game page was opened from — this covers mouse and keyboard (a no-arg
      // click never passes through pressA).
      rememberEntry(el)
      setRing(el)
      emitFocus(el)
    }
  }
  document.addEventListener('focusin', onFocusIn)

  let trailingTimer: number | undefined

  const structureCheck = () => {
    lastStructureCheck = performance.now()
    const scope = overlayScope()
    if (scope !== lastScope) {
      if (scope) {
        if (focused && !scope.contains(focused)) pendingRestore = focused
        lastScope = scope
        focusFirstInScope()
      } else {
        lastScope = null
        restoreOrFirst()
      }
    } else {
      const pageEl = pageRoot()
      if (pageEl !== lastPageEl) {
        lastPageEl = pageEl
        enteredTile = null
        filtersActive = false
        if (!scope) restoreOrFirst()
      }
    }
    emitNav({ section: currentSection(), filtersActive, tileLevel: tileLevel() })
  }

  const observer = new MutationObserver(() => {
    if (focused && !focused.isConnected) {
      focused = null
      setRing(null)
      emitNav({ hasFocus: false })
    }
    const now = performance.now()
    if (now - lastStructureCheck >= 120) {
      structureCheck()
      return
    }
    // A burst can render everything and then go idle — the trailing check is what makes the last
    // state (a modal fully drawn, a hint that just became available) actually reach the UI.
    if (trailingTimer === undefined) {
      trailingTimer = window.setTimeout(() => { trailingTimer = undefined; structureCheck() }, 130)
    }
  })
  observer.observe(document.body, { childList: true, subtree: true })

  return () => {
    document.removeEventListener('focusin', onFocusIn)
    observer.disconnect()
  }
}
