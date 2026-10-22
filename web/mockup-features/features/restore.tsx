import { createContext, useContext, useState, type ReactNode } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Chip } from '../../src/components/ui/Chip'
import { Select } from '../../src/components/ui/Select'
import { when } from '../../src/format'
import { toast } from '../../src/toast'
import type { Version } from '../../src/types'
import { M, MACHINE_NAMES, endFlow, getState, runFlow, useMock } from '../store'
import { Dialog, Steps } from './common'

interface Ctx { variant: 'A' | 'B' | 'C'; version: Version; isLatest: boolean; picked: Set<string>; toggle: (p: string) => void; ask: (paths: string[]) => void }
const RestoreCtx = createContext<Ctx | null>(null)

/** Wraps an opened version's file tree with the planned single-file restore, in the variant on show. */
export function RestoreProvider({ version, isLatest, children }: { version: Version; isLatest: boolean; children: ReactNode }) {
  const variant = useMock(s => s.variant)
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [asked, setAsked] = useState<string[] | null>(null)
  const toggle = (p: string) => setPicked(s => { const n = new Set(s); if (n.has(p)) n.delete(p); else n.add(p); return n })
  return (
    <RestoreCtx.Provider value={{ variant, version, isLatest, picked, toggle, ask: setAsked }}>
      {variant === 'C' && <p className="text-[12px] text-dim">{isLatest ? 'This is the latest version.' : 'Compared with the latest version. Files that differ can be put back on a device.'}</p>}
      {children}
      {variant === 'B' && picked.size > 0 && (
        <div className="flex items-center justify-between gap-2 flex-wrap rounded-xl border border-line bg-raise px-3.5 py-2 text-[12px] text-dim">
          <span>{picked.size} file{picked.size === 1 ? '' : 's'} picked</span>
          <span className="flex gap-1.5"><Button size="sm" variant="quiet" onClick={() => setPicked(new Set())}>Clear</Button>
            <Button size="sm" variant="primary" onClick={() => setAsked([...picked])}>Restore {picked.size} to a device…</Button></span>
        </div>
      )}
      {asked && <RestoreDialog paths={asked} version={version} onClose={() => { setAsked(null); setPicked(new Set()) }} />}
    </RestoreCtx.Provider>
  )
}

/** Before a file's icon: B's checkbox. */
export function FileLead({ path }: { path: string }) {
  const c = useContext(RestoreCtx)
  if (!c || c.variant !== 'B') return null
  return <input type="checkbox" aria-label={`Pick ${path}`} className="accent-[var(--color-accent)]" checked={c.picked.has(path)} onChange={() => c.toggle(path)} />
}

/** After a file's size: A's buttons, or C's comparison and restore. */
export function FileActions({ path, index }: { path: string; index: number }) {
  const c = useContext(RestoreCtx)
  if (!c) return null
  if (c.variant === 'A') return (
    <span className="flex gap-1">
      <Button size="sm" onClick={() => c.ask([path])}>Restore to a device…</Button>
      <Button size="sm" variant="quiet" onClick={() => toast(`Downloaded ${path.split('/').pop()}.`)}>Download</Button>
    </span>
  )
  if (c.variant === 'C') {
    const differs = !c.isLatest && index === 0
    return differs
      ? <span className="flex gap-1 items-center"><Chip tone="warn">Differs</Chip><Button size="sm" onClick={() => c.ask([path])}>Put back on a device…</Button></span>
      : <Chip>{c.isLatest ? 'Latest' : 'Same as latest'}</Chip>
  }
  return null
}

function RestoreDialog({ paths, version, onClose }: { paths: string[]; version: Version; onClose: () => void }) {
  const flow = useMock(s => s.flow)
  const [dev, setDev] = useState(M.deck)
  const name = MACHINE_NAMES[dev]
  const close = () => { endFlow(); onClose() }
  const what = paths.length > 1 ? `${paths.length} files` : paths[0].split('/').pop()
  const send = () => runFlow([
    { t: `Sent to ${name}` },
    { t: `${name} checked Hollow Knight isn’t running` },
    { t: `${name} downloaded ${what} and checked it`, d: 'SHA-256 matches the version' },
    { t: `${name} kept its current file for undo` },
    { t: `${name} replaced ${what} in its save folder` },
  ], { failAt: getState().sim === 'running' ? 1 : -1, failMsg: `Hollow Knight is running on ${name}. Nothing changed. Try again when the game is closed.` })
  return (
    <Dialog title={flow ? (flow.state === 'ok' ? `Restored on ${name}` : flow.state === 'fail' ? `${name} didn’t restore it` : `Restoring on ${name}…`) : `Restore ${what} to a device`}
      sub={flow ? undefined : `From ${version.machineName}’s version of ${when(version.createdAt)}.`} onClose={close}
      actions={!flow ? <><Button variant="quiet" onClick={() => { toast('Downloaded.'); close() }}>Download instead</Button><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" onClick={send}>Send to {name}</Button></>
        : flow.state === 'run' ? <Button disabled>Waiting for {name}…</Button> : <Button variant="primary" onClick={close}>Done</Button>}>
      {!flow ? (
        <>
          <div className="grid grid-cols-[70px_minmax(0,1fr)] gap-3 items-center">
            <span className="text-[12.5px] text-dim">Device</span>
            <Select label="Device" variant="field" value={dev} onChange={setDev}
              options={[M.pc, M.deck].map(id => ({ value: id, label: MACHINE_NAMES[id], sub: 'Syncs Hollow Knight' }))} />
          </div>
          <code className="block font-mono text-[11.5px] bg-raise border border-line rounded-lg px-2.5 py-2 whitespace-pre-wrap">{paths.join('\n')}</code>
          <p>The device puts {paths.length > 1 ? 'these files' : 'this file'} in its save folder and keeps its own copy, so it can undo. The latest version doesn’t change; the device’s next sync uploads a new one.</p>
        </>
      ) : <Steps />}
    </Dialog>
  )
}
