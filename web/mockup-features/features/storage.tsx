import { useState, type ReactNode } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { DataTable } from '../../src/components/ui/DataTable'
import { Icon } from '../../src/components/ui/Icon'
import { InlineConfirm } from '../../src/components/ui/InlineConfirm'
import { toast } from '../../src/toast'
import { NAS, endFlow, getState, setState, useMock, type Remote } from '../store'
import { Dialog, fieldCls } from './common'
import { AdvancedFields, GamesTable, MakeRoom, OffsiteSetup, OffsiteStatus, PROVIDERS, ProviderPicker, RunNow, SignInFields, TestBlock, WhatFields } from './offsite'
import { ConvertCard } from './convert'
import { TrashCard } from './history'

/** Converting to save space is on show: its feature, or “Just upgraded” in All chosen. */
export const convertOn = (s: { combined: boolean; sim: string; feature: string }) => (s.combined ? s.sim === 'upgraded' : s.feature === 'st-convert')

/** The configured remotes; none while st-recover shows a server with no remote. */
export function useRemotes() {
  const remotes = useMock(s => s.offsite.remotes)
  const sim = useMock(s => s.feature === 'st-recover' && !s.combined ? s.sim : null)
  return sim === 'none' ? [] : sim === 'two' ? [...remotes, NAS] : remotes
}

/* ------------------------------------------------------------------ space used */

const GAMES: [string, number, number, number][] = [['Cyberpunk 2077', 120, 6.1, 3.1], ['Elden Ring', 96, 2.4, 0.61], ['Animal Crossing', 64, 1.9, 0.42], ['Hollow Knight', 140, 0.6, 0.05], ['Stardew Valley', 56, 0.7, 0.04]]

