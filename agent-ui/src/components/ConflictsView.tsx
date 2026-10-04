import { useEffect, useState } from 'react'
import { GitBranch } from 'lucide-react'
import { api } from '../api'
import { formatAgo, formatDateTime, formatSpan, asUtc } from '../format'
import { useConflictVersions } from '../useConflictVersions'
import type { Conflict, SaveVersion, TrackedGame } from '../types'
import { ConflictCard } from './ConflictCard'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { PageHead } from './ui/PageHead'

interface Props {
  conflicts: Conflict[]
  games: TrackedGame[]
  machineName: string
  onRefresh: () => void
}

const when = (v: SaveVersion | undefined) => (v ? `${formatAgo(v.createdAt)} (${formatDateTime(v.createdAt)})` : 'a moment earlier')

/**
 * Shared `agent-ui` conflicts page (tasks/conflict-resolution-ui/plan.md, Phase 6). Genuinely new
 * code, not a port of the dashboard's `GameDetail.tsx` conflict card — this fetches through the
 * agent's own local API (`/api/conflicts`, `/api/versions/{id}`), a distinct wire protocol from the
 * dashboard's. `ConflictCard` (shared with the sync-time pop-up) carries the actual local-vs-cloud
 * framing and is unchanged; this view is the page around it: the head, a "why" per conflict, and
 * what was recently resolved.
 */
export function ConflictsView({ conflicts, games, machineName, onRefresh }: Props) {
  const { versions, stats } = useConflictVersions(conflicts)
  const [resolvingId, setResolvingId] = useState<string | null>(null)
  const [resolved, setResolved] = useState<Conflict[] | null>(null)

  // Re-read whenever the open set changes size: resolving one is exactly what adds a row here.
  useEffect(() => {
    let live = true
    api.resolvedConflicts().then(r => { if (live) setResolved(r) }).catch(() => { if (live) setResolved([]) })
    return () => { live = false }
  }, [conflicts.length])

  async function resolve(conflictId: string, versionId: string, keepBoth: boolean) {
    setResolvingId(conflictId)
    try { await api.resolveConflict(conflictId, versionId, keepBoth); onRefresh() }
    catch (e) { alert('Could not resolve the conflict: ' + (e as Error).message) }
    finally { setResolvingId(null) }
  }

  const gameName = (id: string) => games.find(g => g.id === id)?.name ?? id
  const oldest = conflicts.length > 0
    ? Math.min(...conflicts.map(c => new Date(asUtc(c.createdAt)).getTime()))
    : null

  const recentlyResolved = resolved && resolved.length > 0 && (
    <Card title="Recently resolved" flush headerRight={<Chip>{resolved.length} in the last week</Chip>}>
      <div className="sl-table-wrap">
        <table className="sl-table">
          <thead><tr><th>Game</th><th>Kept</th><th>When</th><th>By</th></tr></thead>
          <tbody>
            {resolved.map(c => (
              <tr key={c.id}>
                <td>{gameName(c.gameId)}</td>
                <td>{c.resolvedVersionId === c.versionBId ? 'This device' : c.resolvedVersionId === c.versionAId ? 'The cloud' : 'Another version'}</td>
                <td className="sl-dim" title={c.resolvedAt ? formatDateTime(c.resolvedAt) : undefined}>
                  {c.resolvedAt ? formatAgo(c.resolvedAt) : '—'}
                </td>
                <td className="sl-dim">{c.resolvedBy ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  )

  if (conflicts.length === 0) {
    return (
      <div className="sl-page">
        <PageHead title="Conflicts" sub="No open conflicts · syncing normally" />
        <Card>
          <div className="sl-empty" style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12, padding: '30px 18px' }}>
            <GitBranch size={36} strokeWidth={1.75} color="var(--color-safe-ink)" />
            <div style={{ color: 'var(--color-fg)', fontSize: 15, fontWeight: 700 }}>No open conflicts</div>
            <div style={{ maxWidth: 380, lineHeight: 1.6 }}>
              Every tracked game's save matches the cloud. If this device and the cloud both change the
              same save before syncing, the choice will show up here.
            </div>
          </div>
        </Card>
        {recentlyResolved}
      </div>
    )
  }

  return (
    <div className="sl-page">
      <PageHead
        title="Conflicts"
        sub={`Sync paused for ${conflicts.length === 1 ? 'this game' : 'these games'} · ${conflicts.length} open${oldest !== null ? ` · oldest ${formatSpan(Date.now() - oldest)}` : ''}`}
      />
      {conflicts.map(c => {
        const a = versions[c.versionAId]
        const b = versions[c.versionBId]
        const base = b?.parentVersionId ? versions[b.parentVersionId] : undefined
        return (
          <div key={c.id} className="sl-stack">
            <ConflictCard
              conflict={c}
              gameName={gameName(c.gameId)}
              machineName={machineName}
              versionA={a}
              versionB={b}
              statsA={stats[c.versionAId]}
              statsB={stats[c.versionBId]}
              resolving={resolvingId === c.id}
              onResolve={(versionId, keepBoth) => resolve(c.id, versionId, keepBoth)}
            />
            <Card title="Why did this happen?">
              <dl className="sl-kv">
                <dt>This device</dt>
                <dd>{b ? `${b.machineName} pushed a save ${when(b)}` : 'Loading…'}</dd>
                <dt>Built on</dt>
                <dd>{b?.parentVersionId ? (base ? `the save ${base.machineName} uploaded ${when(base)}` : 'an earlier save') : 'no earlier save — it was the first push'}</dd>
                <dt>The cloud</dt>
                <dd>{a ? `${a.machineName} had uploaded a different save ${when(a)}` : 'Loading…'}</dd>
                <dt>So</dt>
                <dd>
                  Both moved on from the same starting point, and SaveLocker will not choose between two saves
                  for you{c.count > 1 ? ` — it has happened ${c.count} times since` : ''}.
                </dd>
              </dl>
            </Card>
          </div>
        )
      })}
      {recentlyResolved}
    </div>
  )
}
