import { useCallback, useEffect, useRef, useState } from 'react'
import { SlidersHorizontal } from 'lucide-react'
import type { Conflict, DlcCheck, EmptiedHold, GameState, GameSyncMode, SaveVersion, SyncStatus, TrackedGame, View } from '../../src/types'
import { api } from '../../src/api'
import { formatAgo, formatBytes, formatDateTime } from '../../src/format'
import { refreshActivity, useActivityBusy, useFilesChangedAt } from '../../src/useActivity'
import { PadGlyph } from '../../src/gamepad/PadGlyph'
import { GameArt } from '../../src/components/GameArt'
import { GameManagement } from '../../src/components/GameManagement'
import { SaveFilesCard } from '../../src/components/SaveFilesCard'
import { EmulatorSettingsView, type EmulatorTab } from '../../src/components/EmulatorSettingsView'
import { emulatorSettingsFor } from '../../src/emulators'
import { ReleaseChip } from '../../src/components/ReleaseChip'
import { SourceChip } from '../../src/components/SourceChip'
import { VersionsCard } from './VersionsCard'
import { EmptiedHoldBanner } from '../../src/components/EmptiedHoldBanner'
import { Banner } from '../../src/components/ui/Banner'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { Stat } from '../../src/components/ui/Stat'
import { Toast } from '../../src/components/ui/Toast'
import { DEMO_TREES, HUNTERS_NAME, HUNTERS_TREES, enterTree, ptById, useMock, useVariant } from '../store'
import { EmulatorTabs } from '../features/emulatorTabs'
import { PlaythroughChipB, PlaythroughTabsC, PlaythroughsCardA } from '../features/playthroughs'

interface Props {
  game: TrackedGame
  conflicts: Conflict[]
  /** Set while the game is held because every save file vanished here (tasks/empty-save-hold). */
  emptiedHold: EmptiedHold | null
  machineName: string
  platform: string | undefined
  onBack: () => void
  onNavigate: (v: View) => void
  /** Called when a sync this page started has finished, so the shell can refresh and, if it turned up
   *  a conflict, raise the same pop-up Sync all does. */
  onSynced: () => void
  /** The game's folder or process names changed — the shell re-reads the list. */
  onChanged: () => void
  /** The game was removed from this device: the shell leaves its page. */
  onRemoved: () => void
}

const onOff = (v: boolean | null | undefined, unset: string) => (v == null ? unset : v ? 'On' : 'Off')

const POLICY: Record<string, string> = {
  Manual: 'Ask me',
  NewestWins: 'Newest wins',
  PreferMachine: 'Prefer one machine',
}

/**
 * One tracked game: what the server holds, what this machine watches, and Sync / Push now / Pull
 * latest. The server's side is one small request (`/state`) made when the page opens. The save
 * folder is hashed on "Check now" and by the Save files card (on open, after a sync, on Refresh) —
 * both walk every file, so neither runs on a timer.
 */
