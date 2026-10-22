import { useState } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { DataTable } from '../../src/components/ui/DataTable'
import { Icon } from '../../src/components/ui/Icon'
import { InlineConfirm } from '../../src/components/ui/InlineConfirm'
import { Select } from '../../src/components/ui/Select'
import { ago, fmtSize } from '../../src/format'
import { toast } from '../../src/toast'
import type { Game, Version, VersionChanges } from '../../src/types'
import { VersionFiles } from '../overrides/VersionFiles'
import { G, filesOf } from '../fixtures'
import { M, MACHINE_NAMES, devicesOn, endFlow, getState, ptById, runFlow, setState, useMock, type Pt } from '../store'
import { Choice, Dialog, Steps, fieldCls } from './common'

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
              {sel.id !== def && <Button size="sm" variant="quiet" onClick={() => archive(sel.id)}>{sel.archived ? 'Unarchive' : 'Archive'}</Button>}
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
      {!isDefault && <Button size="sm" variant="quiet" onClick={() => archive(p.id)}>{p.archived ? 'Unarchive' : 'Archive'}</Button>}
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
        <Button variant="alert" onClick={() => { setState({ pts: getState().pts.filter(q => q.id !== id) }); onClose(); toast('Moved to trash. Restore it from Storage › Trash within 14 days.') }}>Move to trash</Button></>}>
      <p>Its {p.versions} versions ({fmtSize(p.size)}) go to trash for 14 days, and Restore brings the playthrough back with them. Archive hides it with no time limit.</p>
    </Dialog>
  )
}

function NewDialog({ onClose }: { onClose: () => void }) {
  const [name, setName] = useState('')
  const [from, setFrom] = useState('main')
  const [how, setHow] = useState<'current' | 'fresh'>('current')
  const [keep, setKeep] = useState<Record<string, boolean>>({ 'GraphicsConfig.xml': true, 'ER0000.sl2': false, 'ER0000.sl2.bak': false })
  const kept = Object.keys(keep).filter(k => keep[k])
  return (
    <Dialog title="New playthrough" sub="A separate line of saves. The others stay as they are; no device switches until you switch it." onClose={onClose}
      actions={<><Button variant="quiet" onClick={onClose}>Cancel</Button><Button variant="primary" disabled={!name.trim()} onClick={() => {
        const id = `new-${Date.now()}`
        const fresh = how === 'fresh'
        setState({ pts: [...getState().pts, { id, name: name.trim(), when: new Date().toISOString(), by: fresh ? 'This console' : ptById(from).by, versions: 1, size: fresh ? 4_096 : ptById(from).size }] })
        onClose(); toast(fresh ? `Created ${name.trim()}: an empty save${kept.length ? ` with ${kept.join(', ')}` : ''}.` : `Created ${name.trim()} from ${ptById(from).name}’s latest.`)
      }}>Create</Button></>}>
      <label className="flex flex-col gap-1.5"><span className="text-[10px] tracking-[0.13em] uppercase text-faint">Name</span>
        <input autoFocus className={fieldCls} value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Sorcery build" maxLength={60} /></label>
      <div className="flex flex-col gap-2" role="radiogroup" aria-label="Start from">
        <Choice checked={how === 'current'} onPick={() => setHow('current')} title="Current save" sub="A copy of a playthrough’s latest save" />
        {how === 'current' && <div className="pl-7"><Select label="Start from" variant="field" value={from} onChange={setFrom} options={getState().pts.filter(p => !p.archived).map(p => ({ value: p.id, label: p.name, sub: `${ago(p.when)} on ${p.by}` }))} /></div>}
        <Choice checked={how === 'fresh'} onPick={() => setHow('fresh')} title="Start fresh" sub="An empty save folder, so the game starts a new game. Keep files like settings if you want." />
      </div>
      {how === 'fresh' && (
        <div className="border border-line rounded-xl overflow-hidden">
          {Object.keys(keep).map(f => (
            <label key={f} className="flex items-center gap-2.5 px-3.5 py-2 border-b border-row last:border-b-0 cursor-pointer">
              <input type="checkbox" className="accent-[var(--color-accent)]" checked={keep[f]} onChange={e => setKeep({ ...keep, [f]: e.target.checked })} />
              <span className="font-mono text-[12px] text-fg flex-1">{f}</span>
              <span className="text-[11.5px] text-dim">{f.endsWith('.xml') ? 'Settings' : 'Save data'}</span>
            </label>
          ))}
          <p className="px-3.5 py-2 text-[11.5px] text-dim bg-raise">{kept.length ? `Keeps ${kept.join(', ')} from ${ptById(from).name}.` : 'Nothing is kept: the folder starts empty.'}</p>
        </div>
      )}
      <p className="text-xs">To start from an older save, use “New playthrough from this version” in Versions.</p>
    </Dialog>
  )
}

