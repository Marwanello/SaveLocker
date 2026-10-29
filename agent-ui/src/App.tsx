import { useEffect, useRef, useState, useCallback } from 'react'
import type { View, AgentState, AgentAppearance, Conflict, EmptiedHold, FolderSuggestion, TrackedGame } from './types'
import { api } from './api'
import { Sidebar } from './components/Sidebar'
import { StatusHeader } from './components/StatusHeader'
import { OverviewView } from './components/OverviewView'
import { GamesView } from './components/GamesView'
import { GameDetailView } from './components/GameDetailView'
import { titleOf } from './titles'
import { AddGamesView } from './components/AddGamesView'
import { ConflictsView } from './components/ConflictsView'
import { SyncConflictModal } from './components/SyncConflictModal'
import { FolderSuggestionsModal } from './components/FolderSuggestionsModal'
import { SettingsView } from './components/SettingsView'
import { ActivityView } from './components/ActivityView'
import { Chip } from './components/ui/Chip'
import { Toast } from './components/ui/Toast'
import { Mark } from './components/ui/Mark'
import { PadLayer } from './gamepad/PadLayer'
import { unseenWarnings } from './activitySeen'
import { inAppWindow } from './appWindow'
import { isCurrentPoll, looksEpoch, setLook } from './appearance'
import { clearRouteHash, parseRoute } from './route'
import { useActivityRecent } from './useActivity'

/** A `#enroll:` value may be a URI-encoded join link or a bare code. Decoding is best-effort: a
 *  value with no escapes is already what we want, and a malformed escape is kept as typed. */
function decodeJoinValue(raw: string): string {
  try { return decodeURIComponent(raw) } catch { return raw }
}

