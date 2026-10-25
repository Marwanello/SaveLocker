import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Chip } from '../../src/components/ui/Chip'
import { Icon } from '../../src/components/ui/Icon'
import { when } from '../../src/format'
import { toast } from '../../src/toast'
import type { Version } from '../../src/types'
import { M, MACHINE_NAMES, endFlow, getState, runFlow, useMock, useVariant, type Variant } from '../store'
import { Dialog, Steps } from './common'
import { G, filesOf } from '../fixtures'

interface Ctx { variant: Variant; version: Version; isLatest: boolean; picked: Set<string>; toggle: (paths: string[], on: boolean) => void; ask: (paths: string[]) => void }
const RestoreCtx = createContext<Ctx | null>(null)

/* How the opened version compares with the latest save, for the mock (Hollow Knight). */
const DIFFERS = new Set(['user1.dat', 'Steel Soul/user3.dat'])
// Not in the latest save at all: the latest no longer has the Old saves/ folder.
const NOT_IN_LATEST = new Set(['Old saves/user2.dat', 'Old saves/Pantheon/user4.dat'])
const LATEST_DIRS = new Set(['Steel Soul/', 'Steel Soul/Screens/'])
// In the latest save but not in the opened version, inside a folder the version has.
const ONLY_IN_LATEST = ['Steel Soul/user3.dat.bak2', 'Steel Soul/Screens/shot1.png']

const parents = (p: string) => p.split('/').slice(0, -1).map((_, i, a) => a.slice(0, i + 1).join('/') + '/')

/** Wraps an opened version's file tree with the planned file restore, in the variant on show. */
export function RestoreProvider({ version, isLatest, children }: { version: Version; isLatest: boolean; children: ReactNode }) {
  const variant = useVariant('vh-restore')!
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [asked, setAsked] = useState<string[] | null>(null)
  const toggle = (paths: string[], on: boolean) => setPicked(s => { const n = new Set(s); for (const p of paths) { if (on) n.add(p); else n.delete(p) } return n })
  return (
    <RestoreCtx.Provider value={{ variant, version, isLatest, picked, toggle, ask: setAsked }}>
      {variant === 'C' && <p className="text-[12px] text-dim">{isLatest ? 'This is the latest version.' : 'Compared with the latest version. Files that differ can be restored.'}</p>}
      {variant === 'B' && <p className="text-[12px] text-dim">Tick files, or tick a folder to take everything in it, its sub-folders included.</p>}
      {children}
      {variant === 'B' && picked.size > 0 && (
        <div className="flex items-center justify-between gap-2 flex-wrap rounded-xl border border-line bg-raise px-3.5 py-2 text-[12px] text-dim">
          <span>{picked.size} file{picked.size === 1 ? '' : 's'} picked</span>
          <span className="flex gap-1.5"><Button size="sm" variant="quiet" onClick={() => setPicked(new Set())}>Clear</Button>
            <Button size="sm" variant="primary" onClick={() => setAsked([...picked])}>Restore {picked.size} file{picked.size === 1 ? '' : 's'}…</Button></span>
        </div>
      )}
      {asked && <RestoreDialog paths={asked} version={version} onClose={() => { setAsked(null); setPicked(new Set()) }} />}
    </RestoreCtx.Provider>
  )
}

/** Before a folder's icon in B: ticks every file under it; half-ticked when only some are. */
export function DirLead({ name, paths }: { name: string; paths: string[] }) {
  const c = useContext(RestoreCtx)
  const ref = useRef<HTMLInputElement>(null)
  const n = c ? paths.filter(p => c.picked.has(p)).length : 0
  useEffect(() => { if (ref.current) ref.current.indeterminate = n > 0 && n < paths.length }, [n, paths.length])
  if (!c || c.variant !== 'B') return null
  return <input ref={ref} type="checkbox" aria-label={`Pick everything in ${name}`} className="accent-[var(--color-accent)]" checked={n === paths.length} onChange={() => c.toggle(paths, n !== paths.length)} />
}

/** Before a file's icon: B's checkbox. */
export function FileLead({ path }: { path: string }) {
  const c = useContext(RestoreCtx)
  if (!c || c.variant !== 'B') return null
  return <input type="checkbox" aria-label={`Pick ${path}`} className="accent-[var(--color-accent)]" checked={c.picked.has(path)} onChange={e => c.toggle([path], e.target.checked)} />
}

/** After a file's size: A's buttons, or C's comparison and restore. */
export function FileActions({ path, index }: { path: string; index: number }) {
  const c = useContext(RestoreCtx)
  if (!c) return null
  if (c.variant === 'A') return (
    <span className="flex gap-1">
      <Button size="sm" onClick={() => c.ask([path])}>Restore…</Button>
      <Button size="sm" variant="quiet" onClick={() => toast(`Downloaded ${path.split('/').pop()}.`)}>Download</Button>
    </span>
  )
  if (c.variant === 'C') {
    const differs = !c.isLatest && index === 0
    return differs
      ? <span className="flex gap-1 items-center"><Chip tone="warn">Differs</Chip><Button size="sm" onClick={() => c.ask([path])}>Restore…</Button></span>
      : <Chip>{c.isLatest ? 'Latest' : 'Same as latest'}</Chip>
  }
  return null
}

