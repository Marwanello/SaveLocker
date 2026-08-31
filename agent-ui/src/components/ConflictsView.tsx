import { useEffect, useRef, useState } from 'react'
import { GitBranch } from 'lucide-react'
import { api } from '../api'
import type { Conflict, SaveVersion, TrackedGame, VersionStats } from '../types'

const shortId = (id: string) => id.replace(/-/g, '').slice(0, 8)
// Server timestamps have no zone suffix but are UTC (System.Text.Json default) — without this a
// browser in a non-UTC zone parses them as local time and every "when" is wrong by the offset.
const asUtc = (t: string) => /[Z+]|-\d\d:\d\d$/.test(t) ? t : t + 'Z'
const when = (t: string) => new Date(asUtc(t)).toLocaleString()
const fmtSize = (n: number) =>
  n < 1024 ? `${n} B`
    : n < 1024 * 1024 ? (n / 1024).toFixed(1) + ' KB'
      : (n / (1024 * 1024)).toFixed(2) + ' MB'

interface Props {
  conflicts: Conflict[]
  games: TrackedGame[]
  machineName: string
  onRefresh: () => void
}

/**
 * Shared `agent-ui` conflicts page (tasks/conflict-resolution-ui/plan.md, Phase 6). Genuinely new
 * code, not a port of the dashboard's `GameDetail.tsx` conflict card — this fetches through the
 * agent's own local API (`/api/conflicts`, `/api/versions/{id}`), a distinct wire protocol from the
 * dashboard's — but the visual shape (machine, timestamp, size, file count, newest-change, Use as
 * Latest / Keep both) is deliberately copied from that already-shipped card for consistency.
 */
export function ConflictsView({ conflicts, games, machineName, onRefresh }: Props) {
  const [versions, setVersions] = useState<Record<string, SaveVersion>>({})
  const [stats, setStats] = useState<Record<string, VersionStats>>({})
  // An archive's contents never change once uploaded, so this stops the poll driving `conflicts`
  // from re-fetching what it already has, even though `conflicts` is a fresh array reference every
  // time — the same pattern GameDetail.tsx already uses for the same reason.
  const requestedRef = useRef<Set<string>>(new Set())
  const [resolvingId, setResolvingId] = useState<string | null>(null)

  useEffect(() => {
    for (const c of conflicts) {
      for (const vid of [c.versionAId, c.versionBId]) {
        if (requestedRef.current.has(vid)) continue
        requestedRef.current.add(vid)
        api.version(vid)
          .then(v => setVersions(prev => ({ ...prev, [vid]: v })))
          .catch(() => requestedRef.current.delete(vid)) // best-effort — retry on the next poll
        api.versionStats(vid)
          .then(s => setStats(prev => ({ ...prev, [vid]: s })))
          .catch(() => { /* stats are a bonus line, not load-bearing */ })
      }
    }
  }, [conflicts])

  async function resolve(conflictId: string, versionId: string, keepBoth: boolean) {
    setResolvingId(conflictId)
    try { await api.resolveConflict(conflictId, versionId, keepBoth); onRefresh() }
    catch (e) { alert('Could not resolve the conflict: ' + (e as Error).message) }
    finally { setResolvingId(null) }
  }

  if (conflicts.length === 0) {
    return (
      <div style={{
        position: 'absolute', inset: 0,
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        gap: 14, padding: 24,
      }}>
        <GitBranch size={40} strokeWidth={1.75} color="#129271" />
        <div style={{ color: '#ECEFF1', fontSize: 16, fontWeight: 700 }}>No open conflicts</div>
        <div style={{ color: '#9CA3AF', fontSize: 13, textAlign: 'center', maxWidth: 380, lineHeight: 1.6 }}>
          Every tracked game's save matches the cloud. If this device and another both change the
          same save before syncing, the choice will show up here.
        </div>
      </div>
    )
  }

  return (
    <div style={{
      position: 'absolute', inset: 0, overflowY: 'auto',
      padding: 16, display: 'flex', flexDirection: 'column', gap: 12,
    }}>
      {conflicts.map(c => {
        const game = games.find(g => g.id === c.gameId)
        return (
          <div key={c.id} style={{
            background: '#241a1a', border: `1px solid ${c.escalated ? '#e5534b' : '#4a2a2a'}`,
            borderRadius: 8, padding: '12px 14px', flexShrink: 0,
          }}>
            <div style={{ color: '#f4a60d', fontWeight: 700, fontSize: 13 }}>
              {game?.name ?? shortId(c.gameId)} — choose the save to keep
            </div>
            {c.escalated && (
              <div style={{ color: '#e5534b', fontSize: 11, fontWeight: 600, marginTop: 5 }}>
                Overdue — this conflict has been unresolved for more than six hours.
              </div>
            )}
            {c.count > 1 && (
              <div style={{ color: '#8b9aaa', fontSize: 11, marginTop: 5, lineHeight: 1.5 }}>
                {c.count} divergent saves folded into this conflict — the newest is offered below.
              </div>
            )}

            <div style={{ display: 'flex', gap: 8, marginTop: 10, flexWrap: 'wrap' }}>
              {[c.versionAId, c.versionBId].map(vid => {
                const v = versions[vid]
                const s = stats[vid]
                const mine = v?.machineName === machineName
                return (
                  <div key={vid} style={{
                    flex: '1 1 210px', minWidth: 210,
                    background: '#1E252A', border: '1px solid #4a2a2a', borderRadius: 6, padding: 10,
                  }}>
                    <div style={{ color: '#ECEFF1', fontWeight: 600, fontSize: 12 }}>
                      {v ? (mine ? `This device (${v.machineName})` : v.machineName) : shortId(vid)}
                    </div>
                    <div style={{ color: '#8b9aaa', fontSize: 10.5, fontFamily: "'JetBrains Mono', monospace", margin: '3px 0' }}>
                      {v ? `${when(v.createdAt)} · ${fmtSize(v.size)}` : shortId(vid)}
                    </div>
                    {s && (
                      <div style={{ color: '#8b9aaa', fontSize: 10.5, fontFamily: "'JetBrains Mono', monospace" }}>
                        {s.fileCount} file{s.fileCount === 1 ? '' : 's'}
                        {s.newestFileWriteUtc ? ` · newest change ${when(s.newestFileWriteUtc)}` : ''}
                      </div>
                    )}
                    <div style={{ display: 'flex', gap: 6, marginTop: 8 }}>
                      <button
                        disabled={resolvingId === c.id}
                        onClick={() => resolve(c.id, vid, false)}
                        style={{
                          padding: '5px 10px', background: '#129271', color: '#fff', border: 'none',
                          borderRadius: 4, fontSize: 11, cursor: 'pointer',
                          opacity: resolvingId === c.id ? 0.6 : 1,
                        }}
                      >
                        Use as Latest
                      </button>
                      <button
                        disabled={resolvingId === c.id}
                        onClick={() => resolve(c.id, vid, true)}
                        style={{
                          padding: '5px 10px', background: 'transparent', color: '#fdce63',
                          border: '1px solid #fdce63', borderRadius: 4, fontSize: 11, cursor: 'pointer',
                          opacity: resolvingId === c.id ? 0.6 : 1,
                        }}
                      >
                        Keep both · use this
                      </button>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>
        )
      })}
    </div>
  )
}
