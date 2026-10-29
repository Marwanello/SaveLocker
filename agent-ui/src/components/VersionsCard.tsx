import { useEffect, useState } from 'react'
import { GitFork } from 'lucide-react'
import { api } from '../api'
import { formatAgo, formatBytes, formatDateTime, shortId } from '../format'
import type { SaveVersion } from '../types'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Button } from './ui/Button'
import { Seg } from './ui/Seg'

interface Props {
  gameId: string
  headId: string | null
  /** Bumped by the page after a sync, so the list follows what that sync just did. */
  refreshKey: number
  onLoaded: (versions: SaveVersion[]) => void
  /** The card's heading; inside a title it names the emulator. */
  title?: string
  /** Inside a title: copy a version to another emulator's tree (tasks/multi-emulator-games Phase 6). */
  onCopy?: (versionId: string) => void
  /** A playthrough other than this device's own (tasks/save-playthroughs); the list is that one's. */
  playthrough?: string
  /** "Fork…": a new playthrough starting from this version. */
  onFork?: (v: SaveVersion) => void
  /** The playthrough this list is of, and the game's playthroughs by name: a fork's head belongs to the one it was forked
   *  from (the server lists it here until this one's first push), and its row says so. */
  listOf?: string
  playthroughs?: { id: string; name: string }[]
}

type View = 'main' | 'backups'

/**
 * "Versions on the server": what the server keeps of this game, in two tabs the console's Saves card
 * also shows — History (this playthrough's own line, walked back from its head by parentVersionId) and
 * Backups (everything else: a save that lost a conflict, or was ridden over by a forced push). Read-only
 * — this page is the agent's; pruning and Set-as-Latest are the console's.
 */
export function VersionsCard({ gameId, headId, refreshKey, onLoaded, title = 'Versions on the server', onCopy, playthrough, onFork, listOf, playthroughs }: Props) {
  const [versions, setVersions] = useState<SaveVersion[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [view, setView] = useState<View>('main')

  useEffect(() => {
    let live = true
    api.gameVersions(gameId, playthrough)
      .then(v => { if (live) { setVersions(v); setError(null); onLoaded(v) } })
      .catch(err => { if (live) setError(err instanceof Error ? err.message : 'Could not reach the server.') })
    return () => { live = false }
    // `onLoaded` is the page's own state setter; listing it would only re-fetch on identity churn.
  }, [gameId, refreshKey, playthrough])

  // Same rule as web's SavesCard.tsx (PlaythroughVersions): History is the head's own parent chain; a
  // backup is any other version this playthrough still has (lost a conflict, or was forced over).
  const mainIds = new Set<string>()
  {
    const byId = new Map((versions ?? []).map(v => [v.id, v]))
    let cur = headId
    while (cur && !mainIds.has(cur)) { mainIds.add(cur); cur = byId.get(cur)?.parentVersionId ?? null }
  }
  const main = (versions ?? []).filter(v => mainIds.has(v.id))
  const backups = (versions ?? []).filter(v => !mainIds.has(v.id))
  const list = view === 'main' ? main : backups
  const shown = list.slice(0, 10)

  function forked(v: SaveVersion) {
    if (!listOf || !v.playthroughId || v.playthroughId === listOf) return null
    const name = playthroughs?.find(p => p.id === v.playthroughId)?.name ?? 'another playthrough'
    return <span title={`This playthrough started from this save. It belongs to ${name} and stays there.`}><Chip>From {name}</Chip></span>
  }

  function state(v: SaveVersion) {
    if (v.id === headId) return <Chip tone="ok">Latest</Chip>
    if (v.mergedFrom) return <Chip>From {v.mergedFrom}</Chip>
    if (v.protected) return <Chip tone="warn">Protected</Chip>
    return <Chip>{view === 'main' ? 'Kept' : 'Backup'}</Chip>
  }

  return (
    <Card
      title={title}
      flush
      headerRight={versions && <Chip>{versions.length} kept</Chip>}
    >
      {error ? (
        <div className="sl-empty">{error}</div>
      ) : versions === null ? (
        <div className="sl-empty">Loading…</div>
      ) : versions.length === 0 ? (
        <div className="sl-empty">
          {playthrough
            ? 'Nothing has been pushed to this playthrough yet. It starts from the save it was made from, which stays in the playthrough that made it.'
            : 'Nothing has been uploaded for this game yet.'}
        </div>
      ) : (
        <>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '10px 17px', borderBottom: '1px solid var(--color-row)' }}>
            <Seg value={view} onChange={setView} aria-label="History or backups"
              options={[{ value: 'main', label: `History ${main.length}` }, { value: 'backups', label: `Backups ${backups.length}` }]} />
          </div>
          <div className="sl-table-wrap">
            {list.length === 0 ? (
              <div className="sl-empty">
                {view === 'main' ? 'No versions yet.' : 'No backups. A save that loses a conflict, or is ridden over by a forced push, is kept here.'}
              </div>
            ) : (
              <table className="sl-table">
                <thead>
                  <tr><th>Version</th><th>When</th><th>Machine</th><th className="sl-num">Size</th><th>State</th><th className="sl-end">Actions</th></tr>
                </thead>
                <tbody>
                  {shown.map(v => (
                    <tr key={v.id}>
                      <td className="sl-mono">{shortId(v.id)}</td>
                      <td title={formatDateTime(v.createdAt)}>
                        {formatAgo(v.createdAt)}
                        {v.copiedFrom && <span className="sl-copied">Copied from {v.copiedFrom}</span>}
                      </td>
                      <td className="sl-dim">{v.machineName}</td>
                      <td className="sl-num sl-dim">{formatBytes(v.size)}</td>
                      <td>{state(v)} {forked(v)}</td>
                      <td className="sl-end">
                        <span style={{ display: 'inline-flex', gap: 6, justifyContent: 'flex-end' }}>
                          {onFork && !v.mergedFrom && (
                            <Button size="sm" variant="quiet" aria-label="Fork this version into a new playthrough"
                              title="Fork this version into a new playthrough"
                              data-pad-label="Fork this version into a new playthrough" onClick={() => onFork(v)}>
                              <GitFork size={14} aria-hidden="true" />
                            </Button>
                          )}
                          {onCopy && !v.mergedFrom && (
                            <Button size="sm" variant="quiet" data-pad-label="Copy this version to another emulator" onClick={() => onCopy(v.id)}>
                              Copy to…
                            </Button>
                          )}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {list.length > shown.length && (
              <div className="sl-empty" style={{ padding: '10px 17px' }}>
                And {list.length - shown.length} older. The console lists every version.
              </div>
            )}
          </div>
        </>
      )}
    </Card>
  )
}
