import { useOsk, oskInsert, oskBackspace, oskCommit, oskClose, focusFirstInScope } from './nav'

/** Fallback text entry: a compact QWERTY the D-pad can drive, typing into the real input through a
 *  native value setter + input event so React's controlled fields see it like any other keystroke. */

const ROWS = [
  ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0'],
  ['q', 'w', 'e', 'r', 't', 'y', 'u', 'i', 'o', 'p'],
  ['a', 's', 'd', 'f', 'g', 'h', 'j', 'k', 'l'],
  ['z', 'x', 'c', 'v', 'b', 'n', 'm', '.', '-', '_'],
]

export function Osk() {
  const osk = useOsk()
  if (!osk.open) return null
  return (
    <div className="sl-osk">
      <div className="sl-osk__panel" role="dialog" aria-modal="true" aria-label="On-screen keyboard">
        <div className="sl-osk__head">
          <span className="sl-osk__title">Type — {osk.title}</span>
        </div>
        <div className="sl-osk__field">{osk.text}<span className="sl-osk__caret" /></div>
        <div className="sl-osk__rows">
          {ROWS.map((row, i) => (
            <div key={i} className="sl-osk__row">
              {i === 3 && <span className="sl-osk__sp" />}
              {row.map(ch => (
                <button key={ch} type="button" className="sl-osk__key" onClick={() => oskInsert(ch)}>{ch}</button>
              ))}
            </div>
          ))}
          <div className="sl-osk__row">
            <button type="button" className="sl-osk__key sl-osk__key--wide" onClick={() => oskInsert(' ')}>space</button>
            <button type="button" className="sl-osk__key" aria-label="Backspace" onClick={oskBackspace}>⌫</button>
          </div>
        </div>
        <div className="sl-osk__foot">
          <button type="button" className="sl-btn sl-btn--sm" onClick={() => { oskClose(); focusFirstInScope() }}>Cancel</button>
          <button type="button" className="sl-btn sl-btn--sm sl-btn--primary" onClick={oskCommit}>Done</button>
        </div>
      </div>
    </div>
  )
}
