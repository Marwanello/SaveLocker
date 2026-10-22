import { useState, type ComponentProps } from 'react'
import { SettingsView as RealSettingsView } from '../../src/components/SettingsView'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { setState, useMock } from '../store'
import { Dialog } from '../features/common'

type Mode = 'until' | 'keep' | 'off'
const MODES: { id: Mode; name: string; short: string; what: string }[] = [
  { id: 'until', name: 'Only until uploaded', short: 'Until uploaded', what: 'Keeps the save you switch away from until the server confirms its upload, then removes it. Costs no space afterwards.' },
  { id: 'keep', name: 'Keep for offline switching', short: 'Keep for offline', what: 'Keeps the latest save of every playthrough you played here, so you can switch with no server. Files shared between playthroughs are stored once.' },
  { id: 'off', name: 'Off', short: 'Off', what: 'Keeps nothing. Switching needs the server, and stops if the upload fails.' },
]
const GAMES = [
  { name: "Baldur's Gate 3", pts: 3, saves: 152, disk: 104 },
  { name: 'Cyberpunk 2077', pts: 2, saves: 236, disk: 180 },
  { name: 'Hades II', pts: 2, saves: 48, disk: 26 },
]
const space = (m: Mode) => (m === 'keep' ? '310 MB on this device for 436 MB of saves' : m === 'until' ? 'Nothing waiting to upload · 0 B' : 'Nothing kept')

/** The real Settings page, with the planned "Playthrough copies" card first when that feature is on show. */
export function SettingsView(props: ComponentProps<typeof RealSettingsView>) {
  const feature = useMock(s => s.feature)
  if (feature !== 'pt-copies') return <RealSettingsView {...props} />
  return (
    <>
      <div className="sl-page" style={{ paddingBottom: 0 }}><CopiesCard /></div>
      <RealSettingsView {...props} />
    </>
  )
}

function CopiesCard() {
  const variant = useMock(s => s.variant)
  const mode = useMock(s => s.copies)
  const overrides = useMock(s => s.copyOverrides)
  const [clear, setClear] = useState(false)
  const [cleared, setCleared] = useState(false)
  const set = (m: Mode) => setState({ copies: m })
  const cur = MODES.find(m => m.id === mode)!
  const clearBtn = mode === 'keep' && <Button size="sm" onClick={() => setClear(true)}>Clear copies…</Button>

  return (
    <>
      <Card title="Playthrough copies" headerRight={variant === 'C' ? <Chip>Default: {cur.short}</Chip> : undefined}>
        {variant === 'A' && (
          <>
            <div className="sl-setting">
              <div className="sl-setting__label">Keep a copy when switching playthroughs<small>{cur.what}</small></div>
              <div className="sl-seg" role="group" aria-label="Copies">
                {MODES.map(m => <button key={m.id} type="button" aria-pressed={mode === m.id} onClick={() => set(m.id)}>{m.short}</button>)}
              </div>
            </div>
            <div className="sl-setting__note slf-inline" style={{ justifyContent: 'space-between' }}>
              <span>{cleared ? 'Cleared · 1 copy kept until it uploads' : space(mode)}</span>{clearBtn}
            </div>
          </>
        )}
        {variant === 'B' && (
          <>
            <p className="sl-plays__lede" style={{ marginBottom: 12 }}>What this device keeps when you switch a game to another playthrough.</p>
            <div className="sl-plays" role="radiogroup" aria-label="Copies">
              {MODES.map(m => (
                <button key={m.id} type="button" className="sl-plays__opt" role="radio" aria-checked={mode === m.id} onClick={() => set(m.id)}>
                  <span className="sl-plays__radio" />
                  <span className="sl-plays__txt"><b>{m.name}</b><span>{m.what}</span>{m.id === 'until' && <span className="sl-plays__line--info">Recommended</span>}</span>
                </button>
              ))}
            </div>
            <div className="sl-setting__note slf-inline" style={{ justifyContent: 'space-between' }}>
              <span>{cleared ? 'Cleared · 1 copy kept until it uploads' : space(mode)}</span>{clearBtn}
            </div>
            {mode === 'keep' && !cleared && (
              <div className="sl-table-wrap" style={{ marginTop: 12 }}>
                <table className="sl-table">
                  <thead><tr><th>Game</th><th className="sl-num">Playthroughs</th><th className="sl-num">Saves</th><th className="sl-num">On disk</th></tr></thead>
                  <tbody>{GAMES.map(g => <tr key={g.name}><td>{g.name}</td><td className="sl-num sl-dim">{g.pts}</td><td className="sl-num sl-dim">{g.saves} MB</td><td className="sl-num sl-dim">{g.disk} MB</td></tr>)}</tbody>
                </table>
              </div>
            )}
          </>
        )}
        {variant === 'C' && (
          <>
            <div className="sl-setting">
              <div className="sl-setting__label">Default for every game<small>{cur.what}</small></div>
              <select className="sl-input" style={{ width: 'auto' }} value={mode} onChange={e => set(e.target.value as Mode)} aria-label="Default">
                {MODES.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
              </select>
            </div>
            <div className="sl-table-wrap" style={{ marginTop: 14 }}>
              <table className="sl-table">
                <thead><tr><th>Game</th><th>This game</th><th className="sl-num">On disk</th><th /></tr></thead>
                <tbody>{GAMES.map(g => {
                  const o = overrides[g.name] ?? 'default'
                  const eff = o === 'default' ? mode : o
                  return (
                    <tr key={g.name}>
                      <td>{g.name}<br /><span className="slf-dim" style={{ fontSize: 11.5 }}>{g.pts} playthroughs</span></td>
                      <td><select className="sl-input" style={{ width: 'auto' }} value={o} aria-label={`${g.name} copies`}
                        onChange={e => setState({ copyOverrides: { ...overrides, [g.name]: e.target.value } })}>
                        <option value="default">Default ({MODES.find(m => m.id === mode)!.short})</option>
                        {MODES.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
                      </select></td>
                      <td className="sl-num sl-dim">{eff === 'keep' ? `${g.disk} MB` : '0 B'}</td>
                      <td>{eff === 'keep' && <Button size="sm" variant="quiet" onClick={() => setClear(true)}>Clear</Button>}</td>
                    </tr>
                  )
                })}</tbody>
              </table>
            </div>
          </>
        )}
      </Card>
      {clear && (
        <Dialog title="Clear the copies on this device?" onClose={() => setClear(false)}
          actions={<><Button variant="quiet" onClick={() => setClear(false)}>Cancel</Button><Button variant="primary" onClick={() => { setClear(false); setCleared(true) }}>Clear 310 MB</Button></>}>
          <p>Switching offline won’t work for these games until you play them here again.</p>
          <p className="sl-ask__warn">One copy isn’t uploaded yet: <b>Cyberpunk 2077 · Nomad run</b>, from 2 hours ago. It’s kept until the upload finishes.</p>
        </Dialog>
      )}
    </>
  )
}
