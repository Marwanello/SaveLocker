import { useEffect, useRef, useState } from 'react'
import { Check, ChevronDown, GitBranch, Plus } from 'lucide-react'
import { Banner } from '../../src/components/ui/Banner'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { formatAgo, formatBytes } from '../../src/format'
import { getState, ptById, setState, useMock } from '../store'
import { Dialog, Steps, closeFlow, ptLine, startSwitch } from './common'

/* ------------------------------------------------------------------ dialogs */

/** “Play X here?” → the steps → done. Shared by every variant that confirms first. */
export function SwitchDialog({ target, onClose }: { target: string; onClose: () => void }) {
  const flow = useMock(s => s.flow)
  const selected = useMock(s => s.selected)
  const cur = ptById(selected), tgt = ptById(target)
  const close = () => { closeFlow(); onClose() }
  if (flow) {
    return (
      <Dialog
        title={flow.state === 'fail' ? 'Couldn’t switch' : flow.state === 'ok' ? `Switched to ${tgt.name}` : `Switching to ${tgt.name}…`}
        actions={flow.state === 'run' ? <Button disabled>Switching…</Button> : <Button variant={flow.state === 'ok' ? 'primary' : 'default'} onClick={close}>{flow.state === 'ok' ? 'Done' : 'Close'}</Button>}
      >
        <Steps />
      </Dialog>
    )
  }
  return (
    <Dialog
      title={`Play ${tgt.name} on this device?`} onClose={close}
      actions={<><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" onClick={() => startSwitch(target)}>Play {tgt.name}</Button></>}
    >
      <p>Your save here is <b>{cur.name}</b>. It’s uploaded to its own playthrough before <b>{tgt.name}</b> goes in the save folder, so neither is lost. Your other devices keep the playthrough they play.</p>
      <div className="sl-ask__versus">
        <div><span>Stops playing here</span><b>{cur.name}</b>Changed 20 minutes ago</div>
        <div><span>Plays here from now</span><b>{tgt.name}</b>Latest {ptLine(target)}</div>
      </div>
    </Dialog>
  )
}

export function NewPlaythroughDialog({ onClose }: { onClose: () => void }) {
  const [name, setName] = useState('')
  const [from, setFrom] = useState<'here' | 'latest'>('here')
  const [switchNow, setSwitchNow] = useState(true)
  const selected = useMock(s => s.selected)
  const create = () => {
    const id = `new-${Date.now()}`
    const s = getState()
    setState({
      pts: [...s.pts, { id, name: name.trim(), when: new Date().toISOString(), by: 'Steam Deck — Maro', versions: 1, size: 52_428_800, devices: switchNow ? ['Steam Deck — Maro'] : [], local: true }]
        .map(p => (switchNow && p.id !== id ? { ...p, devices: p.devices.filter(d => d !== 'Steam Deck — Maro') } : p)),
      ...(switchNow ? { selected: id } : {}),
    })
    onClose()
  }
  const opt = (v: 'here' | 'latest', title: string, sub: string) => (
    <button type="button" className="sl-plays__opt" role="radio" aria-checked={from === v} onClick={() => setFrom(v)}>
      <span className="sl-plays__radio" /><span className="sl-plays__txt"><b>{title}</b><span>{sub}</span></span>
    </button>
  )
  return (
    <Dialog
      title="New playthrough" onClose={onClose}
      actions={<><Button variant="quiet" onClick={onClose}>Cancel</Button><Button variant="primary" disabled={!name.trim()} onClick={create}>Create</Button></>}
    >
      <p>A separate line of saves for Baldur’s Gate 3. The others stay as they are.</p>
      <div className="sl-field" style={{ marginBottom: 12 }}>
        <label htmlFor="slf-ptname">Name</label>
        <input id="slf-ptname" className="sl-input" value={name} placeholder="e.g. Dark Urge, tactician" onChange={e => setName(e.target.value)} autoFocus />
      </div>
      <div className="sl-plays" role="radiogroup" style={{ marginBottom: 12 }}>
        {opt('here', 'The save on this device', `A copy of ${ptById(selected).name} as it is here`)}
        {opt('latest', 'The latest of another playthrough', `Main · ${ptLine('main')}`)}
      </div>
      <div className="sl-setting">
        <div className="sl-setting__label">Play it on this device now<small>Uploads {ptById(selected).name} first, as a switch does.</small></div>
        <button type="button" className="sl-switch" role="switch" aria-checked={switchNow} aria-label="Play it now" onClick={() => setSwitchNow(!switchNow)} />
      </div>
    </Dialog>
  )
}

/* ------------------------------------------------------------------ A · card */

export function PlaythroughsCardA() {
  const pts = useMock(s => s.pts)
  const selected = useMock(s => s.selected)
  const [ask, setAsk] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const shown = pts.filter(p => !p.archived)
  return (
    <>
      <Card title={<><GitBranch size={14} aria-hidden="true" style={{ marginRight: 6, verticalAlign: -2 }} />Playthroughs</>} flush
        headerRight={<Button size="sm" onClick={() => setAdding(true)}><Plus size={13} aria-hidden="true" />New playthrough</Button>}>
        <div className="sl-table-wrap">
          <table className="sl-table">
            <thead><tr><th>Playthrough</th><th>Latest save</th><th>Played on</th><th className="sl-num">Versions</th><th /></tr></thead>
            <tbody>
              {shown.map(p => (
                <tr key={p.id} className={p.id === selected ? 'slf-sel' : undefined}>
                  <td><b>{p.name}</b></td>
                  <td title={p.when}>{formatAgo(p.when)} <span className="slf-dim">· {p.by}</span></td>
                  <td className="sl-dim">{p.devices.join(', ') || '—'}</td>
                  <td className="sl-num sl-dim">{p.versions}</td>
                  <td>{p.id === selected ? <Chip tone="ok">Plays here</Chip> : <Button size="sm" onClick={() => setAsk(p.id)}>Play here</Button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
      {ask && <SwitchDialog target={ask} onClose={() => setAsk(null)} />}
      {adding && <NewPlaythroughDialog onClose={() => setAdding(false)} />}
    </>
  )
}

/* ------------------------------------------------------------------ B · header chip */

export function PlaythroughChipB() {
  const pts = useMock(s => s.pts)
  const selected = useMock(s => s.selected)
  const [open, setOpen] = useState(false)
  const [ask, setAsk] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const root = useRef<HTMLSpanElement>(null)
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])
  return (
    <span className="slf-pick" ref={root}>
      <button type="button" className="sl-release__chip" aria-expanded={open} onClick={() => setOpen(!open)}>
        <GitBranch size={13} aria-hidden="true" />Playing: <b>{ptById(selected).name}</b><ChevronDown size={13} aria-hidden="true" />
      </button>
      {open && (
        <div className="slf-menu" role="menu">
          {pts.filter(p => !p.archived).map(p => (
            <button key={p.id} type="button" role="menuitemradio" aria-checked={p.id === selected} className="slf-menu__item"
              onClick={() => { setOpen(false); if (p.id !== selected) setAsk(p.id) }}>
              <span className="slf-menu__tick">{p.id === selected && <Check size={14} />}</span>
              <span>{p.name}<small>{formatAgo(p.when)} on {p.by} · {p.versions} versions</small></span>
            </button>
          ))}
          <div className="slf-menu__sep" />
          <button type="button" role="menuitem" className="slf-menu__item" onClick={() => { setOpen(false); setAdding(true) }}>
            <span className="slf-menu__tick"><Plus size={14} /></span>New playthrough…
          </button>
        </div>
      )}
      {ask && <SwitchDialog target={ask} onClose={() => setAsk(null)} />}
      {adding && <NewPlaythroughDialog onClose={() => setAdding(false)} />}
    </span>
  )
}

/* ------------------------------------------------------------------ C · tabs */

export function PlaythroughTabsC({ viewing, onView }: { viewing: string; onView: (id: string) => void }) {
  const pts = useMock(s => s.pts)
  const selected = useMock(s => s.selected)
  const [ask, setAsk] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const p = ptById(viewing)
  return (
    <>
      <div className="slf-inline">
        <div className="sl-seg" role="tablist" aria-label="Playthroughs">
          {pts.filter(q => !q.archived).map(q => (
            <button key={q.id} type="button" role="tab" aria-pressed={q.id === viewing} onClick={() => onView(q.id)}>
              {q.name}{q.id === selected ? ' ✓' : ''}
            </button>
          ))}
        </div>
        <Button size="sm" variant="quiet" onClick={() => setAdding(true)}><Plus size={13} aria-hidden="true" />New</Button>
      </div>
      {viewing === selected
        ? <Banner tone="ok" title={`This device plays ${p.name}`} detail="Sync, the launch check and Versions all use it." />
        : <Banner tone="warn" title={`Viewing ${p.name}`} detail={`Latest ${formatAgo(p.when)} on ${p.by}, ${formatBytes(p.size)}. This device plays ${ptById(selected).name}.`}
            action={<Button variant="primary" onClick={() => setAsk(viewing)}>Play this one here</Button>} />}
      {ask && <SwitchDialog target={ask} onClose={() => setAsk(null)} />}
      {adding && <NewPlaythroughDialog onClose={() => setAdding(false)} />}
    </>
  )
}
