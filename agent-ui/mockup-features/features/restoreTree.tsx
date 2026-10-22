import { useEffect, useRef, useState } from 'react'
import { ChevronRight, File, Folder, FolderPlus } from 'lucide-react'
import { Button } from '../../src/components/ui/Button'
import { Chip } from '../../src/components/ui/Chip'
import { formatBytes, formatDateTime } from '../../src/format'
import { getState, runFlow, useMock, type DemoVersion } from '../store'
import { Dialog, Steps, closeFlow } from './common'

/* Baldur's Gate 3's save folder in the opened version, and how this device's folder differs from it. */
const FILES: { path: string; size: number; here: 'same' | 'differs' | 'missing' }[] = [
  { path: 'profile8.lsf', size: 2_048, here: 'same' },
  { path: 'Story/HonourMode__Slot1/HonourMode.lsv', size: 31_457_280, here: 'differs' },
  { path: 'Story/HonourMode__Slot1/HonourMode.WebP', size: 184_320, here: 'differs' },
  { path: 'Story/QuickSave_12/QuickSave_12.lsv', size: 19_922_944, here: 'same' },
  { path: 'Story/QuickSave_12/QuickSave_12.WebP', size: 160_000, here: 'same' },
  { path: 'Story/QuickSave_9/QuickSave_9.lsv', size: 19_800_000, here: 'missing' },
  { path: 'Story/QuickSave_9/QuickSave_9.WebP', size: 150_000, here: 'missing' },
]
const HERE_DIRS = new Set(['Story/', 'Story/HonourMode__Slot1/', 'Story/QuickSave_12/', 'Story/Autosave_44/'])
// On this device but not in the opened version, inside folders the version has.
const ONLY_HERE = ['Story/HonourMode__Slot1/HonourMode_backup.lsv', 'Story/Autosave_44/Autosave_44.lsv']

const parents = (p: string) => p.split('/').slice(0, -1).map((_, i, a) => a.slice(0, i + 1).join('/') + '/')

interface Dir { dirs: Map<string, Dir>; files: typeof FILES }
function nest(): Dir {
  const root: Dir = { dirs: new Map(), files: [] }
  for (const f of FILES) {
    let node = root
    for (const part of f.path.split('/').slice(0, -1)) {
      let next = node.dirs.get(part)
      if (!next) node.dirs.set(part, next = { dirs: new Map(), files: [] })
      node = next
    }
    node.files.push(f)
  }
  return root
}
const under = (d: Dir): string[] => [...d.files.map(f => f.path), ...[...d.dirs.values()].flatMap(under)]

function DirBox({ paths, picked, onToggle, label }: { paths: string[]; picked: Set<string>; onToggle: (paths: string[], on: boolean) => void; label: string }) {
  const ref = useRef<HTMLInputElement>(null)
  const n = paths.filter(p => picked.has(p)).length
  useEffect(() => { if (ref.current) ref.current.indeterminate = n > 0 && n < paths.length }, [n, paths.length])
  return <input ref={ref} type="checkbox" className="slf-check" aria-label={`Pick everything in ${label}`} checked={n === paths.length} onChange={() => onToggle(paths, n !== paths.length)} />
}

function Level({ dir, prefix, picked, onToggle }: { dir: Dir; prefix: string; picked: Set<string>; onToggle: (paths: string[], on: boolean) => void }) {
  const [closed, setClosed] = useState<Set<string>>(new Set())
  return (
    <ul className={prefix ? 'sl-tree sl-tree--sub' : 'sl-tree'}>
      {[...dir.dirs.entries()].map(([name, sub]) => {
        const k = prefix + name + '/'
        const open = !closed.has(k)
        return (
          <li key={k}>
            <div className="sl-tree__node sl-tree__node--dir">
              <button type="button" className="sl-tree__twist" aria-expanded={open} aria-label={`${open ? 'Fold' : 'Unfold'} ${name}`}
                onClick={() => setClosed(s => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n })} style={{ border: 0, background: 'none' }}>
                <ChevronRight size={14} className={`sl-tree__chev${open ? ' sl-tree__chev--open' : ''}`} />
              </button>
              <DirBox paths={under(sub)} picked={picked} onToggle={onToggle} label={name} />
              <Folder size={14} className="sl-tree__icon" aria-hidden="true" />
              <span className="sl-tree__name">{name}/</span>
              {!HERE_DIRS.has(k) && <span className="sl-tree__state">Not on this device</span>}
            </div>
            {open && <Level dir={sub} prefix={k} picked={picked} onToggle={onToggle} />}
          </li>
        )
      })}
      {dir.files.map(f => (
        <li key={f.path}>
          <div className="sl-tree__node">
            <span className="sl-tree__twist" />
            <input type="checkbox" className="slf-check" aria-label={`Pick ${f.path}`} checked={picked.has(f.path)} onChange={e => onToggle([f.path], e.target.checked)} />
            <File size={14} className="sl-tree__icon" aria-hidden="true" />
            <span className="sl-tree__name sl-mono">{f.path.split('/').pop()}</span>
            <span className="sl-tree__state">{f.here === 'same' ? 'Same here' : f.here === 'differs' ? 'Differs here' : 'Not on this device'}</span>
            <span className="sl-tree__size">{formatBytes(f.size)}</span>
          </div>
        </li>
      ))}
    </ul>
  )
}

function PlanRow({ folder, path, chip, tone }: { folder?: boolean; path: string; chip: string; tone?: 'ok' | 'warn' }) {
  return (
    <div className="sl-tree__node" style={{ borderBottom: '1px solid var(--color-line)', borderRadius: 0 }}>
      {folder ? <FolderPlus size={14} className="sl-tree__icon" /> : <File size={14} className="sl-tree__icon" />}
      <span className="sl-tree__name sl-mono">{path}</span>
      <Chip tone={tone}>{chip}</Chip>
    </div>
  )
}