/* ------------------------------------------------------------------ D and E · playthroughs and their versions on one card */

const hex8 = (key: string) => {
  let h = 0x811c9dc5
  for (const c of key) h = Math.imul(h ^ c.charCodeAt(0), 0x01000193)
  return (Math.imul(h ^ (h >>> 15), 0x2c1b3c6d) >>> 0).toString(16).padStart(8, '0')
}

/** Each playthrough's own history, shaped like the server's versions so the real file tree can open them. */
function versionsOf(p: Pt, gameId: string): Version[] {
  const gaps = [0, 3, 26, 50, 75, 140, 220, 300, 410]
  const n = Math.min(p.versions, 9)
  const id = (i: number) => `${hex8(`${p.id}-${i}`)}-0000-4000-8000-000000000000`
  return Array.from({ length: n }, (_, i) => ({
    id: id(i), gameId, machineId: null,
    machineName: i % 3 === 1 ? 'Battlestation' : p.by,
    createdAt: new Date(Date.parse(p.when) - gaps[i] * 3_600_000).toISOString(),
    contentHash: hex8(`h${p.id}${i}`), size: Math.round(p.size * (1 - i * 0.01)), parentVersionId: i < n - 1 ? id(i + 1) : null, protected: false,
  }) as unknown as Version)
}

function changesOf(v: Version, game: Game, first: boolean): VersionChanges {
  const files = filesOf(game.id)
  return {
    versionId: v.id, baseMissing: false, added: first ? files.length : 0, changed: first ? 0 : 1, removed: 0, unchanged: first ? 0 : files.length - 1,
    files: first ? files.map(([path, size]) => ({ key: 'main', path, change: 'Added' as const, size }))
      : [{ key: 'main', path: files[0][0], change: 'Changed' as const, size: files[0][1] }],
  } as VersionChanges
}

function DeviceChips({ names, empty = 'No device plays it' }: { names: string[]; empty?: string }) {
  return names.length
    ? <span className="flex gap-1 flex-wrap">{names.map(d => <Chip key={d} tone="ok"><Icon name="monitor" size={11} />{d}</Chip>)}</span>
    : <span className="text-[11.5px] text-faint">{empty}</span>
}

