import { Fragment, useEffect, useRef, useState } from 'react'
import { ArrowLeft, ChevronRight, File, MoreHorizontal, Pencil, RotateCcw, Tag } from 'lucide-react'
import { api } from '../../src/api'
import { formatAgo, formatBytes, formatDateTime } from '../../src/format'
import type { SaveVersion } from '../../src/types'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { DEMO_FILES, DEMO_GAME, DEMO_VERSIONS, getState, ptById, runFlow, setState, useMock, type DemoVersion } from '../store'
import { Dialog, Steps, closeFlow } from '../features/common'

interface Props {
  gameId: string
  headId: string | null
  refreshKey: number
  onLoaded: (versions: SaveVersion[]) => void
}

const SUGGEST = ['Before the final boss', 'Act 3 start', 'Before the Netherbrain', 'Honour mode backup']

/**
 * The real "Versions on the server" card, plus the planned features on the demo game: a name per version (vh-names) and
 * restoring one file from a version (vh-restore). Every other game gets the real card unchanged.
 */
export function VersionsCard({ gameId, headId, refreshKey, onLoaded }: Props) {
  const feature = useMock(s => s.feature)
  const demo = gameId === DEMO_GAME
  const [real, setReal] = useState<SaveVersion[] | null>(null)

  useEffect(() => {
    let live = true
    if (demo) {
      const list = DEMO_VERSIONS.map(v => ({ ...v, gameId, machineId: null, contentHash: v.id, parentVersionId: null, protected: !!v.protected })) as unknown as SaveVersion[]
      onLoaded(list)
      return
    }
    api.gameVersions(gameId).then(v => { if (live) { setReal(v); onLoaded(v) } })
    return () => { live = false }
  }, [gameId, refreshKey, demo])

  if (!demo) return <PlainCard versions={real} headId={headId} />
  if (feature === 'vh-names') return <NamesCard />
  if (feature === 'vh-restore') return <RestoreCard />
  return <DemoPlain />
}

