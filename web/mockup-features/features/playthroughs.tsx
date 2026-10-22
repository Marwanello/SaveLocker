import { useState } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { DataTable } from '../../src/components/ui/DataTable'
import { Icon } from '../../src/components/ui/Icon'
import { Select } from '../../src/components/ui/Select'
import { ago, fmtSize } from '../../src/format'
import { toast } from '../../src/toast'
import { M, MACHINE_NAMES, devicesOn, endFlow, getState, ptById, runFlow, setState, useMock, type Pt } from '../store'
import { Dialog, Steps, fieldCls } from './common'

type Ask = { kind: 'move'; device?: string; to?: string } | { kind: 'rename'; id: string } | { kind: 'delete'; id: string } | { kind: 'new' } | null

/** The planned Playthroughs card on the console's game page, in the variant on show. */
export function PlaythroughsCard() {
  const variant = useMock(s => s.variant)
  const pts = useMock(s => s.pts)
  useMock(s => s.plays)
  const def = useMock(s => s.def)
  const [ask, setAsk] = useState<Ask>(null)
  const [showArchived, setShowArchived] = useState(false)
  const [open, setOpen] = useState<string | null>(null)
  const [pick, setPick] = useState('main')
  const shown = pts.filter(p => showArchived || !p.archived)

  const menu = (p: Pt) => <RowMenu p={p} isDefault={p.id === def} onAsk={setAsk} />
  const header = (
    <span className="flex items-center gap-1.5 flex-wrap">
      <label className="flex items-center gap-1.5 text-[11.5px] text-dim cursor-pointer">
        <input type="checkbox" className="accent-[var(--color-accent)]" checked={showArchived} onChange={e => setShowArchived(e.target.checked)} />Show archived
      </label>
      <Button size="sm" onClick={() => setAsk({ kind: 'move' })}>Switch a device…</Button>
      <Button size="sm" onClick={() => setAsk({ kind: 'new' })}><Icon name="plus" size={13} />New playthrough</Button>
    </span>
  )

  let body
  if (variant === 'A') {
    body = (
      <DataTable caption="Playthroughs" rows={shown} rowKey={p => p.id}
        columns={[
          { head: 'Playthrough', kind: 'k', cell: p => <span className="flex items-center gap-1.5 flex-wrap">{p.name}{p.id === def && <Chip>Default</Chip>}{p.archived && <Chip>Archived</Chip>}</span> },
          { head: 'Latest', kind: 'n', cell: p => <span title={p.when}>{ago(p.when)}</span> },
          { head: 'From', cell: p => p.by },
          { head: 'Versions', kind: 'n', cell: p => p.versions },
          { head: 'Size', kind: 'n', cell: p => fmtSize(p.size) },
          { head: 'Played on', kind: 'wrap', cell: p => <span className="flex gap-1 flex-wrap">{devicesOn(p.id).map(d => <Chip key={d} tone="ok">{d}</Chip>)}{devicesOn(p.id).length === 0 && <span className="text-faint">No device</span>}</span> },
          { head: '', label: 'Actions', end: true, cell: menu },
        ]} />
    )
  } else if (variant === 'B') {
    const sel = open ? ptById(open) : null
    body = (
      <div className="grid md:grid-cols-[minmax(0,1fr)_280px]">
        <div>
          {shown.map(p => {
            const n = Math.min(p.versions, 12)
            return (
              <button key={p.id} type="button" onClick={() => setOpen(p.id)}
                className={`w-full grid grid-cols-[140px_minmax(0,1fr)_auto] items-center gap-3 px-4 py-3 border-b border-row text-left cursor-pointer hover:bg-hover ${open === p.id ? 'bg-raise' : ''}`}>
                <span className="min-w-0"><span className="block text-[13px] font-semibold text-fg truncate">{p.name}</span><span className="text-[11.5px] text-dim">{ago(p.when)}</span></span>
                <span className="relative h-6">
                  <span className="absolute inset-x-0 top-1/2 h-px bg-line" />
                  {Array.from({ length: n }, (_, i) => (
                    <span key={i} className={`absolute top-1/2 -mt-[5px] w-2.5 h-2.5 rounded-full ${i === n - 1 ? 'bg-accent' : 'bg-info'}`} style={{ left: `${(i / Math.max(1, n - 1)) * 96}%` }} />
                  ))}
                </span>
                <span className="flex gap-1">{devicesOn(p.id).map(d => <Chip key={d}>{d.split(/\s+/).map(w => w[0]).join('')}</Chip>)}</span>
              </button>
            )
          })}
          <p className="px-4 py-2.5 text-[11.5px] text-dim">Each dot is a version, the newest in accent. Initials are the devices that play it.</p>
        </div>
        <div className="border-l border-line p-4 flex flex-col gap-3">
          {!sel ? <p className="text-[12.5px] text-dim">Pick a playthrough to see its details and actions.</p> : (
            <>
              <div><div className="text-[15px] font-semibold text-fg">{sel.name}</div><div className="text-xs text-dim">{sel.versions} versions · {fmtSize(sel.size)}</div></div>
              <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-[12.5px]">
                <dt className="text-faint">Latest</dt><dd className="text-fg">{ago(sel.when)} on {sel.by}</dd>
                <dt className="text-faint">Played on</dt><dd className="text-fg">{devicesOn(sel.id).join(', ') || 'No device'}</dd>
                <dt className="text-faint">New devices</dt><dd className="text-fg">{sel.id === def ? 'Start here' : `Start on ${ptById(def).name}`}</dd>
              </dl>
              <Button size="sm" onClick={() => setAsk({ kind: 'move', to: sel.id })}>Switch a device to this…</Button>
              <Button size="sm" onClick={() => setAsk({ kind: 'rename', id: sel.id })}>Rename</Button>
              {sel.id !== def && <Button size="sm" onClick={() => { setState({ def: sel.id }); toast(`New devices now start on ${sel.name}.`) }}>Make default for new devices</Button>}
              <Button size="sm" variant="quiet" onClick={() => archive(sel.id)}>{sel.archived ? 'Unarchive' : 'Archive'}</Button>
              <Button size="sm" variant="alert" onClick={() => setAsk({ kind: 'delete', id: sel.id })}>Delete…</Button>
            </>
          )}
        </div>
      </div>
    )
  } else {
    const p = ptById(pick) ?? pts[0]
    body = (
      <div className="grid md:grid-cols-[220px_minmax(0,1fr)]">
        <div className="border-r border-line">
          {shown.map(q => (
            <button key={q.id} type="button" onClick={() => setPick(q.id)}
              className={`w-full text-left px-4 py-3 border-b border-row cursor-pointer hover:bg-hover ${q.id === p.id ? 'bg-raise' : ''}`}>
              <span className="block text-[13px] font-semibold text-fg">{q.name}</span>
              <span className="text-[11.5px] text-dim">{ago(q.when)} · {devicesOn(q.id).length} device{devicesOn(q.id).length === 1 ? '' : 's'}</span>
            </button>
          ))}
        </div>
        <div className="p-4 flex flex-col gap-3.5 min-w-0">
          <div className="flex items-start justify-between gap-3">
            <div><div className="text-[15px] font-semibold text-fg flex items-center gap-1.5">{p.name}{p.id === def && <Chip>Default for new devices</Chip>}</div>
              <div className="text-xs text-dim">{p.versions} versions · {fmtSize(p.size)} · latest {ago(p.when)} on {p.by}</div></div>
            {menu(p)}
          </div>
          <div>
            <div className="text-[10px] tracking-[0.12em] uppercase text-faint mb-1.5">Devices</div>
            <div className="border border-line rounded-xl overflow-hidden">
              {Object.entries(MACHINE_NAMES).map(([id, name]) => {
                const here = getState().plays[id] === p.id
                return (
                  <div key={id} className="flex items-center gap-3 px-3.5 py-2.5 border-b border-row last:border-b-0">
                    <Icon name="monitor" size={14} className="text-faint" />
                    <span className="flex-1 min-w-0"><span className="block text-[13px] text-fg">{name}</span><span className="text-[11.5px] text-dim">{here ? 'Plays this' : `Plays ${ptById(getState().plays[id]).name}`}</span></span>
                    {here ? <Chip tone="ok">Selected</Chip> : <Button size="sm" onClick={() => setAsk({ kind: 'move', device: id, to: p.id })}>Switch to this</Button>}
                  </div>
                )
              })}
            </div>
          </div>
        </div>
      </div>
    )
  }

  return (
    <>
      <Card title="Playthroughs" headerRight={header} flush>{body}</Card>
      {ask?.kind === 'move' && <MoveDialog device={ask.device} to={ask.to} onClose={() => setAsk(null)} />}
      {ask?.kind === 'rename' && <RenameDialog id={ask.id} onClose={() => setAsk(null)} />}
      {ask?.kind === 'delete' && <DeleteDialog id={ask.id} onClose={() => setAsk(null)} />}
      {ask?.kind === 'new' && <NewDialog onClose={() => setAsk(null)} />}
    </>
  )
}