function PtVersionsTable({ p, game, rows }: { p: Pt; game: Game; rows: Version[] }) {
  const [latest, setLatest] = useState<Record<string, string>>({})
  const [names, setNames] = useState<Record<string, string>>({})
  const [naming, setNaming] = useState<Version | null>(null)
  const [open, setOpen] = useState<Set<string>>(new Set())
  const [text, setText] = useState('')
  const head = latest[p.id] ?? rows[0]?.id
  const on = devicesOn(p.id)
  const toggle = (id: string) => setOpen(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n })
  return (
    <>
      <DataTable caption={`Versions of ${p.name}`} rows={rows} rowKey={v => v.id}
        expanded={v => open.has(v.id)
          ? <VersionFiles id={`pt-files-${v.id}`} game={game} version={v} changes={changesOf(v, game, v.id === rows[rows.length - 1].id)} status="loaded" />
          : null}
        columns={[
          { head: 'Version', kind: ['k', 'm'], cell: v => (
            <span className="flex items-center gap-1.5">
              <button type="button" onClick={() => toggle(v.id)} aria-expanded={open.has(v.id)} aria-controls={`pt-files-${v.id}`}
                aria-label={`${open.has(v.id) ? 'Hide' : 'Show'} the files of version ${v.id.slice(0, 8)}`}
                className="inline-flex items-center gap-1.5 cursor-pointer rounded hover:text-accent-ink">
                <Icon name="chevron-right" size={13} className={`text-faint transition-transform ${open.has(v.id) ? 'rotate-90 text-accent-ink' : ''}`} />
                {v.id.slice(0, 8)}
              </button>
              {v.id === head && <Chip tone="ok">Latest</Chip>}
            </span>
          ) },
          { head: 'When', kind: 'wrap', cell: v => (
            <span className="flex flex-col items-start gap-1">
              <span title={v.createdAt} className="whitespace-nowrap">{ago(v.createdAt)} · {v.machineName}</span>
              {names[v.id] && <span className="inline-flex items-center gap-1 rounded-full border border-info-line bg-info-soft text-info-ink px-2 py-0.5 text-[11px] font-medium whitespace-nowrap"><Icon name="pencil" size={10} />{names[v.id]}</span>}
            </span>
          ) },
          { head: 'Size', kind: 'n', cell: v => fmtSize(v.size) },
          { head: '', label: 'Actions', end: true, cell: v => (
            <span className="inline-flex gap-1.5 justify-end flex-nowrap">
              <Button size="sm" onClick={() => { setText(names[v.id] ?? ''); setNaming(v) }}>{names[v.id] ? 'Rename' : 'Name…'}</Button>
              {v.id !== head && (
                <InlineConfirm label="Set as Latest" tone="primary"
                  consequence={`${p.name}’s latest becomes this save from ${ago(v.createdAt)}. ${on.length ? `${on.join(', ')} ${on.length > 1 ? 'pull' : 'pulls'} it.` : 'No device plays this playthrough, so nothing is pulled yet.'} Other playthroughs aren’t touched.`}
                  confirmLabel={`Make ${v.id.slice(0, 8)} Latest`}
                  onConfirm={() => { setLatest({ ...latest, [p.id]: v.id }); toast(`${v.id.slice(0, 8)} is ${p.name}’s latest now.`) }} />
              )}
              <Button size="sm" variant="quiet" onClick={() => toast(`Downloaded ${p.name} · ${v.id.slice(0, 8)}.zip`)}>Download</Button>
            </span>
          ) },
        ]} />
      {naming && (
        <Dialog title={names[naming.id] ? 'Rename this save' : 'Name this save'} sub={`${p.name} · ${naming.machineName} · ${ago(naming.createdAt)}`} onClose={() => setNaming(null)}
          actions={<><Button variant="quiet" onClick={() => setNaming(null)}>Cancel</Button>
            <Button variant="primary" disabled={!text.trim()} onClick={() => { setNames({ ...names, [naming.id]: text.trim() }); setNaming(null); toast('Named. Pruning never removes this version.') }}>Save</Button></>}>
          <input autoFocus className={fieldCls} value={text} maxLength={80} placeholder="e.g. Before Malenia" onChange={e => setText(e.target.value)} aria-label="Name" />
          <p className="rounded-lg border border-safe-line bg-safe-soft text-safe-ink px-3 py-2 text-[12px]">A named save is never removed when old versions are pruned.</p>
        </Dialog>
      )}
    </>
  )
}

/** The picked playthrough's head: name, devices, its actions. Shared by D and E. */
function PtHead({ p, onAsk }: { p: Pt; onAsk: (a: Ask) => void }) {
  const def = useMock(s => s.def)
  return (
    <div className="flex items-start justify-between gap-3 flex-wrap px-4 py-3 border-b border-line">
      <div className="min-w-0 flex flex-col gap-1.5">
        <div className="text-[15px] font-semibold text-fg flex items-center gap-1.5 flex-wrap">{p.name}{p.id === def && <Chip>Default for new devices</Chip>}{p.archived && <Chip>Archived</Chip>}</div>
        <div className="text-xs text-dim">{p.versions} versions · {fmtSize(p.size)} · latest {ago(p.when)} on {p.by}</div>
        <span className="flex items-center gap-1.5 flex-wrap text-[11.5px] text-dim">Played on <DeviceChips names={devicesOn(p.id)} /></span>
      </div>
      <span className="flex gap-1.5 flex-wrap items-center">
        <Button size="sm" variant="primary" onClick={() => onAsk({ kind: 'move', to: p.id })}>Switch a device to this…</Button>
        <RowMenu p={p} isDefault={p.id === def} onAsk={onAsk} />
      </span>
    </div>
  )
}

/**
 * D and E: the game page's Versions card grown into "Saves": playthroughs and their versions. Every game has at least
 * one playthrough, Main; Elden Ring has four, every other game shows its Main with its real versions.
 */
