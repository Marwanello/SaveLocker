import type { ReactNode } from 'react'
import type { AgentState, Conflict, LeaseWarning, TrackedGame, View } from '../types'
import { api } from '../api'
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

  async function dismiss(w: LeaseWarning) {
    try { await api.dismissLeaseWarning(w.gameName) } catch { /* ignore */ }
    onWarningDismissed()
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

  return (
    <div className="sl-page">
      <div className="sl-grid3">
        <Stat label="Tracked here" value={tracked ?? '…'} context="games on this machine" />
        <Stat label="Saves backed up" value={state?.savesBacked ?? '…'} context="new versions pushed, in total" />
        <Stat label="Last sync" value={state?.lastSyncAgo ?? '—'} context="last push or pull" />
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
            <dt>Server</dt>
            <dd>{state?.serverUrl ? state.serverUrl.replace(/^https?:\/\//, '') : '—'}</dd>
          </dl>
        </Card>
        <RecentCard />
      </div>
    </div>
  )
}
