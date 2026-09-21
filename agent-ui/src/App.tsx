import { useEffect, useRef, useState, useCallback } from 'react'
import type { View, AgentState, Conflict, TrackedGame } from './types'
import { api } from './api'
import { Sidebar } from './components/Sidebar'
import { StatusHeader } from './components/StatusHeader'
import { OverviewView } from './components/OverviewView'
import { GamesView } from './components/GamesView'
import { GameDetailView } from './components/GameDetailView'
import { AddGamesView } from './components/AddGamesView'
import { ConflictsView } from './components/ConflictsView'
import { SyncConflictModal } from './components/SyncConflictModal'
import { SettingsView } from './components/SettingsView'
import { Chip } from './components/ui/Chip'
import logoUrl from './assets/SaveLocker_Logo_crop.png'

export default function App() {
  // The tray's native Sync All / Force Pull / Force Push (TrayApp.cs, Phase 7) open this window at
  // "#conflicts:queue" rather than the plain "#conflicts" route when they find an open conflict —
  // the suffix is stripped for routing but remembered below to auto-open the same queue pop-up
  // the status header's own Sync all already shows, so both hosts get one queue UI regardless of
  // which trigger point found the conflict.
  const initialHash = window.location.hash.slice(1)
  const [view, setView] = useState<View>(() => {
    const base = initialHash.split(':')[0] as View
    return (['overview', 'games', 'addGames', 'conflicts', 'settings'] as View[]).includes(base) ? base : 'games'
  })
  const [autoQueueRequested] = useState(() => initialHash === 'conflicts:queue')
  const [openGameId, setOpenGameId] = useState<string | null>(null)
  const [state, setState] = useState<AgentState | null>(null)
  const [conflicts, setConflicts] = useState<Conflict[]>([])
  const [games, setGames] = useState<TrackedGame[]>([])
  // Non-null only while the sync-time pop-up is up (a Sync all surfaced at least one conflict).
  // Deliberately separate from `conflicts`: the passive 15s poll below must never open this on its
  // own — only an explicit Sync all does, so nothing interrupts the user unprompted.
  const [syncQueue, setSyncQueue] = useState<Conflict[] | null>(null)

  const refreshState = useCallback(() => {
    api.state().then(setState).catch(console.error)
  }, [])

  const refreshConflicts = useCallback(async (): Promise<Conflict[]> => {
    try {
      const [cs, gs] = await Promise.all([api.conflicts(), api.games()])
      setConflicts(cs)
      setGames(gs)
      return cs
    } catch (err) {
      console.error(err)
      return []
    }
  }, [])

  // Read through a ref: Sync all is a long request, and `handleSynced` runs when it FINISHES. A `view`
  // captured by the closure would be the page the user was on when they pressed it, so the guard below
  // could never notice they had since gone to Conflicts. The button is in the header on every page now,
  // which makes navigating away mid-sync the normal case rather than the edge one.
  const viewRef = useRef(view)
  useEffect(() => { viewRef.current = view })

  const handleSynced = useCallback(() => {
    // A Sync all request can still be in flight after the user has already navigated to the
    // Conflicts page themselves — it shows the same conflicts already, so popping the overlay on
    // top of it would only interrupt the user a second time for information they can already see.
    refreshConflicts().then(cs => { if (cs.length > 0 && viewRef.current !== 'conflicts') setSyncQueue(cs) })
  }, [refreshConflicts])

  // A per-game sync (the game page's buttons) only interrupts for a conflict on THAT game: the pop-up
  // queues every conflict it is handed, and another game's old one is not what this press was about.
  const handleGameSynced = useCallback((gameId: string) => {
    refreshState()
    refreshConflicts().then(cs => {
      const mine = cs.filter(c => c.gameId === gameId)
      if (mine.length > 0 && viewRef.current !== 'conflicts') setSyncQueue(mine)
    })
  }, [refreshState, refreshConflicts])

  const navigate = useCallback((v: View) => {
    // Choosing Games in the sidebar always lands on the list, even from inside a game.
    if (v === 'games') setOpenGameId(null)
    setView(v)
  }, [])

  useEffect(() => {
    refreshState()
    const id = setInterval(refreshState, 10_000)
    return () => clearInterval(id)
  }, [refreshState])

  useEffect(() => {
    refreshConflicts()
    const id = setInterval(refreshConflicts, 15_000)
    return () => clearInterval(id)
  }, [refreshConflicts])

  // Runs once, only when a native tray action opened this window looking for a conflict to show.
  // The passive 15s poll above must never do this on its own — see handleSynced's own comment.
  useEffect(() => {
    if (!autoQueueRequested) return
    refreshConflicts().then(cs => { if (cs.length > 0) setSyncQueue(cs) })
  }, [autoQueueRequested, refreshConflicts])

  return (
    <div className="sl-app">
        <header className="sl-topbar">
          <div className="sl-brand">
            <img src={logoUrl} alt="" />
            <div>
              <div className="sl-brand__name">SaveLocker</div>
              <div className="sl-brand__sub">Agent</div>
            </div>
          </div>
          <div className="sl-topbar__tools">
            {state && (state.connected
              ? <Chip tone="ok">Connected</Chip>
              : <Chip tone="crit">Not connected</Chip>)}
          </div>
        </header>

        {/* On every page, because Sync all is not an Overview thing. It re-renders when a sync starts
            or ends, never on a progress tick — see StatusHeader. */}
        <StatusHeader
          state={state}
          conflicts={conflicts}
          games={games}
          onSynced={() => { refreshState(); handleSynced() }}
        />

        <div className="sl-body">
          <Sidebar
            activeView={view}
            onNavigate={navigate}
            conflictCount={conflicts.length}
            agentLabel={state?.buildLabel ?? state?.currentVersion ?? '…'}
            machineName={state?.machineName ?? ''}
            serverHost={(state?.serverUrl ?? '').replace(/^https?:\/\//, '')}
          />

          <main className="sl-content">
            {view === 'overview' && (
              <OverviewView
                state={state}
                conflicts={conflicts}
                games={games}
                onWarningDismissed={refreshState}
                onNavigate={navigate}
              />
            )}
            {view === 'games' && (() => {
              const open = games.find(g => g.id === openGameId)
              return open
                ? (
                  <GameDetailView
                    key={open.id}
                    game={open}
                    conflicts={conflicts}
                    machineName={state?.machineName ?? ''}
                    onBack={() => setOpenGameId(null)}
                    onNavigate={navigate}
                    onSynced={() => handleGameSynced(open.id)}
                  />
                )
                : <GamesView games={games} conflicts={conflicts} onOpen={setOpenGameId} onNavigate={navigate} />
            })()}
            {view === 'addGames' && <AddGamesView onEnrolled={refreshState} />}
            {view === 'conflicts' && (
              <ConflictsView
                conflicts={conflicts}
                games={games}
                machineName={state?.machineName ?? ''}
                onRefresh={refreshConflicts}
              />
            )}
            {view === 'settings' && <SettingsView state={state} onSaved={refreshState} />}
          </main>
        </div>

        {/* position: fixed — a true overlay over the whole shell, sidebar included, not scoped to
            the content pane. Only resolving a side or "Decide later" dismisses it. */}
        {syncQueue && (
          <SyncConflictModal
            queue={syncQueue}
            games={games}
            machineName={state?.machineName ?? ''}
            onResolved={refreshConflicts}
            onAllDone={() => setSyncQueue(null)}
            onLater={() => { setSyncQueue(null); setView('conflicts') }}
          />
        )}
    </div>
  )
}
