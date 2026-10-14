import {
  GLYPH_BAR_SIZE, GLYPH_SIZE_MAX, GLYPH_SIZE_MIN, GLYPH_VARIANTS, Glyph,
  setGlyphOverride, setGlyphPx, setGlyphTone, setGlyphVariant, useGlyphPrefs,
  type GlyphId, type GlyphSet, type GlyphTone, type GlyphVariant,
} from '../gamepad/glyphs'
import { setPadUiMode, usePad, type PadUiMode } from '../gamepad/pad'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Seg } from './ui/Seg'

const PREVIEW: GlyphId[] = ['a', 'b', 'x', 'y', 'lb', 'rb', 'lt', 'rt', 'l3', 'r3', 'view', 'start']

const SET_OPTIONS: { value: GlyphSet; label: string }[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'xbox', label: 'Xbox' },
  { value: 'ps', label: 'PlayStation' },
]
const UI_MODE_OPTIONS: { value: PadUiMode; label: string }[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'on', label: 'On' },
  { value: 'off', label: 'Off' },
]
const TONE_OPTIONS: { value: GlyphTone; label: string }[] = [
  { value: 'white', label: 'White' },
  { value: 'color', label: 'Color' },
]

/**
 * Configuration → Controller. The preferences are per device (a controller is a thing on this
 * desk, not a fleet setting), so they live in localStorage through the glyph store. Glyphs are
 * drawn from Julio Cacko's Free Input Pack — see agent-ui/src/gamepad/pack/README.md.
 */
export function ControllerCard() {
  const pad = usePad()
  const prefs = useGlyphPrefs()
  const set = prefs.set === 'auto' ? pad.detected : prefs.set

  return (
    <Card
      title="Controller"
      headerRight={pad.connected
        ? <Chip tone="ok">{pad.controller.replace(/\s*\(.*\)$/, '')}</Chip>
        : <Chip>No controller connected</Chip>}
    >
      <div className="sl-stack" style={{ gap: 14 }}>
        <div className="sl-setting">
          <div className="sl-setting__label">
            Button hints
            <small>The bottom hint bar shows the bindings of the screen in front of you. Auto shows it only while a controller is connected.</small>
          </div>
          <Seg<PadUiMode> aria-label="Hint bar" value={pad.uiMode} onChange={setPadUiMode} options={UI_MODE_OPTIONS} />
        </div>

        <div className="sl-setting">
          <div className="sl-setting__label">
            Glyph set
            <small>Auto follows the connected controller — PlayStation for a DualSense, Xbox for everything else (the Deck's own pad included).</small>
          </div>
          <Seg<GlyphSet> aria-label="Glyph set" value={prefs.set} onChange={setGlyphOverride} options={SET_OPTIONS} />
        </div>

        <div className="sl-setting">
          <div className="sl-setting__label">Glyph style</div>
          <Seg<GlyphVariant>
            aria-label="Glyph style"
            value={prefs.variant}
            onChange={setGlyphVariant}
            options={GLYPH_VARIANTS.map(v => ({ value: v.id, label: v.label }))}
          />
        </div>

        <div className="sl-setting">
          <div className="sl-setting__label">
            Glyph tone
            <small>White matches the app's palette; the pack's coloured faces are the alternative.</small>
          </div>
          <Seg<GlyphTone> aria-label="Glyph tone" value={prefs.tone} onChange={setGlyphTone} options={TONE_OPTIONS} />
        </div>

        <div className="sl-setting">
          <div className="sl-setting__label">
            Glyph size
            <small>{prefs.px} px on the controls. The hint bar stays {GLYPH_BAR_SIZE} px.</small>
          </div>
          <input
            type="range"
            aria-label="Glyph size"
            min={GLYPH_SIZE_MIN}
            max={GLYPH_SIZE_MAX}
            step={1}
            value={prefs.px}
            onChange={e => setGlyphPx(Number.parseFloat(e.target.value))}
            style={{ width: 180, accentColor: 'var(--color-accent)' }}
          />
        </div>

        <div className="sl-setting">
          <div className="sl-setting__label">Preview</div>
          <span className="sl-inline" style={{ gap: 10 }}>
            {PREVIEW.map(g => <Glyph key={g} id={g} set={set} variant={prefs.variant} tone={prefs.tone} size={prefs.px} />)}
          </span>
        </div>

        {!pad.connected && (
          <div className="sl-setting__note">
            Connect a controller and press a button — the hint bar appears on its own, and every
            bound control shows the button that drives it. The keyboard drives the same bindings:
            arrows move · Enter = A · Backspace = B · X/Y · Q/E · Z/R triggers · F/G/V · T.
          </div>
        )}
      </div>
    </Card>
  )
}
