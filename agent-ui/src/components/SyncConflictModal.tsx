import { useState } from 'react'
import { ArrowRight } from 'lucide-react'
import { api } from '../api'
import { useConflictVersions } from '../useConflictVersions'
import type { Conflict, TrackedGame } from '../types'
import { ConflictCard } from './ConflictCard'

interface Props {
  queue: Conflict[]
  games: TrackedGame[]
  machineName: string
  onResolved: () => void
  onAllDone: () => void
  onLater: () => void
}

/**
 * Pauses on each conflict "Sync now" surfaced, one at a time, over the WHOLE app shell — sidebar
 * included — not just the content pane, so there's no way to click around it by accident. Reuses
 * `ConflictCard` in 'immediate' mode: pressing a side resolves it right away and the queue advances,
 * matching the tray's own planned "one conflict at a time" bulk-resolve queue (Phase 7) rather than
 * a batched list of independent judgment calls. "Decide later" always has somewhere to go — it never
 * just closes this and drops back to a spinner with no trace.
 */
export function SyncConflictModal({ queue, games, machineName, onResolved, onAllDone, onLater }: Props) {
  const [index, setIndex] = useState(0)
  const [resolving, setResolving] = useState(false)
  const { versions, stats } = useConflictVersions(queue)

  if (index >= queue.length) return null
  const conflict = queue[index]
  const game = games.find(g => g.id === conflict.gameId)

  async function handleResolve(winningVersionId: string, keepBoth: boolean) {
    setResolving(true)
    try {
      await api.resolveConflict(conflict.id, winningVersionId, keepBoth)
      onResolved()
      if (index + 1 >= queue.length) onAllDone()
      else setIndex(index + 1)
    } catch (e) {
      alert('Could not resolve the conflict: ' + (e as Error).message)
    } finally {
      setResolving(false)
    }
  }

  return (
    <div style={{
      position: 'fixed', inset: 0, zIndex: 1000,
      background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
    }}>
      <div style={{ width: '100%', maxWidth: 460 }}>
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          fontSize: 10.5, fontWeight: 700, letterSpacing: '0.08em', textTransform: 'uppercase',
          color: '#556070', marginBottom: 8,
        }}>
          <span>Sync paused — conflict found</span>
          {queue.length > 1 && (
            <span style={{
              fontFamily: "'JetBrains Mono', monospace", color: '#fdce63',
              background: 'rgba(244,166,13,0.12)', border: '1px solid rgba(244,166,13,0.35)',
              borderRadius: 20, padding: '2px 8px', letterSpacing: 0,
            }}>{index + 1} of {queue.length}</span>
          )}
        </div>

        <ConflictCard
          conflict={conflict}
          gameName={game?.name ?? 'This game'}
          machineName={machineName}
          versionA={versions[conflict.versionAId]}
          versionB={versions[conflict.versionBId]}
          statsA={stats[conflict.versionAId]}
          statsB={stats[conflict.versionBId]}
          resolving={resolving}
          mode="immediate"
          onResolve={handleResolve}
          footerExtra={
            <button
              onClick={onLater}
              disabled={resolving}
              style={{
                display: 'flex', alignItems: 'center', gap: 6,
                fontSize: 11.5, fontWeight: 600, color: '#8b9aaa',
                background: 'none', border: '1px solid #445059', borderRadius: 5,
                padding: '6px 11px', cursor: resolving ? 'default' : 'pointer',
              }}
            >
              Decide later — go to Conflicts <ArrowRight size={12} strokeWidth={2} />
            </button>
          }
        />
      </div>
    </div>
  )
}