/** Storage management: what the server's data folder holds, and how much each remote holds. */
export function StorageUse() {
  const trash = useMock(s => s.trash)
  const remotes = useRemotes()
  const conv = useMock(s => s.conv)
  const converted = useMock(convertOn) ? conv / 412 : 0
  const trashGb = trash.reduce((n, t) => n + t.size, 0) / 1e9 + 14.2 * converted
  const versionsGb = 14.2 * (1 - converted) + 4.4 * converted
  const parts: [string, number, string][] = [['Save versions', versionsGb, 'bg-info'], ['Server backups', 6.6, 'bg-safe'], ['Trash', trashGb, 'bg-watch'], ['Database', 0.05, 'bg-faint']]
  const used = parts.reduce((n, p) => n + p[1], 0)
  const DISK = 40
  return (
    <Card title="Space used" headerRight={<span className="font-mono text-[11px] text-faint">/data · {used.toFixed(1)} GB of {DISK} GB disk</span>}>
      <div className="flex flex-col gap-3.5">
        <div className="h-3 rounded-full bg-tile overflow-hidden flex" aria-label="Space on this server">
          {parts.map(([l, gb, cls]) => <i key={l} className={`block h-full ${cls}`} style={{ width: `${(gb / DISK) * 100}%` }} title={`${l} ${gb.toFixed(1)} GB`} />)}
        </div>
        <div className="flex gap-x-4 gap-y-1 flex-wrap text-[11.5px] text-dim">
          {parts.map(([l, gb, cls]) => <span key={l} className="flex items-center gap-1.5"><i className={`w-2 h-2 rounded-full ${cls}`} />{l} {gb < 0.1 ? `${Math.round(gb * 1000)} MB` : `${gb.toFixed(1)} GB`}</span>)}
          <span className="flex items-center gap-1.5"><i className="w-2 h-2 rounded-full bg-tile border border-line" />Free {(DISK - used).toFixed(1)} GB</span>
        </div>
        {remotes.length > 0 && (
          <div className="flex flex-col gap-1.5 pt-3 border-t border-row">
            <span className="text-[10px] tracking-[0.12em] uppercase text-faint">Off-site</span>
            {remotes.map(r => (
              <div key={r.id} className="grid grid-cols-[150px_minmax(0,1fr)_auto] items-center gap-3 text-[12.5px]">
                <span className="text-fg font-semibold truncate">{r.provider}</span>
                <span className="h-1.5 rounded-full bg-tile overflow-hidden"><i className="block h-full bg-info" style={{ width: `${(r.used / (r.cap || r.total || 1)) * 100}%` }} /></span>
                <span className="font-mono text-[11.5px] text-dim">{r.used.toFixed(1)} GB of {r.cap ? `${r.cap} GB cap` : 'no cap'}{r.free != null ? ` · ${r.free} GB free` : ''}</span>
              </div>
            ))}
          </div>
        )}
        <details className="group">
          <summary className="text-[12px] text-dim cursor-pointer list-none flex items-center gap-1"><Icon name="chevron-right" size={13} className="transition-transform group-open:rotate-90" />Biggest games</summary>
          <div className="mt-2 border border-line rounded-xl overflow-hidden">
            <DataTable caption="Space by game" rows={GAMES} rowKey={g => g[0]}
              columns={[
                { head: 'Game', kind: 'k', cell: g => g[0] },
                { head: 'Versions', kind: 'n', cell: g => g[1] },
                { head: 'On this server', kind: 'n', cell: g => `${g[2].toFixed(1)} GB` },
                ...(remotes.length ? [{ head: 'Off-site', kind: 'n' as const, cell: (g: typeof GAMES[number]) => `${g[3].toFixed(2)} GB` }] : []),
              ]} />
          </div>
        </details>
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------------ what's on the remote, as a tree */

interface Node { name: string; size?: string; when?: string; kids?: Node[] }
const ver = (name: string, size: string): Node => ({ name, size, when: name.slice(0, 16).replace(' ', ' · ') })
const TREE: Node = { name: 'SaveLocker', kids: [
  { name: 'Elden Ring', kids: [
    { name: 'Main', kids: [{ name: 'latest.zip', size: '2.8 MB', when: '9 min ago' }, { name: 'versions', kids: [
      ver('2026-10-23 10-41 · Steam Deck.zip', '2.8 MB'), ver('2026-10-22 22-10 · Battlestation · Before Malenia.zip', '2.8 MB'), ver('2026-10-20 19-02 · Steam Deck.zip', '2.7 MB'),
    ] }] },
    { name: 'Hard mode run', kids: [{ name: 'latest.zip', size: '2.8 MB', when: '2 h ago' }, { name: 'versions', kids: [ver('2026-10-23 08-15 · Battlestation.zip', '2.8 MB')] }] },
    { name: 'Sara’s run', kids: [{ name: 'latest.zip', size: '2.6 MB', when: '3 days ago' }] },
  ] },
  { name: 'Hollow Knight', kids: [{ name: 'Main', kids: [{ name: 'latest.zip', size: '1.5 MB', when: '1 h ago' }, { name: 'versions', kids: [ver('2026-10-22 21-40 · Steam Deck · Before the Radiance.zip', '1.5 MB')] }] }] },
  { name: 'Cyberpunk 2077', kids: [{ name: 'Main', kids: [{ name: 'latest.zip', size: '19 MB', when: '2 h ago' }] }, { name: 'Nomad run', kids: [{ name: 'latest.zip', size: '18 MB', when: '5 days ago' }] }] },
  { name: '_server', kids: [{ name: 'savelocker-20261023-030000-scheduled.zip', size: '2.2 GB', when: 'Today 03:00' }, { name: 'savelocker-20261022-030000-scheduled.zip', size: '2.2 GB', when: 'Yesterday 03:00' }] },
  { name: 'README.txt', size: '2 KB', when: 'Oct 1' },
  { name: '_index.json', size: '84 KB', when: '18 min ago' },
] }

function TreeRow({ n, depth, path, open, toggle }: { n: Node; depth: number; path: string; open: Set<string>; toggle: (p: string) => void }) {
  const isDir = !!n.kids
  const isOpen = open.has(path)
  return (
    <>
      <div className="flex items-center gap-2 pr-3.5 py-[5px] border-b border-row last:border-b-0 hover:bg-hover text-[12.5px]" style={{ paddingLeft: 12 + depth * 18 }}>
        {isDir
          ? <button type="button" onClick={() => toggle(path)} aria-expanded={isOpen} className="flex items-center gap-1.5 min-w-0 flex-1 text-left cursor-pointer">
              <Icon name="chevron-right" size={12} className={`text-faint transition-transform ${isOpen ? 'rotate-90' : ''}`} />
              <Icon name="folder" size={14} className="text-info-ink flex-none" />
              <span className="text-fg truncate">{n.name}</span>
              <span className="text-[11px] text-faint">{n.kids!.length} item{n.kids!.length === 1 ? '' : 's'}</span>
            </button>
          : <span className="flex items-center gap-1.5 min-w-0 flex-1 pl-[18px]">
              <Icon name="file" size={14} className="text-faint flex-none" />
              <span className="font-mono text-[11.5px] text-fg truncate">{n.name}</span>
            </span>}
        {!isDir && <span className="text-[11px] text-dim whitespace-nowrap">{n.when}</span>}
        {!isDir && <span className="font-mono text-[11px] text-dim w-[64px] text-right">{n.size}</span>}
        {!isDir && <button type="button" onClick={() => toast(`Downloaded ${n.name}.`)} className="text-[11px] text-dim hover:text-fg underline underline-offset-2 cursor-pointer">Download</button>}
      </div>
      {isDir && isOpen && n.kids!.map(k => <TreeRow key={k.name} n={k} depth={depth + 1} path={`${path}/${k.name}`} open={open} toggle={toggle} />)}
    </>
  )
}

export function RemoteTree() {
  const [open, setOpen] = useState<Set<string>>(new Set(['SaveLocker', 'SaveLocker/Elden Ring']))
  const toggle = (p: string) => setOpen(s => { const n = new Set(s); if (n.has(p)) n.delete(p); else n.add(p); return n })
  return (
    <div className="border border-line rounded-xl overflow-hidden max-h-[420px] overflow-y-auto">
      <TreeRow n={TREE} depth={0} path="SaveLocker" open={open} toggle={toggle} />
    </div>
  )
}

/* ------------------------------------------------------------------ remotes */

/** The setup wizard (st-setup A), for the first remote and for each one added after it. */
export function SetupWizard({ onClose, first }: { onClose: () => void; first: boolean }) {
  const flow = useMock(s => s.flow)
  const [prov, setProv] = useState(first ? 'gdrive' : 'folder')
  const [connected, setConnected] = useState(false)
  const [step, setStep] = useState(0)
  const p = PROVIDERS.find(x => x.id === prov)!
  const STEPS = ['Where', 'Sign in', 'What to copy', 'Advanced', 'Test']
  const can = step === 1 ? (p.kind !== 'oauth' || connected) : true
  const finish = () => {
    const s = getState()
    const where = p.kind === 'oauth' ? 'marwan@gmail.com · SaveLocker' : p.kind === 'folder' ? '/offsite' : p.kind === 's3' ? 'savelocker-offsite' : 'nas.local:/backups/SaveLocker'
    const remote: Remote = { id: `r${Date.now()}`, provider: p.name, where, used: 0, cap: 10, free: p.about ? 11.2 : null, total: p.about ? 15 : null }
    setState({ offsite: { ...s.offsite, on: true, provider: p.name, remotes: [...s.offsite.remotes, remote] } })
    endFlow(); onClose()
    toast(`Off-site copies to ${p.name} are on. The first copy is starting.`)
  }
  return (
    <Dialog wide title={first ? 'Set up off-site copies' : 'Add another remote'} onClose={() => { endFlow(); onClose() }}
      sub={<span className="flex gap-1.5 flex-wrap mt-1">{STEPS.map((s, i) => <Chip key={s} tone={i === step ? 'crit' : i < step ? 'ok' : 'default'}>{i + 1} · {s}</Chip>)}</span>}
      actions={<>{step ? <Button variant="quiet" onClick={() => setStep(step - 1)}>Back</Button> : <Button variant="quiet" onClick={onClose}>Cancel</Button>}
        {step < 4 ? <Button variant="primary" disabled={!can} onClick={() => setStep(step + 1)}>Next</Button> : <Button variant="primary" disabled={flow?.state !== 'ok'} onClick={finish}>{first ? 'Turn on off-site copies' : `Add ${p.name}`}</Button>}</>}>
      {!first && step === 0 && <p className="text-[12.5px]">Each remote gets its own copy of the same data, its own history setting and cap, and its own status. They copy one after the other.</p>}
      {[<ProviderPicker key="0" value={prov} onChange={id => { setProv(id); setConnected(false); endFlow() }} />, <SignInFields key="1" p={p} connected={connected} onConnected={() => setConnected(true)} />, <WhatFields key="2" />, <AdvancedFields key="3" />, <TestBlock key="4" p={p} />][step]}
    </Dialog>
  )
}

/** One configured remote (st-status A): status, space, its settings and its files. */
function RemoteTile({ r, first }: { r: Remote; first: boolean }) {
  const paused = useMock(s => s.offsite.paused) && first
  const [panel, setPanel] = useState<'settings' | 'tree' | null>(null)
  const fresh = r.used === 0
  const cap = r.cap || r.total || 10
  return (
    <>
      <div className="rounded-xl border border-line bg-panel">
        <div className="flex items-start justify-between gap-3 flex-wrap px-4 py-3 border-b border-line">
          <div className="min-w-0">
            <div className="flex items-center gap-2 text-[14px] font-semibold text-fg"><Icon name={r.provider === 'Folder' ? 'folder' : 'library'} size={15} />{r.provider}</div>
            <div className="font-mono text-[11.5px] text-dim mt-0.5 truncate">{r.where}</div>
          </div>
          {first ? <RunNow /> : <span className="flex gap-1.5"><Button size="sm" onClick={() => toast(`Copying to ${r.provider}…`)}><Icon name="refresh-cw" size={13} />Run now</Button></span>}
        </div>
        <div className="flex flex-col gap-3 px-4 py-3">
          <div className="flex items-center gap-2 flex-wrap text-[12.5px] text-dim">
            <Chip tone={paused ? 'warn' : fresh ? 'default' : 'ok'}>{paused ? 'Paused by you' : fresh ? 'First copy running' : 'Up to date'}</Chip>
            {fresh ? 'Copying 6.4 GB · about 25 minutes' : 'Last copy 18 min ago · next in 42 min'}
          </div>
          <div className="flex flex-col gap-1.5">
            <div className="h-2 rounded-full bg-tile overflow-hidden"><i className="block h-full bg-info" style={{ width: `${((fresh ? 0.4 : r.used) / cap) * 100}%` }} /></div>
            <div className="flex gap-3.5 flex-wrap text-[11.5px] text-dim"><span>SaveLocker {fresh ? '0.4' : r.used.toFixed(1)} GB</span><span>{r.cap ? `Cap ${r.cap} GB` : "No cap"}</span>{r.free != null && <span>{r.free} GB free of {r.total} GB</span>}</div>
          </div>
          <div className="flex gap-1.5 flex-wrap">
            <Button size="sm" onClick={() => setPanel('settings')}>Saves and history…</Button>
            <Button size="sm" variant="quiet" onClick={() => setPanel('tree')}><Icon name="folder" size={13} />What’s on the remote</Button>
            <span className="flex-1" />
            <InlineConfirm label="Remove" triggerVariant="quiet" consequence={`SaveLocker stops copying to ${r.provider} and forgets its sign-in. The files already there stay; delete them yourself if you want.`}
              confirmLabel={`Remove ${r.provider}`} onConfirm={() => { const s = getState(); setState({ offsite: { ...s.offsite, remotes: s.offsite.remotes.filter(x => x.id !== r.id) } }); toast(`Removed ${r.provider}. Its files stay where they are.`) }} />
          </div>
        </div>
      </div>
      {panel === 'settings' && <Dialog wide title={`Saves and history · ${r.provider}`} onClose={() => setPanel(null)} actions={<Button variant="primary" onClick={() => setPanel(null)}>Done</Button>}>
        <label className="flex items-center gap-2 text-[12.5px] text-dim">History on the remote <select className={`${fieldCls} w-auto`}><option>Thinning · Short</option><option>Latest only</option><option>Last 5 versions</option><option>Thinning · Balanced</option></select></label>
        <span className="text-xs">Named versions always go; a playthrough’s latest is never removed.</span><GamesTable /></Dialog>}
      {panel === 'tree' && <Dialog wide title={`On ${r.provider}`} sub="Normal zips with readable names. Unzip a latest.zip into the game’s save folder to play it without SaveLocker." onClose={() => setPanel(null)}><RemoteTree /></Dialog>}
    </>
  )
}

function OffsiteCard({ children, intro }: { children: ReactNode; intro?: boolean }) {
  const remotes = useRemotes()
  const [adding, setAdding] = useState(false)
  return (
    <>
      <Card title="Off-site copies" headerRight={remotes.length ? <span className="flex items-center gap-2"><Chip tone="ok">{remotes.length} remote{remotes.length === 1 ? '' : 's'}</Chip><Button size="sm" onClick={() => setAdding(true)}><Icon name="plus" size={13} />Add another remote</Button></span> : <Chip>Off</Chip>}>
        {intro && !remotes.length && <p className="text-[12.5px] text-dim mb-3">Copies the server backups, and every save with its history if you want, to a cloud drive or another machine. If this server’s disk dies, your saves are still there as normal zips you can open.</p>}
        {children}
      </Card>
      {adding && <SetupWizard first={false} onClose={() => setAdding(false)} />}
    </>
  )
}

/** Off-site copies on the Storage page, in the variant on show; once set up (A), a tile per remote. */
export function OffsiteSection() {
  const feature = useMock(s => (s.combined ? 'combined' : s.feature))
  const variant = useMock(s => s.variant)
  const remotes = useRemotes()
  const [wizard, setWizard] = useState(false)
  if (feature === 'st-setup' && variant !== 'A') return <OffsiteSetup />
  if (feature === 'st-status' && variant !== 'A') return <OffsiteStatus />
  if (feature === 'st-full' && variant === 'B') return <OffsiteStatus />
  if (feature === 'st-full' && variant === 'C') return <MakeRoom />
  return (
    <OffsiteCard intro>
      {remotes.length === 0
        ? <Button variant="primary" onClick={() => setWizard(true)}>Set up off-site copies</Button>
        : <div className="flex flex-col gap-3">{remotes.map((r, i) => <RemoteTile key={r.id} r={r} first={i === 0} />)}</div>}
      {wizard && <SetupWizard first onClose={() => setWizard(false)} />}
    </OffsiteCard>
  )
}

/** Everything the Storage page adds after the backups: conversion, space used, off-site copies, then trash last. */
export function StorageSections() {
  const converting = useMock(convertOn)
  const trash = useMock(s => s.combined || !(s.feature === 'vh-trash' && s.variant !== 'A'))
  return (
    <>
      {converting && <ConvertCard />}
      <StorageUse />
      <OffsiteSection />
      {trash && <TrashCard />}
    </>
  )
}
