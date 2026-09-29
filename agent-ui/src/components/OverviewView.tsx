import { useState } from 'react'
import type { ReactNode } from 'react'
import type { AgentState, Conflict, LeaseWarning, TrackedGame, View } from '../types'
import { api } from '../api'
import { formatBytes } from '../format'
import { RecentCard } from './RecentCard'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Stat } from './ui/Stat'

interface Props {
  state: AgentState | null
  conflicts: Conflict[]
  games: TrackedGame[]
  onWarningDismissed: () => void
  onNavigate: (v: View) => void
}

/**
 * Quick info only (plan.md Phase 5): three stats, one status banner, what happens next, and the
 * last three events. Sync all and its progress live in the status header on every page, not here.
 * The launch-setup and plugin cards that used to fill this page are in Settings, where the
 * prototype puts them; the full rolling log expands inside "Recent".
 */
export function OverviewView({ state, conflicts, games, onWarningDismissed, onNavigate }: Props) {
  const warnings = state?.leaseWarnings ?? []
  const [rescanning, setRescanning] = useState(false)
  const [rescanNote, setRescanNote] = useState<string | null>(null)

  async function dismiss(w: LeaseWarning) {
    try { await api.dismissLeaseWarning(w.gameName) } catch { /* ignore */ }
    onWarningDismissed()
  }

  // Rescan is an explicit request — the one place this page walks the disk — and reports what it found.
  async function rescan() {
    setRescanning(true)
    setRescanNote(null)
    try {
      const found = await api.rescan()
      const suggested = found.filter(c => !c.hasSteamCloud).length
      setRescanNote(`Found ${found.length} game${found.length === 1 ? '' : 's'}, ${suggested} suggested.`)
      onWarningDismissed() // refreshes the sidebar's suggestion count along with the state
    } catch (err) {
      setRescanNote(err instanceof Error ? err.message : 'The scan failed.')
    } finally {
      setRescanning(false)
    }
  }

  const banners: ReactNode[] = []
  if (state && !state.connected) {
    banners.push(
      <Banner
        key="setup" tone="crit"
        title="This machine is not connected to a server"
        detail="Register it in Settings, then Sync all pulls the latest save of every tracked game."
        action={<Button variant="primary" onClick={() => onNavigate('settings')}>Open Settings</Button>}
      />,
    )
  } else if (conflicts.length > 0) {
    const first = games.find(g => g.id === conflicts[0].gameId)?.name ?? 'A game'
    banners.push(
      <Banner
        key="conflicts" tone="crit"
        title={conflicts.length === 1 ? `${first} is waiting on you` : `${conflicts.length} games are waiting on you`}
        detail="Open Conflicts to keep this device's save or the cloud's."
        action={<Button variant="primary" onClick={() => onNavigate('conflicts')}>Choose</Button>}
      />,
    )
  }
  for (const w of warnings) {
    banners.push(
      <Banner
        key={`lease-${w.gameName}`} tone="warn"
        title={`Save conflict risk — ${w.gameName}`}
        detail={`${w.holderMachine} already has this game checked out. You launched without pulling their latest save, so a conflict will likely appear when you exit.`}
        action={<Button size="sm" variant="quiet" onClick={() => void dismiss(w)}>Dismiss</Button>}
      />,
    )
  }
  if (state && state.offlineQueueCount > 0) {
    banners.push(
      <Banner
        key="queue" tone="warn"
        title={`${state.offlineQueueCount} save${state.offlineQueueCount === 1 ? '' : 's'} waiting to upload`}
        detail="The server could not be reached. They are kept safely and sent as soon as the connection is back."
        action={<Button size="sm" onClick={() => onNavigate('activity')}>See queue</Button>}
      />,
    )
  }
  if (state?.connected && banners.length === 0) {
    banners.push(
      <Banner
        key="clear" tone="ok"
        title="Nothing needs you."
        detail="No conflicts waiting, and no other machine is holding one of your games."
      />,
    )
  }

  const tracked = state?.gamesTracked
  const queued = state?.offlineQueueCount ?? 0

  return (
    <div className="sl-page">
      <div className="sl-grid3">
        <Stat label="Tracked here" value={tracked ?? '…'} context="games on this machine" />
        <Stat label="Saves backed up" value={state?.savesBacked ?? '…'} context="new versions pushed, in total" />
        <Stat
          label="Sent today"
          value={state ? formatBytes(state.sentTodayBytes) : '…'}
          context={`last sync ${state?.lastSyncAgo ?? '—'}`}
        />
      </div>

      {banners}

      <div className="sl-grid2">
        <Card
          title="Next up"
          headerRight={<Button size="sm" onClick={() => onNavigate('addGames')}>Add games</Button>}
        >
          <dl className="sl-kv">
            <dt>Watching</dt>
            <dd>{tracked === undefined ? '…' : `${tracked} save folder${tracked === 1 ? '' : 's'}`}</dd>
            <dt>On quit</dt>
            <dd>
              {state === null ? '…'
                : state.settleQuietSeconds > 0
                  ? `Push after ${state.settleQuietSeconds} seconds of quiet`
                  : 'Push straight away'}
            </dd>
            <dt>Queue</dt>
            <dd>{state === null ? '…' : queued === 0 ? 'Nothing waiting to upload' : `${queued} waiting for the server`}</dd>
            <dt>Server</dt>
            <dd>{state?.serverUrl ? state.serverUrl.replace(/^https?:\/\//, '') : '—'}</dd>
          </dl>
          <div className="sl-inline" style={{ marginTop: 14 }}>
            <Button size="sm" disabled={rescanning} onClick={() => void rescan()}>
              {rescanning ? 'Scanning…' : 'Rescan library'}
            </Button>
            {rescanNote && <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>{rescanNote}</span>}
          </div>
        </Card>
        <RecentCard />
      </div>
    </div>
  )
}
