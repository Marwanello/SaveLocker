import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { markActivitySeen, isWarning } from '../activitySeen'
import { copyText } from '../clipboard'
import { formatAgo, formatBytes, formatDateTime, formatTime } from '../format'
import { useActivityRecent } from '../useActivity'
import type { OfflineQueueEntry } from '../types'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { PageHead } from './ui/PageHead'

/**
 * The full activity feed (the Overview keeps its short "Recent"), what is waiting for the server to
 * come back, and a way to reach agent.log. The queue is the reason this page exists: a push that
 * failed because the server was unreachable is otherwise invisible until it drains.
 */
export function ActivityView() {
  const recent = useActivityRecent() ?? []
  const [queue, setQueue] = useState<OfflineQueueEntry[] | null>(null)
  const [queueError, setQueueError] = useState<string | null>(null)
  const [logNote, setLogNote] = useState<{ path: string; copied: boolean } | null>(null)
  const [opening, setOpening] = useState(false)

  // Opening the page is what "seen" means, and so is leaving it: whatever arrived while it was open
  // was read there, and must not come back as a badge the moment the user navigates away.
  useEffect(() => {
    markActivitySeen()
    return markActivitySeen
  }, [])

  const loadQueue = useCallback(() => {
    api.offlineQueue()
      .then(q => { setQueue(q); setQueueError(null) })
      .catch(err => setQueueError(err instanceof Error ? err.message : 'Could not read the queue.'))
  }, [])

  // The queue drains on its own (the drainer retries every 30 s), so a page that showed it once would
  // keep claiming a push is waiting long after it went through.
  useEffect(() => {
    loadQueue()
    const id = setInterval(loadQueue, 5_000)
    return () => clearInterval(id)
  }, [loadQueue])

  async function openLog() {
    setOpening(true)
    setLogNote(null)
    try {
      const res = await api.openLog()
      // Nothing opened (a headless box): say where the file is, rather than pretending.
      if (!res.opened) setLogNote({ path: res.path, copied: false })
    } catch (err) {
      setLogNote({ path: err instanceof Error ? err.message : 'Could not open the log.', copied: false })
    } finally {
      setOpening(false)
    }
  }

  const waiting = queue?.length ?? 0

  return (
    <div className="sl-page">
      <PageHead
        title="Activity"
        sub={`${recent.length} recent event${recent.length === 1 ? '' : 's'} · ${waiting === 0 ? 'nothing waiting to upload' : `${waiting} waiting to upload`}`}
        actions={<Button onClick={() => void openLog()} disabled={opening}>Open agent.log</Button>}
      />

      {logNote && (
        <div className="sl-confirm" role="status">
          <span>No desktop to open it on. The log is at</span>
          <span className="sl-path">{logNote.path}</span>
          <Button size="sm" onClick={() => void copyText(logNote.path).then(ok => setLogNote({ ...logNote, copied: ok }))}>
            {logNote.copied ? 'Copied' : 'Copy'}
          </Button>
        </div>
      )}

      <Card
        title="Offline queue"
        flush
        headerRight={waiting > 0 ? <Chip tone="warn">{waiting} waiting</Chip> : <Chip tone="ok">Empty</Chip>}
      >
        {queueError ? (
          <div className="sl-empty">{queueError}</div>
        ) : queue === null ? (
          <div className="sl-empty">Loading…</div>
        ) : queue.length === 0 ? (
          <div className="sl-empty">
            Nothing is waiting. If the server is unreachable when a save is pushed, it is kept here and
            sent as soon as the connection comes back.
          </div>
        ) : (
          <div className="sl-table-wrap">
            <table className="sl-table">
              <thead>
                <tr><th>Game</th><th>Queued</th><th className="sl-num">Size</th><th className="sl-num">Attempts</th></tr>
              </thead>
              <tbody>
                {queue.map(e => (
                  <tr key={e.gameId}>
                    <td>{e.gameName}{e.force && <> <Chip tone="warn">Forced</Chip></>}</td>
                    <td className="sl-dim" title={formatDateTime(e.queuedAt)}>{formatAgo(e.queuedAt)}</td>
                    <td className="sl-num sl-dim">{e.size > 0 ? formatBytes(e.size) : '—'}</td>
                    <td className="sl-num sl-dim">{e.attempts}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card title="Everything the agent did" flush>
        {recent.length === 0 ? (
          <div className="sl-empty">No activity yet. Pushes, pulls and warnings show up here as they happen.</div>
        ) : (
          <ul className="sl-feed">
            {recent.map((e, i) => (
              <li key={`${e.timestampUtc}-${i}`}>
                <span className="sl-feed__when">{formatTime(e.timestampUtc)}</span>
                <span
                  className={`sl-dot ${/conflict/i.test(e.message) ? 'sl-dot--crit' : isWarning(e.message) ? 'sl-dot--warn' : ''}`.trim()}
                  style={{ marginTop: 6 }}
                  aria-hidden="true"
                />
                <span className="sl-feed__what">{e.message}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
