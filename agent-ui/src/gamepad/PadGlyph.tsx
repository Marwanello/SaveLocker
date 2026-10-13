import { usePad, usePadVisible } from './pad'
import { Glyph, useGlyphPrefs, type GlyphId } from './glyphs'

/**
 * A binding glyph shown ON (or beside) the control it acts on, and only while a controller is
 * actually driving the UI — `null` on a mouse-and-keyboard window, so nothing moves and the
 * desktop stays exactly as it was. Size follows the user's glyph-size preference (default 37 px).
 * A control that carries one of these is skipped by arrow navigation: its binding is the way in.
 */
export function PadGlyph({ id, size }: { id: GlyphId; size?: number }) {
  const visible = usePadVisible()
  const pad = usePad()
  const prefs = useGlyphPrefs()
  if (!visible) return null
  return (
    <span className="sl-padglyph" aria-hidden="true">
      <Glyph
        id={id}
        set={prefs.set === 'auto' ? pad.detected : prefs.set}
        variant={prefs.variant}
        tone={prefs.tone}
        size={size ?? prefs.px}
      />
    </span>
  )
}