/** B · tick files or whole folders in an opened version; restoring shows what happens to each file and folder here. */
export function PickTree({ version }: { version: DemoVersion }) {
  const flow = useMock(s => s.flow)
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [ask, setAsk] = useState(false)
  const [mirror, setMirror] = useState(false)
  const [undone, setUndone] = useState(false)
  const toggle = (paths: string[], on: boolean) => setPicked(s => { const n = new Set(s); for (const p of paths) { if (on) n.add(p); else n.delete(p) } return n })
  const list = FILES.filter(f => picked.has(f.path))
  const replace = list.filter(f => f.here === 'differs').map(f => f.path)
  const add = list.filter(f => f.here === 'missing').map(f => f.path)
  const same = list.filter(f => f.here === 'same').map(f => f.path)
  const newDirs = [...new Set(add.flatMap(parents))].filter(d => !HERE_DIRS.has(d))
  // A folder counts as ticked when every file the version has in it is; only there are this device's extra files asked about.
  const whole = (d: string) => FILES.every(f => !f.path.startsWith(d) || picked.has(f.path))
  const extra = ONLY_HERE.filter(p => parents(p).some(d => FILES.some(f => f.path.startsWith(d)) && whole(d)))
  const close = () => { closeFlow(); setAsk(false); setPicked(new Set()); setMirror(false); setUndone(false) }
  const start = () => runFlow([
    { t: 'Check that Baldur’s Gate 3 isn’t running' },
    { t: `Download ${list.length} file${list.length > 1 ? 's' : ''} from that version`, d: 'Each checked against its SHA-256' },
    { t: 'Keep a copy of every file it changes, so you can undo' },
    ...(newDirs.length ? [{ t: `Create ${newDirs.length} folder${newDirs.length > 1 ? 's' : ''}`, d: newDirs.join(', ') }] : []),
    { t: `Put them in the save folder at the same paths`, d: `${replace.length} replaced, ${add.length} added, ${same.length} already the same` },
    ...(mirror && extra.length ? [{ t: `Move ${extra.length} file${extra.length > 1 ? 's' : ''} that version didn’t have into the undo copy` }] : []),
  ], { failAt: getState().sim === 'running' ? 0 : -1, failMsg: 'Baldur’s Gate 3 is running. Close it, then restore. Nothing changed.' })
  return (
    <>
      <p className="slf-dim" style={{ fontSize: 11.5, margin: '6px 6px 4px' }}>Tick files, or tick a folder to take everything in it, its sub-folders included.</p>
      <Level dir={nest()} prefix="" picked={picked} onToggle={toggle} />
      {list.length > 0 && (
        <div className="slf-bar" style={{ margin: '8px -17px -12px' }}><span>{list.length} picked</span>
          <span className="slf-inline"><Button size="sm" variant="quiet" onClick={() => setPicked(new Set())}>Clear</Button>
            <Button size="sm" variant="primary" onClick={() => setAsk(true)}>Restore {list.length} file{list.length > 1 ? 's' : ''}</Button></span></div>
      )}
      {ask && (
        <Dialog
          title={flow ? (flow.state === 'ok' ? (undone ? 'Undone' : 'Restored') : flow.state === 'fail' ? 'Couldn’t restore' : 'Restoring…') : `Restore ${list.length} file${list.length > 1 ? 's' : ''}?`}
          onClose={close}
          actions={!flow
            ? <><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" onClick={start}>Restore</Button></>
            : flow.state === 'run' ? <Button disabled>Restoring…</Button>
              : <>{flow.state === 'ok' && !undone && <Button onClick={() => setUndone(true)}>Undo</Button>}<Button variant="primary" onClick={close}>Done</Button></>}
        >
          {!flow ? (
            <>
              <p>From <b>{formatDateTime(version.createdAt)}</b> ({version.machineName}), into this device’s save folder at the same paths. Files you didn’t tick aren’t touched.</p>
              <div className="sl-ask__label">What happens here</div>
              <div style={{ border: '1px solid var(--color-line)', borderRadius: 10, overflow: 'hidden', maxHeight: 240, overflowY: 'auto' }}>
                {newDirs.map(d => <PlanRow key={d} folder path={d} chip="Folder created" tone="ok" />)}
                {replace.map(p => <PlanRow key={p} path={p} chip="Replaced" tone="warn" />)}
                {add.map(p => <PlanRow key={p} path={p} chip="Added" tone="ok" />)}
                {same.map(p => <PlanRow key={p} path={p} chip="Already the same" />)}
                {extra.map(p => <PlanRow key={p} path={p} chip={mirror ? 'Moved to the undo copy' : 'Left as it is'} tone={mirror ? 'warn' : undefined} />)}
              </div>
              {extra.length > 0 && (
                <div className="sl-setting">
                  <div className="sl-setting__label">Make the ticked folders exactly as they were<small>This device has {extra.length} file{extra.length > 1 ? 's' : ''} in them that the version didn’t. Off: they stay. On: they move into the undo copy.</small></div>
                  <button type="button" className="sl-switch" role="switch" aria-checked={mirror} aria-label="Make the folders exactly as they were" onClick={() => setMirror(!mirror)} />
                </div>
              )}
              <p>Everything it changes is kept first, so Undo puts it all back. Your save will then differ from the latest version; the next sync uploads it as a new version.</p>
            </>
          ) : <Steps />}
          {undone && <p>The folder is back as it was before the restore.</p>}
        </Dialog>
      )}
    </>
  )
}
