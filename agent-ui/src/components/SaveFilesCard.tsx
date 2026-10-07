import { useCallback, useEffect, useState } from 'react'
import { ChevronRight, File, Folder, RefreshCw } from 'lucide-react'
import { api } from '../api'
import { formatAgo, formatBytes, formatDateTime } from '../format'
import type { GameFiles, GameFolderFiles, LocalFile, OtherFile } from '../types'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

interface Props {
  gameId: string
  /** Bumped by the page after a sync, so the tree shows what that sync just did. */
  refreshKey: number
}

type Look = { dot: string; text: string }

/** A file's state as its dot and the words for it. "Only on the server" is a ring, not a colour: the
 *  palette has no blue, and a ring reads as "not here" without one. */
function look(f: LocalFile): Look {
  switch (f.state) {
    case 'same': return { dot: 'sl-dot sl-dot--ok', text: 'In sync' }
    case 'here': return f.missing
      ? { dot: 'sl-dot sl-dot--warn', text: 'Deleted here · will push' }
      : { dot: 'sl-dot sl-dot--warn', text: 'Changed here · will push' }
    case 'server': return f.missing
      ? { dot: 'sl-dot sl-dot--ring', text: 'Only on the server · will pull' }
      : { dot: 'sl-dot sl-dot--ring', text: 'Changed on the server · will pull' }
    default: return { dot: 'sl-dot', text: 'Not compared' }
  }
}

/**
 * "Save files on this PC" (tasks/save-file-trees Phase 4, variant A): every folder of the game here as a
 * nested tree, each file marked in sync, changed here or only on the server, and the other files in a
 * shared folder folded into one line. The agent hashes every file to answer, so this loads when the
 * page opens, after a sync from it and on Refresh — never on a timer.
 */
export function SaveFilesCard({ gameId, refreshKey }: Props) {
  const [files, setFiles] = useState<GameFiles | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [nonce, setNonce] = useState(0)

  const reload = useCallback(() => setNonce(n => n + 1), [])

  useEffect(() => {
    let live = true
    setLoading(true)
    api.gameFiles(gameId)
      .then(f => { if (live) { setFiles(f); setError(null) } })
      .catch(err => { if (live) setError(err instanceof Error ? err.message : 'Could not read the save files.') })
      .finally(() => { if (live) setLoading(false) })
    return () => { live = false }
  }, [gameId, refreshKey, nonce])

  return (
    <Card
      title="Save files on this PC"
      flush
      headerRight={
        <div className="sl-files__head">
          <div className="sl-files__legend" aria-hidden="true">
            <span><i className="sl-dot sl-dot--ok" />In sync</span>
            <span><i className="sl-dot sl-dot--warn" />Changed here · will push</span>
            <span><i className="sl-dot sl-dot--ring" />Only on the server · will pull</span>
          </div>
          <Button size="sm" variant="quiet" disabled={loading} onClick={reload}>
            <RefreshCw size={13} aria-hidden="true" className={loading ? 'sl-spin' : undefined} />
            {loading ? 'Reading…' : 'Refresh'}
          </Button>
        </div>
      }
    >
      {error ? (
        <div className="sl-empty">{error}</div>
      ) : files === null ? (
        <div className="sl-empty">Reading the save files…</div>
      ) : (
        <div className="sl-files">
          {files.reachable
            ? files.head && (
                <p className="sl-files__note" title={formatDateTime(files.head.when)}>
                  Compared with the latest on the server: {formatAgo(files.head.when)} from {files.head.machine}.
                </p>
              )
            : <p className="sl-files__note">The server did not answer, so nothing is compared. These are the files here.</p>}
          {files.reachable && !files.head && (
            <p className="sl-files__note">Nothing is on the server yet: the next sync pushes every file below.</p>
          )}
          {files.folders.map(f => <FolderBlock key={f.key} folder={f} />)}
        </div>
      )}
    </Card>
  )
}

function FolderBlock({ folder }: { folder: GameFolderFiles }) {
  return (
    <section className="sl-files__folder" aria-label={`Save folder ${folder.label}`}>
      <div className="sl-files__root">
        <Folder size={14} aria-hidden="true" />
        <b>{folder.label}</b>
        <span className="sl-mono">{folder.path}</span>
      </div>
      {folder.files.length === 0 && folder.otherCount === 0
        ? <p className="sl-files__note">Nothing in this folder yet.</p>
        : <Tree files={folder.files} />}
      <Others folder={folder} />
    </section>
  )
}

interface Dir { dirs: Map<string, Dir>; files: (LocalFile & { name: string })[] }

function nest(files: LocalFile[]): Dir {
  const root: Dir = { dirs: new Map(), files: [] }
  for (const f of files) {
    const parts = f.path.split('/')
    let node = root
    for (const p of parts.slice(0, -1)) {
      let next = node.dirs.get(p)
      if (!next) node.dirs.set(p, next = { dirs: new Map(), files: [] })
      node = next
    }
    node.files.push({ ...f, name: parts[parts.length - 1] })
  }
  return root
}

