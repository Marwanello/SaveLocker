import { useState } from 'react'
import { GitBranch } from 'lucide-react'
import { api } from '../api'
import { useConflictVersions } from '../useConflictVersions'
import type { Conflict, TrackedGame } from '../types'
import { ConflictCard } from './ConflictCard'

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
 * dashboard's. `ConflictCard` (shared with the sync-time pop-up) carries the actual local-vs-cloud
 * framing; this view is just the list, the empty state, and the fetch/resolve plumbing around it.
 */
export function ConflictsView({ conflicts, games, machineName, onRefresh }: Props) {
  const { versions, stats } = useConflictVersions(conflicts)
  const [resolvingId, setResolvingId] = useState<string | null>(null)

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
          Every tracked game's save matches the cloud. If this device and the cloud both change the
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
      {conflicts.map(c => (
        <ConflictCard
          key={c.id}
          conflict={c}
          gameName={games.find(g => g.id === c.gameId)?.name ?? c.gameId}
          machineName={machineName}
          versionA={versions[c.versionAId]}
          versionB={versions[c.versionBId]}
          statsA={stats[c.versionAId]}
          statsB={stats[c.versionBId]}
          resolving={resolvingId === c.id}
          onResolve={(versionId, keepBoth) => resolve(c.id, versionId, keepBoth)}
        />
      ))}
    </div>
  )
}