function archive(id: string) {
  const s = getState()
  setState({ pts: s.pts.map(p => (p.id === id ? { ...p, archived: !p.archived } : p)) })
  toast(ptById(id).archived ? 'Archived. Show archived lists it.' : 'Unarchived.')
}

function RowMenu({ p, isDefault, onAsk }: { p: Pt; isDefault: boolean; onAsk: (a: Ask) => void }) {
  return (
    <span className="inline-flex gap-1.5 justify-end flex-wrap">
      <Button size="sm" variant="quiet" onClick={() => onAsk({ kind: 'rename', id: p.id })}>Rename</Button>
      {!isDefault && !p.archived && <Button size="sm" variant="quiet" onClick={() => { setState({ def: p.id }); toast(`New devices now start on ${p.name}.`) }}>Make default</Button>}
      <Button size="sm" variant="quiet" onClick={() => archive(p.id)}>{p.archived ? 'Unarchive' : 'Archive'}</Button>
      <Button size="sm" variant="quiet" onClick={() => onAsk({ kind: 'delete', id: p.id })}>Delete…</Button>
    </span>
  )
}

function MoveDialog({ device, to, onClose }: { device?: string; to?: string; onClose: () => void }) {
  const flow = useMock(s => s.flow)
  const [dev, setDev] = useState(device ?? M.deck)
  const [target, setTarget] = useState(to ?? 'hard')
  const name = MACHINE_NAMES[dev]
  const close = () => { endFlow(); onClose() }
  const send = () => {
    const running = getState().sim === 'running'
    const from = ptById(getState().plays[dev]).name
    runFlow([
      { t: `Sent to ${name}`, d: 'Devices pick up commands within 20 seconds' },
      { t: `${name} picked it up` },
      { t: `${name} uploaded its save to ${from}` },
      { t: `${name} now plays ${ptById(target).name}` },
    ], {
      failAt: running ? 1 : -1, failMsg: `Elden Ring is running on ${name}, so it didn’t switch. Nothing changed. Try again when the game is closed.`,
      done: ok => { if (ok) setState({ plays: { ...getState().plays, [dev]: target } }) },
    })
  }
  return (
    <Dialog title={flow ? (flow.state === 'fail' ? `${name} didn’t switch` : flow.state === 'ok' ? `${name} switched` : `Switching ${name}…`) : 'Switch a device'}
      sub={flow ? undefined : 'The device runs the switch itself: it uploads its current save first, and refuses while the game is running.'}
      onClose={close}
      actions={!flow ? <><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" disabled={getState().plays[dev] === target} onClick={send}>Send to {name}</Button></>
        : flow.state === 'run' ? <Button disabled>Waiting for {name}…</Button> : <Button variant="primary" onClick={close}>Done</Button>}>
      {!flow ? (
        <div className="grid grid-cols-[90px_minmax(0,1fr)] gap-x-3 gap-y-2.5 items-center">
          <span className="text-[12.5px] text-dim">Device</span>
          <Select label="Device" variant="field" value={dev} onChange={setDev}
            options={Object.entries(MACHINE_NAMES).map(([id, n]) => ({ value: id, label: n, sub: `Plays ${ptById(getState().plays[id]).name}` }))} />
          <span className="text-[12.5px] text-dim">Switch it to</span>
          <Select label="Playthrough" variant="field" value={target} onChange={setTarget}
            options={getState().pts.filter(p => !p.archived).map(p => ({ value: p.id, label: p.name, sub: `Latest ${ago(p.when)} on ${p.by}` }))} />
        </div>
      ) : <Steps />}
    </Dialog>
  )
}

function RenameDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const [name, setName] = useState(ptById(id).name)
  return (
    <Dialog title="Rename playthrough" sub="Every device shows the new name." onClose={onClose}
      actions={<><Button variant="quiet" onClick={onClose}>Cancel</Button><Button variant="primary" disabled={!name.trim()}
        onClick={() => { setState({ pts: getState().pts.map(p => (p.id === id ? { ...p, name: name.trim() } : p)) }); onClose(); toast('Renamed.') }}>Rename</Button></>}>
      <input autoFocus className={fieldCls} value={name} onChange={e => setName(e.target.value)} aria-label="Name" maxLength={60} />
    </Dialog>
  )
}

function DeleteDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const p = ptById(id)
  const def = getState().def === id
  const on = devicesOn(id)
  if (def || on.length) {
    return (
      <Dialog title={`Can’t delete ${p.name} yet`} onClose={onClose} actions={<Button variant="primary" onClick={onClose}>OK</Button>}>
        <p>{def ? 'It’s the default for new devices. Make another playthrough the default first.' : `${on.join(', ')} ${on.length > 1 ? 'play' : 'plays'} it. Switch ${on.length > 1 ? 'them' : 'it'} to another playthrough first.`}</p>
      </Dialog>
    )
  }
  return (
    <Dialog title={`Delete ${p.name}?`} onClose={onClose}
      actions={<><Button variant="quiet" onClick={onClose}>Cancel</Button><Button onClick={() => { archive(id); onClose() }}>Archive instead</Button>
        <Button variant="alert" onClick={() => { setState({ pts: getState().pts.filter(q => q.id !== id) }); onClose(); toast('Moved to trash. Restore it from Backups › Trash within 14 days.') }}>Move to trash</Button></>}>
      <p>Its {p.versions} versions ({fmtSize(p.size)}) go to trash for 14 days, and Restore brings the playthrough back with them. Archive hides it with no time limit.</p>
    </Dialog>
  )
}

