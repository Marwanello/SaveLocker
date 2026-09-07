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

/** Which side won — transferable across every conflict in the queue, since every conflict here is
 * this machine's own (versionA is always "the cloud", versionB is always "this device"'s push). */
type Kind = 'cloud' | 'local'

/**
 * Pauses on each conflict "Sync now" surfaced, one at a time, over the WHOLE app shell — sidebar
 * included — not just the content pane, so there's no way to click around it by accident. Reuses
 * `ConflictCard` in 'immediate' mode: pressing a side resolves it right away and the queue advances,
 * matching the tray's own "one conflict at a time" bulk-resolve queue (Phase 7) rather than a
 * batched list of independent judgment calls. "Decide later" always has somewhere to go — it never
 * just closes this and drops back to a spinner with no trace.
 * <p>
 * After the FIRST resolve in a multi-conflict queue, offers to apply that same choice — keep this
 * device's save, or keep the cloud's — to every remaining conflict in one step (plan.md Phase 7 item
 * 2), rather than making the player click through N one at a time. Asked only once per queue:
 * declining ("Review each") falls back to the ordinary one-at-a-time flow for the rest with no
 * further prompting.
 */
export function SyncConflictModal({ queue, games, machineName, onResolved, onAllDone, onLater }: Props) {
  const [index, setIndex] = useState(0)
  const [resolving, setResolving] = useState(false)
  const [asked, setAsked] = useState(false)
  const [applyPrompt, setApplyPrompt] = useState<{ kind: Kind; keepBoth: boolean } | null>(null)
  const { versions, stats } = useConflictVersions(queue)

  if (index >= queue.length) return null
  const conflict = queue[index]
  const game = games.find(g => g.id === conflict.gameId)

  function advance() {
    if (index + 1 >= queue.length) onAllDone()
    else setIndex(index + 1)
  }

  async function handleResolve(winningVersionId: string, keepBoth: boolean) {
    setResolving(true)
    try {
      await api.resolveConflict(conflict.id, winningVersionId, keepBoth)
      onResolved()
      const remaining = queue.length - index - 1
      if (!asked && remaining > 0) {
        setAsked(true)
        setApplyPrompt({ kind: winningVersionId === conflict.versionAId ? 'cloud' : 'local', keepBoth })
        return
      }
      advance()
    } catch (e) {
      alert('Could not resolve the conflict: ' + (e as Error).message)
    } finally {
      setResolving(false)
    }
  }

  async function applyToAllRemaining() {
    if (!applyPrompt) return
    setResolving(true)
    try {
      for (let i = index + 1; i < queue.length; i++) {
        const c = queue[i]
        await api.resolveConflict(c.id, applyPrompt.kind === 'cloud' ? c.versionAId : c.versionBId, applyPrompt.keepBoth)
      }
      onResolved()
      onAllDone()
    } catch (e) {
      alert('Could not apply to the remaining conflicts: ' + (e as Error).message)
    } finally {
      setResolving(false)
      setApplyPrompt(null)
    }
  }

  function reviewEach() {
    setApplyPrompt(null)
    advance()
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

        {applyPrompt ? (
          <ApplyToAllPrompt
            kindLabel={applyPrompt.kind === 'cloud' ? 'the cloud' : 'this device'}
            remaining={queue.length - index - 1}
            busy={resolving}
            onApply={() => void applyToAllRemaining()}
            onReviewEach={reviewEach}
          />
        ) : (
          <ConflictCard
            key={conflict.id}
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
        )}
      </div>
    </div>
  )
}

/** The follow-up shown once, right after the first resolve in a multi-conflict queue — see the
 * module doc comment above. */
function ApplyToAllPrompt({ kindLabel, remaining, busy, onApply, onReviewEach }: {
  kindLabel: string
  remaining: number
  busy: boolean
  onApply: () => void
  onReviewEach: () => void
}) {
  return (
    <div style={{
      background: '#1E252A', border: '1px solid #34424b',
      borderRadius: 10, padding: '18px 20px',
    }}>
      <div style={{ color: '#ECEFF1', fontSize: 13.5, fontWeight: 700 }}>Apply to the rest too?</div>
      <div style={{ color: '#8b9aaa', fontSize: 12.5, lineHeight: 1.6, marginTop: 8 }}>
        Keep <strong style={{ color: '#ECEFF1' }}>{kindLabel}</strong>'s save for the other {remaining}{' '}
        conflict{remaining === 1 ? '' : 's'} too, or go through {remaining === 1 ? 'it' : 'them'} one at a time?
      </div>
      <div style={{ display: 'flex', gap: 10, marginTop: 16 }}>
        <button
          disabled={busy}
          onClick={onApply}
          style={{
            padding: '7px 16px', borderRadius: 6, fontSize: 12, fontWeight: 700, border: 'none',
            cursor: busy ? 'default' : 'pointer', opacity: busy ? 0.6 : 1,
            background: '#129271', color: '#fff',
          }}
        >
          Apply to all remaining
        </button>
        <button
          disabled={busy}
          onClick={onReviewEach}
          style={{
            padding: '7px 16px', borderRadius: 6, fontSize: 12, fontWeight: 600,
            background: 'transparent', color: '#8b9aaa', border: '1px solid #445059',
            cursor: busy ? 'default' : 'pointer', opacity: busy ? 0.6 : 1,
          }}
        >
          Review each
        </button>
      </div>
    </div>
  )
}