/** What restoring `paths` does to the latest save, file by file and folder by folder. */
function plan(paths: string[]) {
  const replace = paths.filter(p => DIFFERS.has(p))
  const add = paths.filter(p => NOT_IN_LATEST.has(p))
  const same = paths.filter(p => !DIFFERS.has(p) && !NOT_IN_LATEST.has(p))
  const newDirs = [...new Set(add.flatMap(parents))].filter(d => !LATEST_DIRS.has(d))
  // A folder counts as picked when every file the version has in it is; only there are the latest's extra files asked about.
  const all = filesOf(G.hollow).map(f => f[0])
  const whole = (d: string) => all.some(f => f.startsWith(d)) && all.every(f => !f.startsWith(d) || paths.includes(f))
  const extra = ONLY_IN_LATEST.filter(p => parents(p).some(whole))
  return { replace, add, same, newDirs, extra }
}

function PlanRow({ icon, path, chip, tone }: { icon: 'file' | 'folder'; path: string; chip: string; tone: 'ok' | 'warn' | 'default' }) {
  return (
    <div className="flex items-center gap-2 px-3 py-1.5 border-b border-row last:border-b-0">
      <Icon name={icon} size={13} className="text-faint shrink-0" />
      <span className="flex-1 min-w-0 font-mono text-[11.5px] text-fg [overflow-wrap:anywhere]">{path}</span>
      <Chip tone={tone}>{chip}</Chip>
    </div>
  )
}

function RestoreDialog({ paths, version, onClose }: { paths: string[]; version: Version; onClose: () => void }) {
  const flow = useMock(s => s.flow)
  const [mirror, setMirror] = useState(false)
  const close = () => { endFlow(); onClose() }
  const what = paths.length > 1 ? `${paths.length} files` : paths[0].split('/').pop()
  const devices = [MACHINE_NAMES[M.pc], MACHINE_NAMES[M.deck]]
  const running = getState().sim === 'running'
  const p = plan(paths)
  const send = () => runFlow([
    { t: 'Take the latest save', d: `${MACHINE_NAMES[M.deck]} · 9 minutes ago` },
    { t: `Put ${what} from ${when(version.createdAt)} in it, at the same paths`, d: `${p.replace.length} replaced, ${p.add.length} added${p.newDirs.length ? `, ${p.newDirs.length} folder${p.newDirs.length > 1 ? 's' : ''} created` : ''}, ${p.same.length} already the same · each checked against its SHA-256` },
    ...(mirror && p.extra.length ? [{ t: `Leave out ${p.extra.length} file${p.extra.length > 1 ? 's' : ''} the old version didn’t have`, d: p.extra.join(', ') }] : []),
    { t: 'Save it as a new version and make it Latest', d: `“Restored ${what} from ${when(version.createdAt)}”` },
    { t: `Tell ${devices.join(' and ')} to pull it`, d: running ? `${MACHINE_NAMES[M.deck]} has Hollow Knight open, so it pulls when the game closes.` : 'Each pulls it the way it pulls any new save, creating folders it doesn’t have' },
  ], { done: ok => ok && toast('Restored as a new save. The previous latest is still in history.') })
  return (
    <Dialog wide title={flow ? (flow.state === 'ok' ? 'Restored as a new save' : 'Restoring…') : `Restore ${what}`}
      sub={flow ? undefined : `From ${version.machineName}’s version of ${when(version.createdAt)}.`} onClose={close}
      actions={!flow ? <><Button variant="quiet" onClick={() => { toast('Downloaded.'); close() }}>Download instead</Button><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" onClick={send}>Restore and create save</Button></>
        : flow.state === 'run' ? <Button disabled>Restoring…</Button> : <Button variant="primary" onClick={close}>Done</Button>}>
      {!flow ? (
        <>
          <p>SaveLocker makes a new save: the latest one, with {paths.length > 1 ? 'these files' : 'this file'} taken from this version at the same paths inside the save folder. It becomes Latest, and {devices.join(' and ')}, which sync Hollow Knight, pull it. The previous latest stays in history, so Set as Latest undoes this.</p>
          <div>
            <div className="text-[10px] tracking-[0.12em] uppercase text-faint mb-1.5">What changes in the new save</div>
            <div className="border border-line rounded-xl overflow-hidden max-h-[260px] overflow-y-auto">
              {p.newDirs.map(d => <PlanRow key={d} icon="folder" path={d} chip="Folder created" tone="ok" />)}
              {p.replace.map(f => <PlanRow key={f} icon="file" path={f} chip="Replaced" tone="warn" />)}
              {p.add.map(f => <PlanRow key={f} icon="file" path={f} chip="Added" tone="ok" />)}
              {p.same.map(f => <PlanRow key={f} icon="file" path={f} chip="Already the same" tone="default" />)}
              {p.extra.map(f => <PlanRow key={f} icon="file" path={f} chip={mirror ? 'Left out' : 'Kept, not in that version'} tone={mirror ? 'warn' : 'default'} />)}
            </div>
          </div>
          {p.extra.length > 0 && (
            <label className="flex items-start gap-2 text-[12.5px] cursor-pointer">
              <input type="checkbox" className="mt-0.5 accent-[var(--color-accent)]" checked={mirror} onChange={e => setMirror(e.target.checked)} />
              <span><span className="text-fg">Make the picked folders exactly as they were</span><span className="block text-xs">The latest has {p.extra.length} file{p.extra.length > 1 ? 's' : ''} in them that this version didn’t. Off: they stay. On: the new save leaves them out (the previous latest still has them).</span></span>
            </label>
          )}
          <p className="text-xs">Paths are relative to the save folder, so a folder restores the same on every device, whatever its save folder is. Files outside the picked ones are never touched.</p>
          {running && <p className="rounded-lg border border-watch-line bg-watch-soft text-watch-ink px-3 py-2 text-[12px]">Hollow Knight is open on {MACHINE_NAMES[M.deck]}. It gets the restored save when the game closes.</p>}
        </>
      ) : <Steps />}
    </Dialog>
  )
}
