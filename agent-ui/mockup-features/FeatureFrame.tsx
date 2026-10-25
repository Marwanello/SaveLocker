import type { ReactNode } from 'react'
import { CHOSEN, DEMO_GAME, FEATURES, HUNTERS_MELON, HUNTERS_TREES, chooseCombined, chooseFeature, chooseVariant, openGame, setState, useMock, type Sim } from './store'

const SIM_LABEL: Record<Sim, string> = { online: 'Online', offline: 'Server unreachable', running: 'Game running' }

/**
 * The mockup's own chrome — not part of the agent. Pick a feature and a variant; everything below the strip is the
 * real agent UI with that variant built in.
 */
export function FeatureFrame({ children }: { children: ReactNode }) {
  const featureId = useMock(s => s.feature)
  const variant = useMock(s => s.variant)
  const sim = useMock(s => s.sim)
  const notes = useMock(s => s.notes)
  const treeId = useMock(s => s.treeId)
  const combined = useMock(s => s.combined)
  const f = FEATURES.find(x => x.id === featureId)!
  const v = f.variants.find(x => x.id === variant)!
  const tasks = [...new Set(FEATURES.map(x => x.task))]

  return (
    <div className="mock">
      <div className="mock-toolbar">
        <h1>SaveLocker agent<small>feature mockups · the real agent UI with a mock backend</small></h1>
        <button className="mock-btn" aria-pressed={combined} onClick={() => chooseCombined(!combined)}>All chosen</button>
        {tasks.map(t => (
          <div className="mock-group" key={t}>
            <span className="mock-label">{t}</span>
            {FEATURES.filter(x => x.task === t).map(x => (
              <button key={x.id} className="mock-btn" aria-pressed={!combined && x.id === featureId} onClick={() => chooseFeature(x.id)}>{x.name}</button>
            ))}
          </div>
        ))}
        <div className="mock-spacer" />
        <a className="mock-btn" href="console.html">Console mockups →</a>
        <a className="mock-btn" href="handheld.html">Decky &amp; Playnite →</a>
      </div>
      <div className="mock-toolbar mockf-row">
        {combined ? (
          <div className="mock-group">
            <span className="mock-label">Open</span>
            <button className="mock-btn" onClick={() => openGame(DEMO_GAME)}>Baldur’s Gate 3</button>
            <button className="mock-btn" onClick={() => openGame(HUNTERS_MELON)}>Metroid Prime - Hunters</button>
            <button className="mock-btn" onClick={() => { history.replaceState(null, '', '#settings'); window.dispatchEvent(new HashChangeEvent('hashchange')) }}>Settings</button>
          </div>
        ) : <div className="mock-group">
          <span className="mock-label">Variant</span>
          {f.variants.map(x => (
            <button key={x.id} className="mock-btn" aria-pressed={x.id === variant} onClick={() => chooseVariant(x.id)}>{x.id} · {x.name}</button>
          ))}
        </div>}
        {(combined || f.sims) && (
          <div className="mock-group">
            <span className="mock-label">Mock only</span>
            {(combined ? ['online', 'offline', 'running'] as Sim[] : f.sims!).map(s => (
              <button key={s} className="mock-btn" aria-pressed={sim === s} onClick={() => setState({ sim: s })}>{SIM_LABEL[s]}</button>
            ))}
          </div>
        )}
        {!combined && f.games && (
          <div className="mock-group">
            <span className="mock-label">Game</span>
            <button className="mock-btn" aria-pressed={treeId === DEMO_GAME} onClick={() => openGame(DEMO_GAME)}>Baldur’s Gate 3</button>
            <button className="mock-btn" aria-pressed={HUNTERS_TREES.includes(treeId)} onClick={() => openGame(HUNTERS_MELON)}>Metroid Prime - Hunters · 2 emulators</button>
          </div>
        )}
        <div className="mock-spacer" />
        <button className="mock-btn" aria-pressed={notes} onClick={() => setState({ notes: !notes })}>Notes</button>
        <button className="mock-btn" onClick={() => chooseVariant(variant)}>Start again</button>
      </div>
      {notes && combined && (
        <div className="mockf-notes">
          <p><b>All chosen.</b> Every planned feature at once, each in your pick, so you can see how they sit together on the real pages.</p>
          <p><span className="mock-label">On</span> {FEATURES.map(x => `${x.name} ${CHOSEN[x.id]} · ${x.variants.find(v => v.id === CHOSEN[x.id])!.name}`).join(' · ')}</p>
          <p><span className="mock-label">Try it</span> Baldur’s Gate 3: switch playthrough tabs, then name a version and restore a folder from the playthrough on show. Settings: the sections and Playthrough copies. Metroid Prime - Hunters: the emulator tabs over the playthrough tabs.</p>
        </div>
      )}
      {notes && !combined && (
        <div className="mockf-notes">
          <p><b>{v.id} · {v.name}.</b> {v.what}</p>
          <p><span className="mock-label">Try it</span> {v.try}</p>
          <p><span className="mock-label">In every variant</span> {f.always.join(' ')}</p>
          <p><span className="mock-label">My pick</span> {f.pick}</p>
        </div>
      )}
      <div className="mock-app">{children}</div>
    </div>
  )
}