export default function App() {
  // The tray's native Sync All / Force Pull / Force Push (TrayApp.cs, Phase 7) open this window at
  // "#conflicts:queue" rather than the plain "#conflicts" route when they find an open conflict —
  // the suffix is stripped for routing but remembered below to auto-open the same queue pop-up
  // the status header's own Sync all already shows, so both hosts get one queue UI regardless of
  // which trigger point found the conflict. A notification's button does the same, and adds
  // "#game:<id>" — see route.ts, which is also what the hashchange listener below reads.
  const [initialRoute] = useState(() => parseRoute(window.location.hash))
  const [view, setView] = useState<View>(initialRoute.view)
  const [autoQueueRequested] = useState(initialRoute.queue)
  const [openGameId, setOpenGameId] = useState<string | null>(initialRoute.gameId)
  // The pairing page's "Open the agent and finish" button lands here (#enroll:<link>); the Join card
  // reads it, and clears it once the machine is enrolled. Decoded because a link in a hash carries
  // its own colons and slashes — the pairing page URI-encodes it.
  const [pendingJoin, setPendingJoin] = useState<string | null>(
    initialRoute.join != null ? decodeJoinValue(initialRoute.join) : null)
  const [state, setState] = useState<AgentState | null>(null)
  const [appearance, setAppearance] = useState<AgentAppearance | null>(null)
  const [conflicts, setConflicts] = useState<Conflict[]>([])
  // Games held here because their save folder was emptied: answered beside conflicts, counted with them.
  const [emptied, setEmptied] = useState<EmptiedHold[]>([])
  const [games, setGames] = useState<TrackedGame[]>([])
  // False only until the first games request settles (either way) — Games must not read an unset
  // list as "nothing is tracked" and offer Add games while the real answer is still in flight.
  const [gamesLoaded, setGamesLoaded] = useState(false)
  // Non-null only while the sync-time pop-up is up (a Sync all surfaced at least one conflict).
  // Deliberately separate from `conflicts`: the passive 15s poll below must never open this on its
  // own — only an explicit Sync all does, so nothing interrupts the user unprompted.
  const [syncQueue, setSyncQueue] = useState<Conflict[] | null>(null)
  // What became of the save here after a conflict choice: the pop-up closes on it, so it is said here.
  const [resolveNote, setResolveNote] = useState<string | null>(null)
  // "More save folders found": folders the manifest names for tracked games that nobody has answered
  // for yet. Asked once when the window opens (and when a notification links here), never by a poll.
  const [folderQueue, setFolderQueue] = useState<FolderSuggestion[] | null>(null)
  const askAboutFolders = useCallback(() => {
    api.folderSuggestions()
      .then(all => {
        const open = all.filter(s => !s.deferred)
        if (open.length > 0) setFolderQueue(q => q ?? open)
      })
      .catch(() => {})
  }, [])

  // How many games the LAST scan suggested — read from the agent's cache, never triggering a scan, so
  // the sidebar count costs nothing and navigating never walks the disk. Null until one has run.
  const [suggested, setSuggested] = useState<number | null>(null)
  const recent = useActivityRecent()

  const refreshState = useCallback(() => {
    api.state().then(setState).catch(console.error)
    api.cachedCandidates().then(c => setSuggested(c.suggested)).catch(() => {})
  }, [])

  // What this window looks like: the console's look or this machine's own (Settings > Appearance). Adopted
  // through setLook, which paints it and is a no-op while it is unchanged, so polling costs nothing.
  const adoptAppearance = useCallback((a: AgentAppearance) => {
    setAppearance(a)
    setLook(a.effective)
  }, [])
  // A poll that left before a save made from this window, or was still out while one ran, answers with the
  // state from before it: adopting that would undo the save until the next poll. Dropped instead.
  const refreshAppearance = useCallback(() => {
    const epoch = looksEpoch()
    api.appearance().then(a => { if (isCurrentPoll(epoch)) adoptAppearance(a) }).catch(console.error)
  }, [adoptAppearance])

  const refreshConflicts = useCallback(async (): Promise<Conflict[]> => {
    // Separate requests, not Promise.all: the game list is this machine's own config, but conflicts come
    // from the server. With the server down the pair used to fail together and the list never loaded.
    const [cs, gs, eh] = await Promise.allSettled([api.conflicts(), api.games(), api.emptiedHolds()])
    if (gs.status === 'fulfilled') setGames(gs.value)
    else console.error(gs.reason)
    setGamesLoaded(true)
    if (eh.status === 'fulfilled') setEmptied(eh.value)
    if (cs.status === 'fulfilled') {
      setConflicts(cs.value)
      return cs.value
    }
    console.error(cs.reason)
    return []
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
    refreshAppearance()
    const id = setInterval(refreshAppearance, 10_000)
    return () => clearInterval(id)
  }, [refreshAppearance])

  useEffect(() => {
    refreshConflicts()
    const id = setInterval(refreshConflicts, 15_000)
    return () => clearInterval(id)
  }, [refreshConflicts])

  // The route above is read in a state initializer, which StrictMode runs twice — so the hash is
  // consumed here, after both reads, rather than there. See clearRouteHash for why it goes at all.
  useEffect(() => { clearRouteHash() }, [])

  // Once per window: a conflict the user asked to see goes first, so the folder prompt waits for it.
  useEffect(() => {
    if (!autoQueueRequested) askAboutFolders()
  }, [autoQueueRequested, askAboutFolders])

  // Runs once, only when a native tray action opened this window looking for a conflict to show.
  // The passive 15s poll above must never do this on its own — see handleSynced's own comment.
  useEffect(() => {
    if (!autoQueueRequested) return
    refreshConflicts().then(cs => { if (cs.length > 0) setSyncQueue(cs) })
  }, [autoQueueRequested, refreshConflicts])

  // The same routes, asked for while the window is already open. Without this the hash was read only
  // once at load, so the tray's window — which is kept and re-shown rather than rebuilt — ignored
  // every deep link after the first, and a notification's button would have opened the app to
  // wherever it had last been. Like the effect above, only an explicit request raises the pop-up.
  useEffect(() => {
    const onHash = () => {
      const route = parseRoute(window.location.hash)
      clearRouteHash()
      setView(route.view)
      setOpenGameId(route.gameId)
      if (route.join != null) setPendingJoin(decodeJoinValue(route.join))
      if (route.queue) refreshConflicts().then(cs => { if (cs.length > 0) setSyncQueue(cs) })
      if (route.folders) askAboutFolders()
    }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [refreshConflicts, askAboutFolders])

  return (
    <div className="sl-app">
        <header className="sl-topbar">
          <div className="sl-brand">
            <Mark size={30} />
            <div>
              <div className="sl-brand__name">SaveLocker</div>
              <div className="sl-brand__sub">
                {inAppWindow && state?.machineName ? `${state.machineName} — desktop session` : 'Agent'}
              </div>
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
            counts={{
              games: state?.gamesTracked,
              addGames: suggested ?? 0,
              conflicts: conflicts.length + emptied.length,
              // Being on the page is what clears it; leaving starts the count again from now.
              activity: view === 'activity' ? 0 : unseenWarnings(recent),
            }}
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
                    emptiedHold={emptied.find(h => h.gameId === open.id) ?? null}
                    machineName={state?.machineName ?? ''}
                    platform={state?.platform}
                    onBack={() => setOpenGameId(null)}
                    onNavigate={navigate}
                    onSynced={() => handleGameSynced(open.id)}
                    onChanged={() => { refreshState(); void refreshConflicts() }}
                    onRemoved={() => setOpenGameId(null)}
                    trees={titleOf(open, games).trees}
                    onPickTree={setOpenGameId}
                    onNote={setResolveNote}
                  />
                )
                : <GamesView games={games} loaded={gamesLoaded} conflicts={conflicts} onOpen={setOpenGameId} onNavigate={navigate} />
            })()}
            {view === 'addGames' && <AddGamesView onEnrolled={refreshState} />}
            {view === 'conflicts' && (
              <ConflictsView
                conflicts={conflicts}
                emptied={emptied}
                games={games}
                machineName={state?.machineName ?? ''}
                onRefresh={refreshConflicts}
                onResolvedNote={setResolveNote}
              />
            )}
            {view === 'activity' && <ActivityView />}
            {view === 'settings' && (
              <SettingsView
                state={state}
                onSaved={refreshState}
                appearance={appearance}
                onAppearanceChanged={adoptAppearance}
                onOpenGame={id => { setOpenGameId(id); setView('games') }}
                pendingJoin={pendingJoin}
                onJoinHandled={() => setPendingJoin(null)}
              />
            )}
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
            onResolvedNote={setResolveNote}
            onAllDone={() => setSyncQueue(null)}
            onLater={() => { setSyncQueue(null); setView('conflicts') }}
          />
        )}
        {resolveNote && <Toast dwellMs={8000} onDismiss={() => setResolveNote(null)}>{resolveNote}</Toast>}
        {folderQueue && !syncQueue && (
          <FolderSuggestionsModal
            suggestions={folderQueue}
            onChanged={() => { refreshState(); void refreshConflicts() }}
            onDone={() => setFolderQueue(null)}
          />
        )}

        {/* Controller support: input, spatial navigation, the hint bar and the on-screen keyboard.
            Inert on a machine with no gamepad — nothing renders and nothing changes. */}
        <PadLayer />
    </div>
  )
}
