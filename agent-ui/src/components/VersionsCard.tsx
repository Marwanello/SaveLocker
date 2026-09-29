import { useEffect, useState } from 'react'
import { api } from '../api'
import { formatAgo, formatBytes, formatDateTime } from '../format'
import type { SaveVersion } from '../types'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'

interface Props {
  gameId: string
  headId: string | null
  /** Bumped by the page after a sync, so the list follows what that sync just did. */
  refreshKey: number
  onLoaded: (versions: SaveVersion[]) => void
}

/**
 * "Versions on the server": what the server keeps of this game, newest first, from the per-game
 * versions route. Read-only — this page is the agent's; pruning and Set-as-Latest are the console's.
 * The newest ten are shown; the count above is always the real total.
 */
export function VersionsCard({ gameId, headId, refreshKey, onLoaded }: Props) {
  const [versions, setVersions] = useState<SaveVersion[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let live = true
    api.gameVersions(gameId)
      .then(v => { if (live) { setVersions(v); setError(null); onLoaded(v) } })
      .catch(err => { if (live) setError(err instanceof Error ? err.message : 'Could not reach the server.') })
    return () => { live = false }
    // `onLoaded` is the page's own state setter; listing it would only re-fetch on identity churn.
  }, [gameId, refreshKey])

  const shown = versions?.slice(0, 10) ?? []

  return (
    <Card
      title="Versions on the server"
      flush
      headerRight={versions && <Chip>{versions.length} kept</Chip>}
    >
      {error ? (
        <div className="sl-empty">{error}</div>
      ) : versions === null ? (
        <div className="sl-empty">Loading…</div>
      ) : versions.length === 0 ? (
        <div className="sl-empty">Nothing has been uploaded for this game yet.</div>
      ) : (
        <div className="sl-table-wrap">
          <table className="sl-table">
            <thead>
              <tr><th>When</th><th>From</th><th className="sl-num">Size</th><th /></tr>
            </thead>
            <tbody>
              {shown.map(v => (
                <tr key={v.id}>
                  <td title={formatDateTime(v.createdAt)}>{formatAgo(v.createdAt)}</td>
                  <td className="sl-dim">{v.machineName}</td>
                  <td className="sl-num sl-dim">{formatBytes(v.size)}</td>
                  <td>
                    {v.id === headId && <Chip tone="ok">Latest</Chip>}
                    {v.protected && v.id !== headId && <Chip>Protected</Chip>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {versions.length > shown.length && (
            <div className="sl-empty" style={{ padding: '10px 17px' }}>
              And {versions.length - shown.length} older. The console lists every version.
            </div>
          )}
        </div>
      )}
    </Card>
  )
}
