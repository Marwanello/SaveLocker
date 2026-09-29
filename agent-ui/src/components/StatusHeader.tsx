import { useState } from 'react'
import type { ReactNode } from 'react'
import { Check, GitBranch, RefreshCw, Unplug } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { api } from '../api'
import { formatAgo, formatBytes } from '../format'
import { refreshActivity, useActivityBusy, useActivityCurrent, useActivityLastRun } from '../useActivity'
import type { AgentState, Conflict, SyncRun, TrackedGame } from '../types'
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

const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`

/** The done state's sentence: what a finished (or cancelled) Sync all did. Counts are what really
 *  happened per game, never a guess: a game that was current is "already current", not "synced". */
function describeRun(run: SyncRun): { title: string; detail: string } {
  const parts: string[] = []
  if (run.uploaded > 0) parts.push(`${run.uploaded} uploaded`)
  if (run.alreadyCurrent > 0) parts.push(`${run.alreadyCurrent} already current`)
  if (run.conflicts > 0) parts.push(plural(run.conflicts, 'conflict'))
  if (run.failed > 0) parts.push(`${run.failed} failed — see Activity`)
  const detail = `${parts.join(', ') || 'nothing to do'} · ${formatAgo(run.finishedAtUtc)}`
  const sent = run.bytesSent > 0 ? ` — ${formatBytes(run.bytesSent)} sent` : ''
  return {
    title: run.cancelled
      ? `Sync cancelled after ${run.games} ${run.games === 1 ? 'game' : 'games'}${sent}`
      : `Synced ${plural(run.games, 'game')}${sent}`,
    detail,
  }
}

/** What the header says when nothing is syncing. Ordered by what a person most needs to know:
 *  a machine that cannot sync at all, then a decision waiting, then what the last run did, then
 *  "all clear". */
function summarize(
  state: AgentState | null, conflicts: Conflict[], games: TrackedGame[], lastRun: SyncRun | null | undefined,
): Summary {
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
  if (lastRun) {
    const { title, detail } = describeRun(lastRun)
    return { tone: 'ok', Icon: Check, title, detail, chip: <Chip tone="ok">All clear</Chip> }
  }
  const last = state.lastSyncAgo === '—' ? 'no push yet' : `last push ${state.lastSyncAgo}`
  const queue = state.offlineQueueCount > 0 ? ` · offline queue ${state.offlineQueueCount}` : ''
  return {
    tone: 'ok', Icon: Check, title: 'Idle — nothing syncing right now.',
    detail: `${state.machineName} · ${last} · settle ${state.settleQuietSeconds}s${queue}`,
    chip: <Chip tone="ok">All clear</Chip>,
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
  const lastRun = useActivityLastRun()
  const [syncing, setSyncing] = useState(false)
  const [toast, setToast] = useState<{ text: string; failed: boolean } | null>(null)
  // A sync the tray, a game exit or the Deck's own UI started is not this component's own request,
  // but it is just as much "busy" — and a second press would only be told a sync is already running.
  const busy = syncing || activityBusy
  const summary = summarize(state, conflicts, games, lastRun)
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
          {busy && <CancelButton />}
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

/** Only a Sync all is a run with games left to skip — a single game's sync, a launch or an exit push
 *  has nothing to cancel, so the button is not offered for them. Reads its own slice so a progress
 *  tick does not re-render the header around it. */
function CancelButton() {
  const current = useActivityCurrent()
  const [asked, setAsked] = useState(false)
  if (!current || current.total === 0) return null
  const stopping = asked || current.cancelRequested
  return (
    <Button
      size="sm"
      disabled={stopping}
      title="The game being synced right now finishes first, so no save is left half-written."
      onClick={() => { setAsked(true); void api.cancelSync().finally(refreshActivity) }}
    >
      {stopping ? 'Stopping after this game…' : 'Cancel'}
    </Button>
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
  const position = current && current.total > 0 ? `${current.index} of ${current.total}` : ''

  // "Syncing 2 of 6 — Hades II"; a single game's sync has no position and names its phase instead
  // ("Pushing Hades II…"). The legend below carries the phase for a run.
  let title = 'Syncing…'
  if (position) title = game ? `Syncing ${position} — ${game}` : `Syncing ${position}…`
  else if (game) {
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
        <div className="sl-hero__legend">
          <span>{phase === 'Idle' ? 'Between games' : phase}</span>
          {determinate && <span>{formatBytes(current!.bytesDone)} of {formatBytes(current!.bytesTotal)} sent</span>}
          {determinate && <span>{pct}%</span>}
        </div>
      </div>
    </>
  )
}