function NewDialog({ onClose }: { onClose: () => void }) {
  const [name, setName] = useState('')
  const [from, setFrom] = useState('main')
  return (
    <Dialog title="New playthrough" sub="A separate line of saves. The others stay as they are; no device switches until you switch it." onClose={onClose}
      actions={<><Button variant="quiet" onClick={onClose}>Cancel</Button><Button variant="primary" disabled={!name.trim()} onClick={() => {
        const id = `new-${Date.now()}`
        setState({ pts: [...getState().pts, { id, name: name.trim(), when: new Date().toISOString(), by: ptById(from).by, versions: 1, size: ptById(from).size }] })
        onClose(); toast(`Created ${name.trim()} from ${ptById(from).name}’s latest.`)
      }}>Create</Button></>}>
      <label className="flex flex-col gap-1.5"><span className="text-[10px] tracking-[0.13em] uppercase text-faint">Name</span>
        <input autoFocus className={fieldCls} value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Sorcery build" maxLength={60} /></label>
      <label className="flex flex-col gap-1.5"><span className="text-[10px] tracking-[0.13em] uppercase text-faint">Start from the latest of</span>
        <Select label="Start from" variant="field" value={from} onChange={setFrom} options={getState().pts.filter(p => !p.archived).map(p => ({ value: p.id, label: p.name, sub: `${ago(p.when)} on ${p.by}` }))} /></label>
      <p className="text-xs">To start from an older save, use “New playthrough from this version” in Versions.</p>
    </Dialog>
  )
}