function PlainCard({ versions, headId }: { versions: SaveVersion[] | null; headId: string | null }) {
  return (
    <Card title="Versions on the server" flush headerRight={versions && <Chip>{versions.length} kept</Chip>}>
      {versions === null ? <div className="sl-empty">Loading…</div> : (
        <div className="sl-table-wrap">
          <table className="sl-table">
            <thead><tr><th>When</th><th>From</th><th className="sl-num">Size</th><th /></tr></thead>
            <tbody>
              {versions.slice(0, 10).map(v => (
                <tr key={v.id}>
                  <td title={formatDateTime(v.createdAt)}>{formatAgo(v.createdAt)}</td>
                  <td className="sl-dim">{v.machineName}</td>
                  <td className="sl-num sl-dim">{formatBytes(v.size)}</td>
                  <td>{v.id === headId && <Chip tone="ok">Latest</Chip>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

/** The demo game's card as it is today, naming the playthrough (pt-* features). */
function DemoPlain() {
  const selected = useMock(s => s.selected)
  const pt = ptById(selected)
  return (
    <Card title="Versions on the server" flush headerRight={<Chip>{pt.name} · {pt.versions} kept</Chip>}>
      <div className="sl-table-wrap">
        <table className="sl-table">
          <thead><tr><th>When</th><th>From</th><th className="sl-num">Size</th><th /></tr></thead>
          <tbody>
            {DEMO_VERSIONS.slice(0, 4).map((v, i) => (
              <tr key={v.id}>
                <td>{i === 0 ? formatAgo(pt.when) : formatAgo(v.createdAt)}</td>
                <td className="sl-dim">{i === 0 ? pt.by : v.machineName}</td>
                <td className="sl-num sl-dim">{formatBytes(v.size)}</td>
                <td>{i === 0 && <Chip tone="ok">Latest</Chip>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------------ names */

const labelOf = (v: DemoVersion) => getState().labels[v.id] ?? null
function saveLabel(id: string, text: string | null) {
  setState({ labels: { ...getState().labels, [id]: text && text.trim() ? text.trim().slice(0, 80) : null } })
}
const keptChip = (v: DemoVersion, i: number) =>
  i === 0 ? <Chip tone="ok">Latest</Chip> : labelOf(v) ? <Chip>Kept · named</Chip> : v.protected ? <Chip>Protected</Chip> : null

function NamesCard() {
  const variant = useMock(s => s.variant)
  useMock(s => s.labels)
  const [edit, setEdit] = useState<string | null>(null)
  const [text, setText] = useState('')
  const [dialog, setDialog] = useState<string | null>(null)
  const [menu, setMenu] = useState<string | null>(null)
  const menuRoot = useRef<HTMLSpanElement>(null)
  useEffect(() => {
    if (!menu) return
    const onDown = (e: MouseEvent) => { if (!menuRoot.current?.contains(e.target as Node)) setMenu(null) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [menu])

  const openDialog = (v: DemoVersion) => { setText(labelOf(v) ?? ''); setDialog(v.id) }
  const named = DEMO_VERSIONS.filter(v => labelOf(v))

  return (
    <>
      <Card title="Versions on the server" flush headerRight={<Chip>{DEMO_VERSIONS.length} kept</Chip>}>
        {variant === 'C' && (
          named.length
            ? <div className="slf-named">{named.map(v => (
                <div key={v.id}><b><Tag size={12} aria-hidden="true" style={{ marginRight: 5, verticalAlign: -1 }} />{labelOf(v)}</b><span>{formatAgo(v.createdAt)} · {v.machineName}</span>
                  <span><button type="button" className="sl-btn sl-btn--quiet sl-btn--sm" style={{ padding: '2px 0' }} onClick={() => openDialog(v)}>Rename</button></span></div>
              ))}</div>
            : <div className="sl-empty" style={{ borderBottom: '1px solid var(--color-line)' }}>Name a save from its ••• menu to keep it here.</div>
        )}
        <div className="sl-table-wrap">
          <table className="sl-table">
            <thead><tr><th>When</th><th>From</th><th className="sl-num">Size</th><th /></tr></thead>
            <tbody>
              {DEMO_VERSIONS.map((v, i) => (
                <tr key={v.id}>
                  <td title={formatDateTime(v.createdAt)}>
                    {variant === 'B' && labelOf(v) ? <><b>{labelOf(v)}</b><br /><span className="slf-dim">{formatAgo(v.createdAt)}</span></> : formatAgo(v.createdAt)}
                    {variant === 'A' && edit !== v.id && labelOf(v) && <><br /><span className="sl-tag"><Tag size={10} aria-hidden="true" /> {labelOf(v)}</span></>}
                    {variant === 'A' && edit === v.id && (
                      <div className="slf-inline" style={{ marginTop: 6 }}>
                        <input className="sl-input" style={{ maxWidth: 240 }} value={text} placeholder="Name this save" autoFocus maxLength={80}
                          onChange={e => setText(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') { saveLabel(v.id, text); setEdit(null) } if (e.key === 'Escape') setEdit(null) }} />
                        <Button size="sm" variant="primary" onClick={() => { saveLabel(v.id, text); setEdit(null) }}>Save</Button>
                        <Button size="sm" variant="quiet" onClick={() => setEdit(null)}>Cancel</Button>
                      </div>
                    )}
                    {variant === 'C' && labelOf(v) && <><br /><span className="sl-tag"><Tag size={10} aria-hidden="true" /> {labelOf(v)}</span></>}
                  </td>
                  <td className="sl-dim">{v.machineName}</td>
                  <td className="sl-num sl-dim">{formatBytes(v.size)}</td>
                  <td>
                    <span className="slf-inline" style={{ justifyContent: 'flex-end' }}>
                      {keptChip(v, i)}
                      {variant === 'A' && edit !== v.id && (
                        <Button size="sm" variant="quiet" aria-label={labelOf(v) ? 'Rename' : 'Name this save'} title={labelOf(v) ? 'Rename' : 'Name this save'}
                          onClick={() => { setText(labelOf(v) ?? ''); setEdit(v.id) }}>{labelOf(v) ? <Pencil size={13} /> : <Tag size={13} />}</Button>
                      )}
                      {variant === 'B' && <Button size="sm" onClick={() => openDialog(v)}><Tag size={12} aria-hidden="true" />{labelOf(v) ? 'Rename' : 'Name…'}</Button>}
                      {variant === 'C' && (
                        <span className="slf-pick" ref={menu === v.id ? menuRoot : undefined}>
                          <Button size="sm" variant="quiet" aria-label="More" onClick={() => setMenu(menu === v.id ? null : v.id)}><MoreHorizontal size={14} /></Button>
                          {menu === v.id && (
                            <div className="slf-menu" role="menu" style={{ left: 'auto', right: 0, width: 220 }}>
                              <button type="button" className="slf-menu__item" onClick={() => { setMenu(null); openDialog(v) }}><span className="slf-menu__tick"><Tag size={14} /></span>Name this save…</button>
                              <button type="button" className="slf-menu__item" onClick={() => setMenu(null)}><span className="slf-menu__tick" />Open in the console</button>
                            </div>
                          )}
                        </span>
                      )}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
      {dialog && (
        <Dialog
          title={labelOf(DEMO_VERSIONS.find(v => v.id === dialog)!) ? 'Rename this save' : 'Name this save'} onClose={() => setDialog(null)}
          actions={<>
            {labelOf(DEMO_VERSIONS.find(v => v.id === dialog)!) && <Button variant="quiet" onClick={() => { saveLabel(dialog, null); setDialog(null) }}>Remove name</Button>}
            <Button variant="quiet" onClick={() => setDialog(null)}>Cancel</Button>
            <Button variant="primary" disabled={!text.trim()} onClick={() => { saveLabel(dialog, text); setDialog(null) }}>Save</Button>
          </>}
        >
          <p>{formatDateTime(DEMO_VERSIONS.find(v => v.id === dialog)!.createdAt)} · {DEMO_VERSIONS.find(v => v.id === dialog)!.machineName}</p>
          <div className="sl-field" style={{ marginBottom: 10 }}>
            <label htmlFor="slf-label">Name</label>
            <input id="slf-label" className="sl-input" value={text} maxLength={80} autoFocus placeholder="e.g. Before the final boss" onChange={e => setText(e.target.value)} />
          </div>
          <div className="slf-inline" style={{ marginBottom: 10 }}>
            {SUGGEST.map(s => <button key={s} type="button" className="sl-release__chip" onClick={() => setText(s)}>{s}</button>)}
          </div>
          <p className="sl-ask__warn" style={{ borderColor: 'var(--color-safe-line)', background: 'var(--color-safe-soft)', color: 'var(--color-safe-ink)' }}>
            A named save is never removed when old versions are pruned. Removing the name later leaves Protect as it was.
          </p>
        </Dialog>
      )}
    </>
  )
}

/* ------------------------------------------------------------------ restore one file */

function RestoreCard() {
  const variant = useMock(s => s.variant)
  const flow = useMock(s => s.flow)
  const [open, setOpen] = useState<string | null>('v-3')
  const [picked, setPicked] = useState<Record<string, boolean>>({})
  const [ask, setAsk] = useState<string[] | null>(null)
  const [undone, setUndone] = useState(false)
  const files = DEMO_FILES.filter(f => !f.onlyHere || variant === 'C')
  const pickedList = Object.keys(picked).filter(k => picked[k])

  const start = (list: string[]) => {
    const running = getState().sim === 'running'
    runFlow([
      { t: 'Check that Baldur’s Gate 3 isn’t running' },
      { t: `Download ${list.length > 1 ? `${list.length} files` : list[0]} from that version` },
      { t: 'Check the file matches the version', d: 'SHA-256' },
      { t: 'Keep the current file so you can undo' },
      { t: 'Replace it in the save folder' },
    ], { failAt: running ? 0 : -1, failMsg: 'Baldur’s Gate 3 is running. Close it, then restore. Nothing changed.' })
  }
  const close = () => { closeFlow(); setAsk(null); setPicked({}); setUndone(false) }
  const v = DEMO_VERSIONS.find(x => x.id === open)

  return (
    <>
      <Card title="Versions on the server" flush headerRight={<Chip>{DEMO_VERSIONS.length} kept</Chip>}>
        <div className="sl-table-wrap">
          <table className="sl-table">
            <thead><tr><th>When</th><th>From</th><th className="sl-num">Size</th><th /></tr></thead>
            <tbody>
              {DEMO_VERSIONS.map((d, i) => (
                <Fragment key={d.id}>
                  <tr className={open === d.id ? 'slf-sel' : undefined}>
                    <td>
                      <button type="button" className="sl-tree__twist" aria-expanded={open === d.id} aria-label="Show files"
                        onClick={() => { setOpen(open === d.id ? null : d.id); setPicked({}) }} style={{ border: 0, background: 'none', marginRight: 6 }}>
                        <ChevronRight size={14} className={`sl-tree__chev${open === d.id ? ' sl-tree__chev--open' : ''}`} />
                      </button>
                      {formatAgo(d.createdAt)}
                    </td>
                    <td className="sl-dim">{d.machineName}</td>
                    <td className="sl-num sl-dim">{formatBytes(d.size)}</td>
                    <td>{i === 0 && <Chip tone="ok">Latest</Chip>}</td>
                  </tr>
                  {open === d.id && (
                    <tr><td colSpan={4} style={{ padding: '4px 17px 12px', background: 'var(--color-raise)' }}>
                      {variant === 'C' && (
                        <div className="slf-inline slf-dim" style={{ fontSize: 11.5, padding: '6px 6px 4px', justifyContent: 'space-between' }}>
                          <span>Compared with this device · {files.filter(f => f.differs || f.onlyHere).length} differ</span><span>this device · that version</span>
                        </div>
                      )}
                      <ul className="sl-tree">
                        {files.map(f => {
                          const differs = f.differs || f.onlyHere
                          return (
                            <li key={f.path}>
                              <div className="sl-tree__node" style={variant === 'C' && differs ? { background: 'var(--color-watch-soft)' } : undefined}>
                                {variant === 'B'
                                  ? <input type="checkbox" className="slf-check" aria-label={`Pick ${f.path}`} checked={!!picked[f.path]} onChange={e => setPicked({ ...picked, [f.path]: e.target.checked })} />
                                  : <span className="sl-tree__twist" />}
                                <File size={14} className="sl-tree__icon" aria-hidden="true" />
                                <span className="sl-tree__name sl-mono">{f.path}</span>
                                {variant === 'C' && <span className="sl-tree__state">{f.onlyHere ? 'Only on this device' : f.differs ? 'Differs' : 'Same'}</span>}
                                {variant === 'C' && <span className="sl-tree__size">{formatBytes(f.here)}</span>}
                                {variant === 'C' && (differs && !f.onlyHere
                                  ? <Button size="sm" aria-label={`Restore ${f.path}`} title="Restore this file" onClick={() => { setAsk([f.path]); setUndone(false) }}><ArrowLeft size={13} /></Button>
                                  : <span style={{ width: 34, textAlign: 'center' }} className="slf-dim">{f.onlyHere ? '' : '='}</span>)}
                                <span className="sl-tree__size">{f.onlyHere ? '—' : formatBytes(f.version)}</span>
                                {variant === 'A' && <Button size="sm" onClick={() => { setAsk([f.path]); setUndone(false) }}><RotateCcw size={12} aria-hidden="true" />Restore</Button>}
                              </div>
                            </li>
                          )
                        })}
                      </ul>
                    </td></tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
        {variant === 'B' && pickedList.length > 0 && (
          <div className="slf-bar"><span>{pickedList.length} picked from {v ? formatAgo(v.createdAt) : ''}</span>
            <span className="slf-inline"><Button size="sm" variant="quiet" onClick={() => setPicked({})}>Clear</Button>
              <Button size="sm" variant="primary" onClick={() => { setAsk(pickedList); setUndone(false) }}><RotateCcw size={12} aria-hidden="true" />Restore {pickedList.length} file{pickedList.length > 1 ? 's' : ''}</Button></span></div>
        )}
      </Card>
      {ask && (
        <Dialog
          title={flow ? (flow.state === 'ok' ? (undone ? 'Undone' : 'Restored') : flow.state === 'fail' ? 'Couldn’t restore' : 'Restoring…') : `Restore ${ask.length > 1 ? `${ask.length} files` : ask[0]}?`}
          onClose={close}
          actions={!flow
            ? <><Button variant="quiet" onClick={close}>Cancel</Button><Button variant="primary" onClick={() => start(ask)}>Restore</Button></>
            : flow.state === 'run' ? <Button disabled>Restoring…</Button>
              : <>{flow.state === 'ok' && !undone && <Button onClick={() => setUndone(true)}>Undo</Button>}<Button variant="primary" onClick={close}>Done</Button></>}
        >
          {!flow ? (
            <>
              <p>{ask.length > 1 ? 'These files' : 'This file'} on this device will be replaced with the copy from <b>{v ? formatDateTime(v.createdAt) : ''}</b>{v ? ` (${v.machineName})` : ''}. The current file is kept, so you can undo.</p>
              <code className="sl-ask__path">{ask.join('\n')}</code>
              <p>Your save will then differ from the latest version. The next sync uploads it as a new version.</p>
            </>
          ) : <Steps />}
          {undone && <p>The file is back as it was before the restore.</p>}
        </Dialog>
      )}
    </>
  )
}
