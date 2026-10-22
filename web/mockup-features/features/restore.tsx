import { createContext, useContext, useState, type ReactNode } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Chip } from '../../src/components/ui/Chip'
import { when } from '../../src/format'
import { toast } from '../../src/toast'
import type { Version } from '../../src/types'
import { M, MACHINE_NAMES, endFlow, getState, runFlow, useMock, type Variant } from '../store'
import { Dialog, Steps } from './common'

interface Ctx { variant: Variant; version: Version; isLatest: boolean; picked: Set<string>; toggle: (p: string) => void; ask: (paths: string[]) => void }
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
            <Button size="sm" variant="primary" onClick={() => setAsked([...picked])}>Restore {picked.size} file{picked.size === 1 ? '' : 's'}…</Button></span>
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

function RestoreDialog({ paths, version, onClose }: { paths: string[]; version: Version; onClose: () => void }) {
  const flow = useMock(s => s.flow)
  const close = () => { endFlow(); onClose() }
  const what = paths.length > 1 ? `${paths.length} files` : paths[0].split('/').pop()
  const devices = [MACHINE_NAMES[M.pc], MACHINE_NAMES[M.deck]]
  const running = getState().sim === 'running'
  const send = () => runFlow([
    { t: 'Take the latest save', d: `${MACHINE_NAMES[M.deck]} · 9 minutes ago` },
    { t: `Put ${what} from ${when(version.createdAt)} in it`, d: 'Each file checked against its SHA-256' },
    { t: 'Save it as a new version and make it Latest', d: `“Restored ${what} from ${when(version.createdAt)}”` },
    { t: `Tell ${devices.join(' and ')} to pull it`, d: running ? `${MACHINE_NAMES[M.deck]} has Hollow Knight open, so it pulls when the game closes.` : 'Each pulls it the way it pulls any new save' },
  ], { done: ok => ok && toast(`Restored as a new save. The previous latest is still in history.`) })
  return (
    <Dialog title={flow ? (flow.state === 'ok' ? 'Restored as a new save' : 'Restoring…') : `Restore ${what}`}
      sub={flow ? undefined : `From ${version.machineName}’s version of ${when(version.createdAt)}.`} onClose={close}
      actions={!flow ? <><Button variant="quiet" onClick={() => { toast('Downloaded.'); close() }}>Download instead</Button><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" onClick={send}>Restore and create save</Button></>
        : flow.state === 'run' ? <Button disabled>Restoring…</Button> : <Button variant="primary" onClick={close}>Done</Button>}>
      {!flow ? (
        <>
          <code className="block font-mono text-[11.5px] bg-raise border border-line rounded-lg px-2.5 py-2 whitespace-pre-wrap">{paths.join('\n')}</code>
          <p>SaveLocker makes a new save: the latest one, with {paths.length > 1 ? 'these files' : 'this file'} taken from this version. It becomes Latest, and {devices.join(' and ')}, which sync Hollow Knight, pull it. The previous latest stays in history, so Set as Latest undoes this.</p>
          {running && <p className="rounded-lg border border-watch-line bg-watch-soft text-watch-ink px-3 py-2 text-[12px]">Hollow Knight is open on {MACHINE_NAMES[M.deck]}. It gets the restored save when the game closes.</p>}
        </>
      ) : <Steps />}
    </Dialog>
  )
}