export function PlaythroughVersionsCard({ game, versions, machines }: { game: Game; versions: Version[]; machines: string[] }) {
  const variant = useMock(s => s.variant)
  const pts = useMock(s => s.pts)
  useMock(s => s.plays)
  const [ask, setAsk] = useState<Ask>(null)
  const [pick, setPick] = useState('main')
  const [showArchived, setShowArchived] = useState(false)
  const many = game.id === G.elden
  const one: Pt = { id: 'main', name: 'Main', when: versions[0]?.createdAt ?? new Date().toISOString(), by: versions[0]?.machineName ?? '—', versions: versions.length, size: versions[0]?.size ?? 0 }
  const list = many ? pts : [one]
  const shown = list.filter(q => showArchived || !q.archived)
  const p = list.find(q => q.id === pick) ?? list[0]
  const rows = many ? versionsOf(p, game.id) : versions
  const header = (
    <span className="flex items-center gap-1.5 flex-wrap">
      {many && <label className="flex items-center gap-1.5 text-[11.5px] text-dim cursor-pointer">
        <input type="checkbox" className="accent-[var(--color-accent)]" checked={showArchived} onChange={e => setShowArchived(e.target.checked)} />Show archived
      </label>}
      <Button size="sm" onClick={() => setAsk({ kind: 'new' })}><Icon name="plus" size={13} />New playthrough</Button>
    </span>
  )
  const head = many ? <PtHead p={p} onAsk={setAsk} /> : (
    <div className="flex items-start justify-between gap-3 flex-wrap px-4 py-3 border-b border-line">
      <div className="min-w-0 flex flex-col gap-1.5">
        <div className="text-[15px] font-semibold text-fg flex items-center gap-1.5">Main<Chip>Default for new devices</Chip></div>
        <div className="text-xs text-dim">{versions.length} versions · every game starts with this one playthrough</div>
        <span className="flex items-center gap-1.5 flex-wrap text-[11.5px] text-dim">Played on <DeviceChips names={machines} /></span>
      </div>
      <Button size="sm" variant="quiet" onClick={() => setAsk({ kind: 'rename', id: 'main' })} disabled>Rename</Button>
    </div>
  )
  const body = variant === 'D' ? (
    <div className="grid md:grid-cols-[200px_minmax(0,1fr)]">
      <div className="border-r border-line" role="tablist" aria-label="Playthroughs">
        {shown.map(q => (
          <button key={q.id} type="button" role="tab" aria-selected={q.id === p.id} onClick={() => setPick(q.id)}
            className={`w-full text-left px-4 py-3 border-b border-row cursor-pointer hover:bg-hover flex flex-col gap-1.5 ${q.id === p.id ? 'bg-raise shadow-[inset_2px_0_0_var(--color-accent)]' : ''}`}>
            <span className="text-[13px] font-semibold text-fg flex items-center gap-1.5">{q.name}{q.archived && <Chip>Archived</Chip>}</span>
            <span className="text-[11.5px] text-dim">{q.versions} versions · latest {ago(q.when)}</span>
            <DeviceChips names={many ? devicesOn(q.id) : machines} empty="No device" />
          </button>
        ))}
      </div>
      <div className="min-w-0">{head}<PtVersionsTable key={p.id} p={p} game={game} rows={rows} /></div>
    </div>
  ) : (
    <div>
      <div className="flex gap-1 px-3 pt-2 border-b border-line overflow-x-auto" role="tablist" aria-label="Playthroughs">
        {shown.map(q => (
          <button key={q.id} type="button" role="tab" aria-selected={q.id === p.id} onClick={() => setPick(q.id)}
            className={`flex flex-col items-start gap-0.5 px-3.5 pt-2 pb-2.5 -mb-px border-b-2 rounded-t-lg cursor-pointer whitespace-nowrap ${q.id === p.id ? 'border-accent text-fg' : 'border-transparent text-dim hover:text-fg hover:bg-hover'}`}>
            <span className="text-[13px] font-semibold">{q.name}</span>
            <span className="text-[11px] text-faint">{(many ? devicesOn(q.id) : machines).join(' · ') || 'No device'}</span>
          </button>
        ))}
      </div>
      {head}
      <PtVersionsTable key={p.id} p={p} game={game} rows={rows} />
    </div>
  )
  return (
    <>
      <Card title="Saves" headerRight={header} flush>{body}</Card>
      {ask?.kind === 'move' && <MoveDialog device={ask.device} to={ask.to} onClose={() => setAsk(null)} />}
      {ask?.kind === 'rename' && <RenameDialog id={ask.id} onClose={() => setAsk(null)} />}
      {ask?.kind === 'delete' && <DeleteDialog id={ask.id} onClose={() => setAsk(null)} />}
      {ask?.kind === 'new' && <NewDialog onClose={() => setAsk(null)} />}
    </>
  )
}
