import { useEffect, useState } from 'react'
import { usePad, type PadAction } from './pad'
import { hasKeepBothOption, hasResolveButton, isGamePage, useNav } from './nav'
import { GLYPH_BAR_SIZE, Glyph, useGlyphPrefs } from './glyphs'

/**
 * The bar the SDL UI had at the bottom, rebuilt out of the same rules: every binding that exists is
 * shown, per screen context where it changes (A's label follows the focused control, X appears only
 * where it does something), and a press flashes its own hint so the connection between a button and
 * its effect is visible while learning.
 */

interface Hint {
  key: string
  glyphs: import('./glyphs').GlyphId[]
  label: string
}

const FLASH_FOR: Partial<Record<PadAction, string>> = {
  up: 'move', down: 'move', left: 'move', right: 'move',
  a: 'a', b: 'b', x: 'x', y: 'y', lb: 'lb', rb: 'rb', lt: 'lt', rt: 'rt', r3: 'r3',
}

export function HintBar() {
  const pad = usePad()
  const nav = useNav()
  const prefs = useGlyphPrefs()
  const set = prefs.set === 'auto' ? pad.detected : prefs.set
  const [flash, setFlash] = useState<string | null>(null)

  useEffect(() => {
    if (!pad.lastAction) return
    const key = FLASH_FOR[pad.lastAction]
    if (!key) return
    setFlash(key)
    const t = setTimeout(() => setFlash(null), 240)
    return () => clearTimeout(t)
  }, [pad.seq, pad.lastAction])

  const hints: Hint[] = [
    { key: 'a', glyphs: ['a'], label: nav.contextLabel || 'Select' },
    { key: 'b', glyphs: ['b'], label: 'Back' },
    { key: 'move', glyphs: ['move', 'stick'], label: 'Move' },
    { key: 'rstick', glyphs: ['rstick'], label: 'Scroll' },
  ]
  if (nav.section === 'Games') {
    if (isGamePage()) {
      // A conflict card on the page owns LT/RT (keep-both / Resolve); only advertise Push/Pull when
      // it does not, or the bar would show two different meanings for the same button.
      hints.push({ key: 'x', glyphs: ['x'], label: 'Sync' })
      if (!hasKeepBothOption()) hints.push({ key: 'lt', glyphs: ['lt'], label: 'Push' })
      if (!hasResolveButton()) hints.push({ key: 'rt', glyphs: ['rt'], label: 'Pull' })
    } else {
      hints.push({ key: 'x', glyphs: ['x'], label: 'Search' })
      if (nav.viewToggleAvailable) hints.push({ key: 'r3', glyphs: ['r3'], label: 'List / Grid' })
    }
  }
  if (nav.section === 'Add Games') {
    hints.push({ key: 'x', glyphs: ['x'], label: nav.filtersActive ? 'List' : 'Filters' })
    hints.push({ key: 'lt', glyphs: ['lt', 'rt'], label: 'Clear / Add' })
    if (nav.folderActionAvailable) hints.push({ key: 'r3', glyphs: ['r3'], label: 'Change' })
  }
  if (hasKeepBothOption()) hints.push({ key: 'lt', glyphs: ['lt'], label: 'Keep backup' })
  if (hasResolveButton()) hints.push({ key: 'rt', glyphs: ['rt'], label: 'Resolve' })
  hints.push(
    { key: 'y', glyphs: ['y'], label: 'Sync all' },
    { key: 'lb', glyphs: ['lb', 'rb'], label: 'Sections' },
  )

  return (
    <div className="sl-hintbar" role="status" aria-label="Controller hints">
      {hints.map(h => (
        <span key={h.key} className={`sl-hint${flash === h.key ? ' sl-hint--hot' : ''}`}>
          {h.glyphs.map(g => (
            <Glyph key={g} id={g} set={set} variant={prefs.variant} tone={prefs.tone} size={GLYPH_BAR_SIZE} />
          ))}
          <b>{h.label}</b>
        </span>
      ))}
      <span className="sl-hintbar__spacer" />
      <span className="sl-hintbar__pad">
        <Glyph id="a" set={set} variant={prefs.variant} tone={prefs.tone} size={GLYPH_BAR_SIZE} />
        <Glyph id="b" set={set} variant={prefs.variant} tone={prefs.tone} size={GLYPH_BAR_SIZE} />
        <Glyph id="x" set={set} variant={prefs.variant} tone={prefs.tone} size={GLYPH_BAR_SIZE} />
        <Glyph id="y" set={set} variant={prefs.variant} tone={prefs.tone} size={GLYPH_BAR_SIZE} />
        <span className="sl-hintbar__name">
          {pad.connected ? pad.controller.replace(/\s*\(.*\)$/, '') : 'Keyboard'}
        </span>
      </span>
    </div>
  )
}