export function GameDetailView({ game, conflicts, emptiedHold, machineName, platform, onBack, onNavigate, onSynced, onChanged, onRemoved }: Props) {
  const busy = useActivityBusy()
  const [state, setState] = useState<GameState | null>(null)
  const [versions, setVersions] = useState<SaveVersion[] | null>(null)
  const [localBytes, setLocalBytes] = useState<number | null>(null)
  // Bumped after every sync this page runs so the versions list follows what that sync just did.
  const [syncCount, setSyncCount] = useState(0)
  const [stateError, setStateError] = useState<string | null>(null)
  const [pending, setPending] = useState<GameSyncMode | 'check' | null>(null)
  const [check, setCheck] = useState<SyncStatus | null>(null)
  const [toast, setToast] = useState<{ text: string; failed: boolean } | null>(null)
  // The "Same game as …" banner's Stop tracking, asked once more before it acts.
  const [stopCopy, setStopCopy] = useState(false)

  async function stopTrackingCopy() {
    try {
      await api.removeGame(game.id)
      onChanged()
      onRemoved()
    } catch (err) {
      setStopCopy(false)
      setToast({ text: `Could not stop tracking: ${err instanceof Error ? err.message : String(err)}`, failed: true })
    }
  }
  // Bumped when a pull from this page was held by the DLC check, so the card opens its choice.
  const [dlcAsk, setDlcAsk] = useState(0)
  // Emulator settings, open on one of its sections; null is the game's own page.
  const [emuTab, setEmuTab] = useState<EmulatorTab | null>(null)
  const [dlc, setDlc] = useState<DlcCheck | null>(null)
  const emulator = emulatorSettingsFor(game)
  // Mockup: the planned playthrough switcher on the demo game, in the variant on show.
  const feature = useMock(s => s.feature)
  const combined = useMock(s => s.combined)
  const ptVariant = useVariant('pt-switch')
  const selectedPt = useMock(s => s.selected)
  const [viewingPt, setViewingPt] = useState<string | null>(null)
  const demo = DEMO_TREES.includes(game.id)
  const treeId = useMock(s => s.treeId)
  useEffect(() => { if (demo && treeId !== game.id) enterTree(game.id) }, [demo, treeId, game.id])
  useEffect(() => { setViewingPt(null) }, [game.id])
  // Metroid Prime - Hunters: one title, a save tree per emulator (multi-emulator-games). Its trees are two games here.
  const titled = HUNTERS_TREES.includes(game.id)
  const [trees, setTrees] = useState<TrackedGame[]>([])
  useEffect(() => { if (titled) void api.games().then(gs => setTrees(gs.filter(g => HUNTERS_TREES.includes(g.id)))) }, [titled])
  const treeName = titled ? game.source?.detail ?? null : null
  const ptFeature = demo && (combined || feature.startsWith('pt-'))
  const ptUi = demo ? ptVariant : null
  const historyFeature = demo && (combined || feature.startsWith('vh-'))

  const load = useCallback(() => {
    api.gameState(game.id)
      .then(s => { setState(s); setStateError(null) })
      .catch(err => setStateError(err instanceof Error ? err.message : 'Could not reach the server.'))
  }, [game.id])

  useEffect(() => { setState(null); setCheck(null); setEmuTab(null); load() }, [load])

  // The agent's word that this game's files here changed, or that a push, pull or hold ran for it from anywhere (the
  // folder watcher, Sync all, the launch gate, another surface): everything a sync from this page refreshes follows.
  const changedAt = useFilesChangedAt(game.id)
  const seenChange = useRef(changedAt)
  useEffect(() => {
    if (changedAt === seenChange.current) return
    seenChange.current = changedAt
    setSyncCount(n => n + 1)
    load()
  }, [changedAt, load])

  // What the page needs to point at Emulator settings: packs missing here, and other machines asking for this one's.
  useEffect(() => {
    if (emulator !== 'xenia' || !game.path) { setDlc(null); return }
    let live = true
    api.gameDlc(game.id).then(d => { if (live) setDlc(d) }).catch(() => { if (live) setDlc(null) })
    return () => { live = false }
  }, [game.id, game.path, emulator, syncCount])

  // A plain directory walk on the agent (no hashing), so asking on open — and after a sync — is fine.
  // Never on a timer: the point is what is here now, not a live gauge.
  useEffect(() => {
    let live = true
    api.localSize(game.id).then(s => { if (live) setLocalBytes(s.bytes) }).catch(() => { if (live) setLocalBytes(null) })
    return () => { live = false }
  }, [game.id, syncCount, game.path])

  const inConflict = conflicts.some(c => c.gameId === game.id)
  // Without a save folder here there is nothing to push, pull or compare — the game only exists on the
  // server until one is chosen (Games groups it apart for the same reason).
  const enrolled = !!game.path

  async function run(mode: GameSyncMode) {
    setPending(mode)
    setToast(null)
    setCheck(null)
    try {
      const { message } = await api.syncGame(game.id, mode)
      setToast({ text: message, failed: false })
      if (mode !== 'push') {
        const dlc = await api.gameDlc(game.id).catch(() => null)
        if (dlc?.held && dlc.missingCount > 0) { setEmuTab('dlc'); setDlcAsk(n => n + 1) }
      }
    } catch (err) {
      setToast({ text: err instanceof Error ? err.message : 'Sync failed.', failed: true })
    } finally {
      setPending(null)
      setSyncCount(n => n + 1)
      refreshActivity()
      load()
      onSynced()
    }
  }

  async function checkNow() {
    setPending('check')
    setToast(null)
    try { setCheck(await api.syncStatus(game.id)) }
    catch (err) { setToast({ text: err instanceof Error ? err.message : 'Check failed.', failed: true }) }
    finally { setPending(null) }
  }

  // Another sync (Sync all, the tray, a game exiting) holds the agent's single gate; a press now
  // would only be told so.
  const locked = busy || pending !== null
  const head = state?.head ?? null
  const holder = state?.lease?.holderMachineName ?? null
  const heldElsewhere = holder !== null && holder !== machineName
  const dlcMissing = dlc?.packages?.filter(p => !p.here).length ?? 0
  const dlcAsking = dlc?.asked?.filter(t => t.status === 'Requested') ?? []
  const emuCount = (dlcMissing > 0 ? 1 : 0) + dlcAsking.length

  const toastView = toast && (
    <Toast tone={toast.failed ? 'warn' : 'default'} onDismiss={() => setToast(null)}>{toast.text}</Toast>
  )

  if (emulator && emuTab) {
    return (
      <>
        <EmulatorSettingsView
          game={game} emulator={emulator} machineName={machineName} tab={emuTab}
          onTab={t => { setDlcAsk(0); setEmuTab(t) }} onBack={() => { setDlcAsk(0); setEmuTab(null) }}
          dlc={dlc} refreshKey={syncCount} askSignal={dlcAsk} onChanged={onChanged}
          onPulled={text => { setToast({ text, failed: false }); setSyncCount(n => n + 1); load(); onSynced() }}
        />
        {toastView}
      </>
    )
  }

  return (
    <div className="sl-page">
      <Button size="sm" variant="quiet" className="sl-back" onClick={onBack}>← Games</Button>

      <div className="sl-gamehead">
        <div className="sl-gamehead__cover"><GameArt id={game.id} name={game.name} kind="grid" w={192} console={game.release?.console} /></div>
        <div className="sl-gamehead__main">
          <h2 className="sl-gamehead__title">{titled ? HUNTERS_NAME : game.name}</h2>
          {(enrolled || game.release) && (
            <div className="sl-gamehead__source">
              {enrolled && <SourceChip source={game.source} />}
              <ReleaseChip game={game} />
            </div>
          )}
          <div className="sl-gamehead__chips">
            {ptUi === 'B' && <PlaythroughChipB />}
            {!enrolled && <Chip tone="warn">Not set up on this device</Chip>}
            {inConflict && <Chip tone="crit">Conflict</Chip>}
            {emptiedHold && <Chip tone="crit">Save files gone</Chip>}
            {heldElsewhere && <Chip tone="warn">Checked out by {holder}</Chip>}
            {check && !check.hasOpenConflict && (check.inSync ? <Chip tone="ok">Matches the cloud</Chip> : <Chip tone="warn">Differs from the cloud</Chip>)}
          </div>
        </div>
      </div>

      {titled && trees.length > 1 && <EmulatorTabs trees={trees} current={game.id} />}

      {inConflict && (
        <Banner
          tone="crit"
          title={`${game.name} is waiting on you`}
          detail="This device and the cloud both changed. Open Conflicts to keep one."
          action={<Button variant="primary" onClick={() => onNavigate('conflicts')}>Choose</Button>}
        />
      )}

      {emptiedHold && (
        <EmptiedHoldBanner
          hold={emptiedHold}
          gameName={game.name}
          onAnswered={(text, failed) => {
            setToast({ text, failed })
            setSyncCount(n => n + 1)
            refreshActivity()
            load()
            onSynced()
          }}
        />
      )}

      {game.sameGameAs && (
        <Banner
          tone="warn"
          title={`Same game as ${game.sameGameAs.name}${game.sameGameAs.emulator ? ` (${game.sameGameAs.emulator})` : ''}`}
          detail="This Steam shortcut starts that emulator game, so its save is synced twice, as two games. Stop tracking this copy; to keep its history, merge the two games in the console first."
          action={stopCopy ? (
            <span className="sl-confirm">
              <Button variant="primary" data-pad-label="Stop tracking this copy" onClick={() => void stopTrackingCopy()}>
                Stop tracking {game.name}
              </Button>
              <Button variant="quiet" data-pad-label="Keep tracking" onClick={() => setStopCopy(false)}>Keep tracking</Button>
            </span>
          ) : (
            <Button variant="primary" data-pad-label="Stop tracking this copy…" onClick={() => setStopCopy(true)}>
              Stop tracking this copy…
            </Button>
          )}
        />
      )}

      {dlc?.held && dlcMissing > 0 && (
        <Banner
          tone="warn"
          title={`Pull on hold: the latest save needs ${dlcMissing} DLC pack${dlcMissing === 1 ? '' : 's'} this device doesn't have`}
          detail={`${formatBytes(dlc.missingSize)}. Get them from ${dlc.headMachineName ?? 'the machine that made it'}, or pull the save anyway, in Emulator settings.`}
          action={<Button variant="primary" onClick={() => { setEmuTab('dlc'); setDlcAsk(n => n + 1) }}>Choose</Button>}
        />
      )}
      {dlcAsking.length > 0 && (
        <Banner
          tone="crit"
          title={`${dlcAsking[0].toMachineName} asks for ${game.name}'s DLC`}
          detail="Send or decline it in Emulator settings."
          action={<Button variant="primary" onClick={() => setEmuTab('sharing')}>Answer</Button>}
        />
      )}

      {enrolled ? (
        <div className="sl-actions">
          <Button variant="primary" data-pad-action="sync-this-game" disabled={locked} onClick={() => void run('sync')}>
            {pending === 'sync' ? 'Syncing…' : treeName ? `Sync ${treeName} save` : 'Sync this game'}
            <PadGlyph id="x" />
          </Button>
          <Button data-pad-action="push-now" disabled={locked} onClick={() => void run('push')}>
            {pending === 'push' ? 'Pushing…' : 'Push now'}
            <PadGlyph id="lt" />
          </Button>
          <Button data-pad-action="pull-latest" disabled={locked} onClick={() => void run('pull')}>
            {pending === 'pull' ? 'Pulling…' : 'Pull latest'}
            <PadGlyph id="rt" />
          </Button>
          {emulator && (
            <Button onClick={() => setEmuTab(dlcAsking.length > 0 && dlcMissing === 0 ? 'sharing' : dlcMissing > 0 ? 'dlc' : 'profile')}>
              <SlidersHorizontal size={14} aria-hidden="true" />Emulator settings
              {emuCount > 0 && <span className="sl-count" style={{ marginLeft: 4 }}>{emuCount}</span>}
            </Button>
          )}
          <Button variant="quiet" disabled={locked} onClick={() => void checkNow()}>
            {pending === 'check' ? 'Checking…' : 'Check now'}
          </Button>
        </div>
      ) : (
        <Banner
          tone="warn"
          title={`${game.name} has no save folder on this device`}
          detail="It is on your server, but this machine does not sync it yet. Choose its save folder below to start."
        />
      )}

      {ptUi === 'C' && <PlaythroughTabsC viewing={viewingPt && ptById(viewingPt) ? viewingPt : selectedPt} onView={setViewingPt} scope={treeName} />}

      {enrolled && <div className="sl-grid4">
        <Stat
          label="Save here"
          value={localBytes === null ? '…' : formatBytes(localBytes)}
          context="in the save folder now"
        />
        <Stat
          label="Last push"
          value={game.lastPushAt ? formatAgo(game.lastPushAt) : 'None yet'}
          context={state ? `Conflicts: ${POLICY[state.game.conflictPolicy ?? 'Manual'] ?? state.game.conflictPolicy}` : '…'}
        />
        <Stat
          label="Versions"
          value={versions ? versions.length : '…'}
          context={state?.game.retainVersions ? `keeps the newest ${state.game.retainVersions}` : 'server default retention'}
        />
        <Stat
          label="Sent last push"
          value={game.lastPushBytes != null ? formatBytes(game.lastPushBytes) : '—'}
          context="only what changed goes over"
        />
      </div>}

      {ptUi === 'A' && <PlaythroughsCardA />}

      <div className="sl-grid2">
        <Card title="On the server">
          {stateError && <p className="sl-empty" style={{ padding: 0 }}>{stateError}</p>}
          {!stateError && !state && <p className="sl-empty" style={{ padding: 0 }}>Loading…</p>}
          {state && (
            <dl className="sl-kv">
              {ptFeature && (<><dt>Playthrough</dt><dd>{ptById(selectedPt).name}</dd></>)}
              <dt>Latest save</dt>
              <dd title={head ? formatDateTime(head.createdAt) : undefined}>
                {head ? formatAgo(head.createdAt) : 'Nothing uploaded yet'}
              </dd>
              {head && (<><dt>Saved by</dt><dd>{head.machineName}</dd></>)}
              {head && (<><dt>Size</dt><dd>{formatBytes(head.size)}</dd></>)}
              <dt>Stored total</dt>
              <dd>{formatBytes(state.totalStorageBytes)} across all versions</dd>
              <dt>Checked out</dt>
              <dd>{holder ? (holder === machineName ? 'By this device' : `By ${holder}`) : 'By nobody'}</dd>
            </dl>
          )}
        </Card>

        <Card title="On this device">
          <dl className="sl-kv">
            <dt>Save folder</dt>
            <dd>{game.path || 'Not set'}</dd>
            <dt>Watches</dt>
            <dd>
              {game.processNames.length > 0
                ? game.processNames.join(', ')
                : 'No process named, so launch and exit syncing is off'}
            </dd>
            {game.steamAppId != null && (<><dt>Steam app</dt><dd>{game.steamAppId}</dd></>)}
            {game.installDir && (<><dt>Installed in</dt><dd>{game.installDir}</dd></>)}
            <dt>Steam Cloud</dt>
            <dd>{game.hasSteamCloud == null ? 'Not known' : game.hasSteamCloud ? 'Covers this game too' : 'Does not cover it'}</dd>
            <dt>Pull before launch</dt>
            <dd>{onOff(game.pullBeforeLaunchEnabled, 'Default')}</dd>
            <dt>Push after exit</dt>
            <dd>{onOff(game.pushAfterExitEnabled, 'On (default)')}</dd>
            <dt>Excludes</dt>
            <dd className="sl-mono">
              {state?.game.excludeGlobs && state.game.excludeGlobs.length > 0
                ? state.game.excludeGlobs.join('  ')
                : 'None — every file in the folder is saved'}
            </dd>
            <dt>Launch</dt>
            <dd>{platform === 'Linux' ? 'Through the Steam launch command (Settings)' : 'Watched by process name'}</dd>
          </dl>
        </Card>
      </div>

      {historyFeature ? (
        <>
          <VersionsCard gameId={game.id} headId={state?.head?.id ?? null} refreshKey={syncCount} onLoaded={setVersions}
            playthrough={ptUi === 'C' ? (viewingPt && ptById(viewingPt) ? viewingPt : selectedPt) : undefined} />
          <GameManagement game={game} platform={platform} onChanged={onChanged} onRemoved={onRemoved} />
        </>
      ) : (
        <div className="sl-grid2">
          <VersionsCard gameId={game.id} headId={state?.head?.id ?? null} refreshKey={syncCount} onLoaded={setVersions}
            playthrough={ptUi === 'C' ? (viewingPt && ptById(viewingPt) ? viewingPt : selectedPt) : undefined} />
          <GameManagement game={game} platform={platform} onChanged={onChanged} onRemoved={onRemoved} />
        </div>
      )}

      {enrolled && <SaveFilesCard gameId={game.id} refreshKey={syncCount} />}

      {toastView}
    </div>
  )
}
