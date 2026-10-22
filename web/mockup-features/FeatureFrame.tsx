import type { ReactNode } from 'react'
import { FEATURES, chooseFeature, chooseVariant, setState, useMock } from './store'

/** The mockup's own strip above the real console: feature, variant, mock-only switches and notes. */
export function FeatureFrame({ children }: { children: ReactNode }) {
  const featureId = useMock(s => s.feature)
  const variant = useMock(s => s.variant)
  const sim = useMock(s => s.sim)
  const notes = useMock(s => s.notes)
  const f = FEATURES.find(x => x.id === featureId)!
  const v = f.variants.find(x => x.id === variant)!
  const tasks = [...new Set(FEATURES.map(x => x.task))]
  return (
    <div className="mockc">
      <div className="mockc-bar">
        <h1>SaveLocker console<small>feature mockups · the real console with a mock backend</small></h1>
        {tasks.map(t => (
          <div className="mockc-group" key={t}>
            <span className="mockc-label">{t}</span>
            {FEATURES.filter(x => x.task === t).map(x => (
              <button key={x.id} type="button" className="mockc-btn" aria-pressed={x.id === featureId} onClick={() => chooseFeature(x.id)}>{x.name}</button>
            ))}
          </div>
        ))}
        <div className="mockc-spacer" />
        <a className="mockc-btn" href="agent.html">Agent mockups →</a>
        <a className="mockc-btn" href="handheld.html">Decky &amp; Playnite →</a>
      </div>
      <div className="mockc-bar">
        <div className="mockc-group">
          <span className="mockc-label">Variant</span>
          {f.variants.map(x => (
            <button key={x.id} type="button" className="mockc-btn" aria-pressed={x.id === variant} onClick={() => chooseVariant(x.id)}>{x.id} · {x.name}</button>
          ))}
        </div>
        {f.sims && (
          <div className="mockc-group">
            <span className="mockc-label">Mock only</span>
            {f.sims.map(([k, label]) => (
              <button key={k} type="button" className="mockc-btn" aria-pressed={sim === k} onClick={() => setState({ sim: k })}>{label}</button>
            ))}
          </div>
        )}
        <div className="mockc-spacer" />
        <button type="button" className="mockc-btn" aria-pressed={notes} onClick={() => setState({ notes: !notes })}>Notes</button>
        <button type="button" className="mockc-btn" onClick={() => chooseVariant(variant)}>Start again</button>
      </div>
      {notes && (
        <div className="mockc-notes">
          <p><b>{v.id} · {v.name}.</b> {v.what}</p>
          <p><span className="mockc-label">Try it</span> {v.try}</p>
          <p><span className="mockc-label">In every variant</span> {f.always.join(' ')}</p>
          <p><span className="mockc-label">My pick</span> {f.pick}</p>
        </div>
      )}
      <div className="mockc-app">{children}</div>
    </div>
  )
}