function Tree({ files }: { files: LocalFile[] }) {
  return <Level node={nest(files)} prefix="" top />
}

/** One level of the tree: folders first (foldable, open to start with), then files. */
function Level({ node, prefix, top }: { node: Dir; prefix: string; top?: boolean }) {
  const [closed, setClosed] = useState<Set<string>>(new Set())
  const toggle = (k: string) => setClosed(s => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n })
  const dirs = [...node.dirs.entries()].sort(([a], [b]) => a.localeCompare(b))
  const files = [...node.files].sort((a, b) => a.name.localeCompare(b.name))
  return (
    <ul className={top ? 'sl-tree' : 'sl-tree sl-tree--sub'}>
      {dirs.map(([name, sub]) => {
        const k = prefix + name
        const open = !closed.has(k)
        return (
          <li key={'d:' + k}>
            <div className="sl-tree__node sl-tree__node--dir">
              <button type="button" className="sl-tree__twist" aria-expanded={open}
                aria-label={`${open ? 'Fold' : 'Unfold'} ${name}`} onClick={() => toggle(k)}>
                <ChevronRight size={12} className={open ? 'sl-tree__chev sl-tree__chev--open' : 'sl-tree__chev'} />
              </button>
              <Folder size={14} aria-hidden="true" className="sl-tree__icon" />
              <span className="sl-tree__name">{name}/</span>
            </div>
            {open && <Level node={sub} prefix={k + '/'} />}
          </li>
        )
      })}
      {files.map(f => {
        const l = look(f)
        return (
          <li key={'f:' + prefix + f.name}>
            <div className={`sl-tree__node${f.missing ? ' sl-tree__node--missing' : ''}`}
              title={f.modifiedUtc ? `${l.text} · written ${formatDateTime(f.modifiedUtc)}` : l.text}>
              <span className="sl-tree__twist" />
              <i className={l.dot} role="img" aria-label={l.text} />
              <span className="sl-tree__name sl-mono">{f.name}</span>
              {f.state !== 'same' && f.state != null && <span className="sl-tree__state">{l.text}</span>}
              <span className="sl-tree__size">{formatBytes(f.size)}</span>
            </div>
          </li>
        )
      })}
    </ul>
  )
}

/** The folder's files this game does not sync, folded into a line each: other games' saves (a shared
 *  emulator folder) and files an exclude pattern leaves out. */
function Others({ folder }: { folder: GameFolderFiles }) {
  const other = folder.other ?? []
  if (folder.otherCount === 0) return null
  // Past the listing cap the two kinds can no longer be told apart by count: one line for all of them.
  if (folder.otherCount > other.length) {
    return <OtherGroup items={other} more={folder.otherCount - other.length}
      label={`${folder.otherCount} other files in this folder aren't part of this game`} />
  }
  const games = other.filter(o => o.why === 'otherGame')
  const excluded = other.filter(o => o.why === 'excluded')
  return (
    <>
      {games.length > 0 && (
        <OtherGroup items={games} more={0} label={games.length === 1
          ? '1 other file in this folder belongs to another game'
          : `${games.length} other files in this folder belong to other games`} />
      )}
      {excluded.length > 0 && (
        <OtherGroup items={excluded} more={0} label={excluded.length === 1
          ? "1 file here isn't synced (an exclude pattern)"
          : `${excluded.length} files here aren't synced (exclude patterns)`} />
      )}
    </>
  )
}

function OtherGroup({ label, items, more }: { label: string; items: OtherFile[]; more: number }) {
  const [open, setOpen] = useState(false)
  return (
    <div className="sl-tree sl-tree--other">
      <div className="sl-tree__node">
        <button type="button" className="sl-tree__twist" aria-expanded={open} onClick={() => setOpen(o => !o)}
          aria-label={`${open ? 'Hide' : 'Show'}: ${label}`}>
          <ChevronRight size={12} className={open ? 'sl-tree__chev sl-tree__chev--open' : 'sl-tree__chev'} />
        </button>
        <Folder size={14} aria-hidden="true" className="sl-tree__icon" />
        <span className="sl-tree__name">{label}</span>
      </div>
      {open && (
        <ul className="sl-tree sl-tree--sub">
          {items.map(o => (
            <li key={o.path}>
              <div className="sl-tree__node">
                <span className="sl-tree__twist" />
                <File size={13} aria-hidden="true" className="sl-tree__icon" />
                <span className="sl-tree__name sl-mono">{o.path}</span>
                <span className="sl-tree__size">{formatBytes(o.size)}</span>
              </div>
            </li>
          ))}
          {more > 0 && <li className="sl-files__note">…and {more} more</li>}
        </ul>
      )}
    </div>
  )
}
