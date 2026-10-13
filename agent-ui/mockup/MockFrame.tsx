import type { ReactNode } from 'react'
import {
  GLYPH_SIZE_MAX, GLYPH_SIZE_MIN, GLYPH_VARIANTS, Glyph,
  setGlyphOverride, setGlyphPx, setGlyphTone, setGlyphVariant, useGlyphPrefs,
} from '../src/gamepad/glyphs'
import { setPadUiMode, usePad } from '../src/gamepad/pad'

/**
 * The mockup's own chrome — not part of the agent UI. The controller layer itself (hint bar, OSK,
 * input) is mounted by the real App; this strip only switches what is being demonstrated.
 */
export function MockFrame({ children }: { children: ReactNode }) {
  const prefs = useGlyphPrefs()
  const pad = usePad()
  const set = prefs.set === 'auto' ? pad.detected : prefs.set

  return (
    <div className="mock">
      <div className="mock-toolbar">
        <h1>SaveLocker agent UI<small>controller mockup — the real components, a mock backend, live pad navigation</small></h1>
        <div className="mock-group">
          <span className="mock-label">Glyphs</span>
          {(['auto', 'xbox', 'ps'] as const).map(g => (
            <button key={g} className="mock-btn" aria-pressed={prefs.set === g} onClick={() => setGlyphOverride(g)}>
              {g === 'auto' ? 'Auto' : g === 'xbox' ? 'Xbox' : 'PlayStation'}
            </button>
          ))}
        </div>
        <div className="mock-group">
          <span className="mock-label">Style</span>
          {GLYPH_VARIANTS.map(v => (
            <button key={v.id} className="mock-btn" aria-pressed={prefs.variant === v.id} onClick={() => setGlyphVariant(v.id)}>
              {v.label}
            </button>
          ))}
        </div>
        <div className="mock-group">
          <span className="mock-label">Size</span>
          <input
            className="mock-range"
            type="range"
            min={GLYPH_SIZE_MIN}
            max={GLYPH_SIZE_MAX}
            step={1}
            value={prefs.px}
            aria-label="Glyph size"
            onChange={e => setGlyphPx(Number.parseFloat(e.target.value))}
          />
          <span className="mock-range-value">{prefs.px} px <span className="mock-range-note">(bar 22)</span></span>
        </div>
        <div className="mock-group">
          <span className="mock-label">Tone</span>
          {(['white', 'color'] as const).map(t => (
            <button key={t} className="mock-btn" aria-pressed={prefs.tone === t} onClick={() => setGlyphTone(t)}>
              {t === 'white' ? 'White' : 'Color'}
            </button>
          ))}
        </div>
        <div className="mock-group">
          <span className="mock-label">Pad bar</span>
          {(['auto', 'on', 'off'] as const).map(m => (
            <button key={m} className="mock-btn" aria-pressed={pad.uiMode === m} onClick={() => setPadUiMode(m)}>
              {m === 'auto' ? 'Auto' : m === 'on' ? 'On' : 'Off'}
            </button>
          ))}
        </div>
        <div className="mock-group mock-preview-group">
          <span className="mock-label">Preview</span>
          {(['a', 'b', 'x', 'y', 'lb', 'rb', 'lt', 'rt', 'l3', 'r3', 'view', 'start', 'move', 'stick', 'rstick'] as const).map(g => (
            <Glyph key={g} id={g} set={set} variant={prefs.variant} tone={prefs.tone} size={prefs.px} />
          ))}
        </div>
        <div className="mock-spacer" />
        <button className="mock-btn" onClick={() => location.reload()}>Reset</button>
        <div className="mock-pad-state">
          <span className={`mock-dot${pad.connected ? ' mock-dot--ok' : ''}`} />
          {pad.connected
            ? pad.controller.replace(/\s*\(.*\)$/, '')
            : 'No gamepad — arrows move · Enter A · Backspace B · X · Y · Q/E bumpers · V List/Grid · T Start'}
        </div>
      </div>

      <div className="mock-app">
        {children}
      </div>
    </div>
  )
}
