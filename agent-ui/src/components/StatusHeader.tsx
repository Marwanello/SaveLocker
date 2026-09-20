import { useState } from 'react'
import type { ReactNode } from 'react'
import { Check, GitBranch, RefreshCw, Unplug } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { api } from '../api'
import { formatBytes } from '../format'
import { refreshActivity, useActivityBusy, useActivityCurrent } from '../useActivity'
import type { AgentState, Conflict, TrackedGame } from '../types'
import { Button } from './ui/Button'
import { Chip } from './ui/Chip'
import { Toast } from './ui/Toast'

interface Props {
  state: AgentState | null
  conflicts: Conflict[]
  games: TrackedGame[]
  /** Fired once a Sync all run finishes, success or failure. The caller refreshes what the run may
   *  have changed (the "last sync" stat, any conflict it surfaced) and decides whether to pause on one. */
  onSynced: () => void
}

interface Summary {
  tone: 'neutral' | 'ok' | 'crit'
  Icon: LucideIcon
  title: string
  detail: string
  chip: ReactNode
}

/** What the header says when nothing is syncing. Ordered by what a person most needs to know:
 *  a machine that cannot sync at all, then a decision waiting, then "all clear". */
function summarize(state: AgentState | null, conflicts: Conflict[], games: TrackedGame[]): Summary {
  if (!state) {
    return { tone: 'neutral', Icon: Check, title: 'Starting…', detail: 'Reading this machine\'s state.', chip: null }
  }
  if (!state.connected) {
    return {
      tone: 'crit', Icon: Unplug, title: 'Not connected to a server',
      detail: 'Register this machine in Settings to start syncing.', chip: null,
    }
  }
  if (conflicts.length > 0) {
    const first = games.find(g => g.id === conflicts[0].gameId)?.name ?? 'A game'
    return {
      tone: 'crit', Icon: GitBranch,
      title: conflicts.length === 1 ? `${first} needs a decision` : `${conflicts.length} games need a decision`,
      detail: 'Both copies changed since the last sync. Keep one.',
      chip: <Chip tone="crit">{conflicts.length} conflict{conflicts.length === 1 ? '' : 's'}</Chip>,
    }
  }
  const last = state.lastSyncAgo === '—' ? 'no sync yet' : `last sync ${state.lastSyncAgo}`
  return {
    tone: 'ok', Icon: Check, title: 'Idle — nothing syncing right now.',
    detail: `${state.machineName} · ${last}`, chip: <Chip tone="ok">All clear</Chip>,
  }
}

/**
 * The strip under the top bar on every page: what the agent is doing and the one button that starts
 * a sync of everything. Replaces the old "Agent Status: CONNECTED" header and the Overview's "Sync
 * now" button (plan.md Phase 3 item 3).
 *
 * plan.md Motion: a progress tick must not re-render the surrounding view. This component reads only
 * `useActivityBusy()` — a boolean — so it re-renders when a sync starts or ends, not on every byte;
 * the part that shows progress is `HeroStatus`, which alone subscribes to `useActivityCurrent()`.
 */
export function StatusHeader({ state, conflicts, games, onSynced }: Props) {
  const activityBusy = useActivityBusy()
  const [syncing, setSyncing] = useState(false)
  const [toast, setToast] = useState<{ text: string; failed: boolean } | null>(null)
  // A sync the tray, a game exit or the Deck's own UI started is not this component's own request,
  // but it is just as much "busy" — and a second press would only be told a sync is already running.
  const busy = syncing || activityBusy
  const summary = summarize(state, conflicts, games)
  const canSync = state?.connected === true

  async function syncAll() {
    setSyncing(true)
    setToast(null)
    try {
      const { message } = await api.syncNow()
      setToast({ text: message, failed: false })
    } catch (err) {
      setToast({ text: err instanceof Error ? err.message : 'Sync failed.', failed: true })
    } finally {
      setSyncing(false)
      refreshActivity()
      onSynced()
    }
  }

  return (
    <>
      <div className="sl-hero">
        <HeroStatus busy={busy} summary={summary} />
        <div className="sl-hero__right">
          {!busy && summary.chip}
          <Button
            variant="primary"
            onClick={() => void syncAll()}
            disabled={busy || !canSync}
            title={canSync ? undefined : 'Register this machine in Settings first.'}
          >
            <RefreshCw size={14} strokeWidth={2} aria-hidden="true" />
            {busy ? 'Syncing…' : 'Sync all'}
          </Button>
        </div>
      </div>
      {toast && (
        <Toast tone={toast.failed ? 'warn' : 'default'} onDismiss={() => setToast(null)}>
          {toast.text}
        </Toast>
      )}
    </>
  )
}

function HeroStatus({ busy, summary }: { busy: boolean; summary: Summary }) {
  const current = useActivityCurrent()

  if (!busy) {
    const { Icon } = summary
    return (
      <>
        <span className={`sl-hero__badge ${summary.tone === 'neutral' ? '' : `sl-hero__badge--${summary.tone}`}`.trim()}>
          <Icon size={20} strokeWidth={1.9} aria-hidden="true" />
        </span>
        <div className="sl-hero__main">
          <div className="sl-hero__title" role="status">{summary.title}</div>
          <div className="sl-hero__detail">{summary.detail}</div>
        </div>
      </>
    )
  }

  const phase = current?.phase ?? 'Idle'
  const game = current?.gameName
  const determinate = phase === 'Pushing' && (current?.bytesTotal ?? 0) > 0
  const pct = determinate ? Math.min(100, Math.round((current!.bytesDone / current!.bytesTotal) * 100)) : 0

  let title = 'Syncing…'
  if (game) {
    if (phase === 'Pushing') title = `Pushing ${game}…`
    else if (phase === 'Pulling') title = `Pulling ${game}…`
    else if (phase === 'Settling') title = `Waiting for ${game} to finish writing…`
  }

  return (
    <>
      <span className="sl-hero__badge">
        <RefreshCw size={20} strokeWidth={1.9} className="sl-spin" aria-hidden="true" />
      </span>
      <div className="sl-hero__main">
        <div className="sl-hero__title" role="status">{title}</div>
        <div
          className="sl-meter"
          role="progressbar"
          aria-label="Sync progress"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={determinate ? pct : undefined}
        >
          <i
            className={determinate ? undefined : 'sl-meter__sweep'}
            style={determinate ? { width: `${pct}%` } : undefined}
          />
        </div>
        {determinate && (
          <div className="sl-hero__legend">
            <span>{formatBytes(current!.bytesDone)} of {formatBytes(current!.bytesTotal)}</span>
            <span>{pct}%</span>
          </div>
        )}
      </div>
    </>
  )
}
