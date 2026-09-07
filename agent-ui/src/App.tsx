import { useEffect, useState, useCallback } from 'react'
import { HardDrive } from 'lucide-react'
import type { View, AgentState, Conflict, TrackedGame } from './types'
import { api } from './api'
import { Sidebar } from './components/Sidebar'
import { StatusHeader } from './components/StatusHeader'
import { OverviewView } from './components/OverviewView'
import { AddGamesView } from './components/AddGamesView'
import { ConflictsView } from './components/ConflictsView'
import { SyncConflictModal } from './components/SyncConflictModal'
import { SettingsView } from './components/SettingsView'
import logoUrl from './assets/SaveLocker_Logo_crop.png'

export default function App() {
  // The tray's native Sync All / Force Pull / Force Push (TrayApp.cs, Phase 7) open this window at
  // "#conflicts:queue" rather than the plain "#conflicts" route when they find an open conflict —
  // the suffix is stripped for routing but remembered below to auto-open the same queue pop-up
  // OverviewView's own "Sync now" button already shows, so both hosts get one queue UI regardless of
  // which trigger point found the conflict.
  const initialHash = window.location.hash.slice(1)
  const [view, setView] = useState<View>(() => {
    const base = initialHash.split(':')[0] as View
    return (['overview', 'addGames', 'conflicts', 'settings'] as View[]).includes(base) ? base : 'addGames'
  })
  const [autoQueueRequested] = useState(() => initialHash === 'conflicts:queue')
  const [state, setState] = useState<AgentState | null>(null)
  const [conflicts, setConflicts] = useState<Conflict[]>([])
  const [games, setGames] = useState<TrackedGame[]>([])
  // Non-null only while the sync-time pop-up is up (Overview's "Sync now" surfaced at least one
  // conflict). Deliberately separate from `conflicts`: the passive 15s poll below must never open
  // this on its own — only an explicit Sync now does, so nothing interrupts the user unprompted.
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

  const handleSynced = useCallback(() => {
    // A "Sync now" request can still be in flight after the user has already navigated to the
    // Conflicts page themselves — it shows the same conflicts already, so popping the overlay on
    // top of it would only interrupt the user a second time for information they can already see.
    refreshConflicts().then(cs => { if (cs.length > 0 && view !== 'conflicts') setSyncQueue(cs) })
  }, [refreshConflicts, view])

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
    <div style={{
      height: '100vh',
      display: 'flex',
      flexDirection: 'column',
      background: '#0d1114',
      fontFamily: "'Inter', system-ui, -apple-system, sans-serif",
      overflow: 'hidden',
    }}>
        {/* Shared header row — one element, guaranteed alignment */}
        <div style={{ display: 'flex', borderBottom: '1px solid #494949', flexShrink: 0 }}>
          <div style={{
            width: 212, minWidth: 212, padding: '15px 14px',
            background: '#1E252A', borderRight: '1px solid #494949',
            display: 'flex', alignItems: 'center', gap: 10,
          }}>
            <img src={logoUrl} alt="SaveLocker" style={{ width: 34, height: 34, objectFit: 'contain', borderRadius: 5, flexShrink: 0 }} />
            <div>
              <div style={{ color: '#ECEFF1', fontSize: 13, fontWeight: 700, letterSpacing: '-0.015em', lineHeight: 1.2 }}>SaveLocker</div>
              {/* buildLabel, not currentVersion: several builds share one version number, and on a
                  machine running a test build beside the installed one that is the whole question.
                  Case is left alone here — a commit hash in caps reads as a different string. */}
              <div style={{ color: '#9CA3AF', fontSize: 10, letterSpacing: '0.07em', lineHeight: 1.5 }}>AGENT v{state?.buildLabel ?? state?.currentVersion ?? '…'}</div>
            </div>
          </div>
          <StatusHeader connected={state?.connected ?? false} serverUrl={state?.serverUrl ?? ''} />
        </div>

        {/* Main row: sidebar nav + content */}
        <div style={{ flex: 1, display: 'flex', overflow: 'hidden' }}>
          <Sidebar activeView={view} onNavigate={setView} conflictCount={conflicts.length} />

          <div style={{ flex: 1, minWidth: 0, background: '#2A3238', position: 'relative', overflow: 'hidden' }}>
            {view === 'overview' && <OverviewView state={state} onWarningDismissed={refreshState} onSynced={handleSynced} />}
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
          </div>
        </div>

        {/* Shared footer row — one element, guaranteed alignment */}
        <div style={{ display: 'flex', borderTop: '1px solid #494949', flexShrink: 0 }}>
          <div style={{
            width: 212, minWidth: 212, padding: '11px 14px',
            background: '#1E252A', borderRight: '1px solid #494949',
            display: 'flex', alignItems: 'center', gap: 7,
          }}>
            <HardDrive size={12} strokeWidth={1.75} color="#9CA3AF" />
            <span style={{ color: '#9CA3AF', fontSize: 11 }}>Machine: {state?.machineName ?? '…'}</span>
          </div>
          <div style={{ flex: 1, background: '#2A3238' }} />
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
