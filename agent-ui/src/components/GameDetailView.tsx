import { useCallback, useEffect, useRef, useState } from 'react'
import { SlidersHorizontal } from 'lucide-react'
import type { Conflict, DlcCheck, EmptiedHold, GamePlaythroughs, GameState, GameSyncMode, SaveVersion, SyncStatus, TrackedGame, View } from '../types'
import { api } from '../api'
import { formatAgo, formatBytes, formatDateTime } from '../format'
import { refreshActivity, useActivityBusy, useFilesChangedAt } from '../useActivity'
import { PadGlyph } from '../gamepad/PadGlyph'
import { GameArt } from './GameArt'
import { GameManagement } from './GameManagement'
import { SaveFilesCard } from './SaveFilesCard'
import { EmulatorSettingsView, type EmulatorTab } from './EmulatorSettingsView'
import { emulatorSettingsFor } from '../emulators'
import { ReleaseChip } from './ReleaseChip'
import { SourceChip } from './SourceChip'
import { VersionsCard } from './VersionsCard'
import { PlaythroughTabs } from './PlaythroughTabs'
import { NewPlaythroughDialog, PlaythroughProgress } from './PlaythroughDialogs'
import { EmptiedHoldBanner } from './EmptiedHoldBanner'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Stat } from './ui/Stat'
import { Toast } from './ui/Toast'
import { EmulatorTabs } from './EmulatorTabs'
import { CopySaveModal } from './CopySaveModal'
import { emulatorOf, treeLabel } from '../titles'

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
  /** Every tree of the title `game` is one of (tasks/multi-emulator-games Phase 6), `game` included; just `game` for a game
   *  shown alone. With two or more, the page gets a tab per emulator and everything under the tabs is `game`'s. */
  trees?: TrackedGame[]
  onPickTree?: (id: string) => void
  /** A sentence the shell shows after this page has gone (a copy opens the other tree's page). */
  onNote?: (text: string) => void
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
export function GameDetailView({ game, conflicts, emptiedHold, machineName, platform, onBack, onNavigate, onSynced, onChanged, onRemoved, trees = [game], onPickTree, onNote }: Props) {
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
  // Playthroughs (tasks/save-playthroughs Phase 6): the server's list with the one this device plays, the tab being looked at
  // (null = this device's own), and the dialogs a switch or a new playthrough runs in.
  const [pts, setPts] = useState<GamePlaythroughs | null>(null)
  const [viewing, setViewing] = useState<string | null>(null)
  const [switching, setSwitching] = useState<{ id: string; name: string } | null>(null)
  const [creating, setCreating] = useState<{ fromVersion: { id: string; label: string } | null } | null>(null)
  const [renaming, setRenaming] = useState<string | null>(null)
  const [confirmArchive, setConfirmArchive] = useState(false)
  const emulator = emulatorSettingsFor(game)
  const titled = trees.length > 1
  const lead = trees.find(t => t.title?.lead) ?? trees[0]
  const treeName = titled ? treeLabel(game, trees) : null
  // Copy this save to another emulator: open on one version, null when closed.
  const [copyFrom, setCopyFrom] = useState<string | null>(null)

  const load = useCallback(() => {
    api.gameState(game.id)
      .then(s => { setState(s); setStateError(null) })
      .catch(err => setStateError(err instanceof Error ? err.message : 'Could not reach the server.'))
  }, [game.id])

  useEffect(() => { setState(null); setCheck(null); setEmuTab(null); setPts(null); setViewing(null); load() }, [load])

  const loadPlaythroughs = useCallback(() => {
    if (!game.path) { setPts(null); return }
    api.playthroughs(game.id).then(setPts).catch(() => setPts(null))
  }, [game.id, game.path])

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
  useEffect(() => { loadPlaythroughs() }, [loadPlaythroughs, syncCount])

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
  const conflicted = new Set(conflicts.map(c => c.gameId))
  const conflictElsewhere = titled ? trees.find(t => t.id !== game.id && conflicted.has(t.id)) : undefined
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
  const locked = busy || pending !== null || switching !== null
  // The state route's head is the game's DEFAULT playthrough; this device's own is the one on the tab it plays.
  const selectedPt = pts?.playthroughs.find(p => p.id === pts.selectedId) ?? null
  const several = (pts?.playthroughs.length ?? 0) > 1
  const otherPt = pts && viewing && viewing !== pts.selectedId ? pts.playthroughs.find(p => p.id === viewing) ?? null : null
  const head = (selectedPt ? selectedPt.head : state?.head) ?? null
  const holder = state?.lease?.holderMachineName ?? null
  const heldElsewhere = holder !== null && holder !== machineName
  const dlcMissing = dlc?.packages?.filter(p => !p.here).length ?? 0
  const dlcAsking = dlc?.asked?.filter(t => t.status === 'Requested') ?? []
  const emuCount = (dlcMissing > 0 ? 1 : 0) + dlcAsking.length

  async function changePlaythrough(action: 'rename' | 'archive' | 'unarchive', id: string, name?: string) {
    try {
      const r = action === 'rename' ? await api.renamePlaythrough(game.id, id, name ?? '')
        : action === 'archive' ? await api.archivePlaythrough(game.id, id) : await api.unarchivePlaythrough(game.id, id)
      setToast({ text: r.message, failed: !r.ok })
      if (r.ok) { setRenaming(null); setConfirmArchive(false); loadPlaythroughs(); onChanged() }
    } catch (err) {
      setToast({ text: err instanceof Error ? err.message : 'The change failed.', failed: true })
    }
  }

  // Looking at a playthrough this device is not on: read-only, with the one way to change that.
  const otherPanel = otherPt && (
    <>
      <Card title={`${otherPt.name}${otherPt.isDefault ? ' · default' : ''}${otherPt.archivedAt ? ' · archived' : ''}`}
        headerRight={otherPt.hasOpenConflict ? <Chip tone="crit">Conflict</Chip> : undefined}>
        <div className="sl-stack" style={{ gap: 12 }}>
          <dl className="sl-kv">
            <dt>Latest save</dt>
            <dd title={otherPt.head ? formatDateTime(otherPt.head.createdAt) : undefined}>
              {otherPt.head ? `${formatAgo(otherPt.head.createdAt)} · ${otherPt.head.machineName} · ${formatBytes(otherPt.head.size)}` : 'No save yet'}
            </dd>
            <dt>Versions</dt><dd>{otherPt.versionCount} uploaded to it</dd>
            <dt>Played on</dt>
            <dd>{otherPt.devices.length > 0 ? otherPt.devices.map(d => d.machineName).join(', ') : 'No device right now'}</dd>
          </dl>
          <p className="sl-empty" style={{ padding: 0, textAlign: 'left' }}>
            This device plays {pts?.selectedName ?? 'another playthrough'}; looking at this one changes nothing. Playing it here saves
            {' '}{pts?.selectedName ?? 'your current playthrough'} to its own line first, then puts this one's latest save in the save folder.
          </p>
          <div className="sl-actions">
            <Button variant="primary" disabled={locked || !!otherPt.archivedAt || pts?.offline === true && !pts.copies.some(c => c.playthroughId === otherPt.id)}
              data-pad-label={`Play ${otherPt.name} here`} onClick={() => setSwitching({ id: otherPt.id, name: otherPt.name })}>
              Play this one here
            </Button>
            {renaming === null ? (
              <Button disabled={locked || pts?.offline === true} data-pad-label="Rename this playthrough" onClick={() => setRenaming(otherPt.name)}>Rename…</Button>
            ) : (
              <span className="sl-inline">
                <input className="sl-input" style={{ width: 200 }} aria-label="New name" maxLength={60} value={renaming} autoFocus
                  onChange={e => setRenaming(e.target.value)} />
                <Button variant="primary" disabled={!renaming.trim()} onClick={() => void changePlaythrough('rename', otherPt.id, renaming)}>Save name</Button>
                <Button variant="quiet" onClick={() => setRenaming(null)}>Cancel</Button>
              </span>
            )}
            {!otherPt.isDefault && (otherPt.archivedAt ? (
              <Button disabled={locked} onClick={() => void changePlaythrough('unarchive', otherPt.id)}>Unarchive</Button>
            ) : confirmArchive ? (
              <span className="sl-confirm">
                <span>Archive {otherPt.name}? Its saves are kept, and nothing syncs to it until you unarchive it.</span>
                <Button variant="primary" onClick={() => void changePlaythrough('archive', otherPt.id)}>Archive</Button>
                <Button variant="quiet" onClick={() => setConfirmArchive(false)}>Keep</Button>
              </span>
            ) : (
              <Button disabled={locked || pts?.offline === true} onClick={() => setConfirmArchive(true)}>Archive…</Button>
            ))}
          </div>
        </div>
      </Card>
      <VersionsCard key={otherPt.id} gameId={game.id} headId={otherPt.head?.id ?? null} refreshKey={syncCount}
        onLoaded={() => {}} playthrough={otherPt.id} title={`Versions on the server · ${otherPt.name}`}
        onFork={v => setCreating({ fromVersion: { id: v, label: `a version of ${otherPt.name}` } })} />
    </>
  )

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
        <div className="sl-gamehead__cover"><GameArt id={lead.id} name={lead.name} kind="grid" w={192} console={game.release?.console} /></div>
        <div className="sl-gamehead__main">
          <h2 className="sl-gamehead__title">{titled ? game.title?.name ?? game.name : game.name}</h2>
          {(enrolled || game.release) && (
            <div className="sl-gamehead__source">
              {enrolled && <SourceChip source={game.source} />}
              <ReleaseChip game={game} />
            </div>
          )}
          <div className="sl-gamehead__chips">
            {!enrolled && <Chip tone="warn">Not set up on this device</Chip>}
            {several && selectedPt && <Chip>Playing {selectedPt.name}</Chip>}
            {inConflict && <Chip tone="crit">Conflict</Chip>}
            {conflictElsewhere && <Chip tone="crit">Conflict in {emulatorOf(conflictElsewhere) ?? conflictElsewhere.name}</Chip>}
            {emptiedHold && <Chip tone="crit">Save files gone</Chip>}
            {heldElsewhere && <Chip tone="warn">Checked out by {holder}</Chip>}
            {check && !check.hasOpenConflict && (check.inSync ? <Chip tone="ok">Matches the cloud</Chip> : <Chip tone="warn">Differs from the cloud</Chip>)}
          </div>
        </div>
      </div>

      {titled && <EmulatorTabs trees={trees} current={game.id} conflicted={conflicted} onPick={id => onPickTree?.(id)} />}

      {enrolled && pts && (pts.playthroughs.length > 0 || pts.selectedId) && (
        <PlaythroughTabs
          data={pts} viewing={viewing} disabled={locked} machineName={machineName}
          onView={id => { setViewing(id === pts.selectedId ? null : id); setRenaming(null); setConfirmArchive(false) }}
          onNew={() => setCreating({ fromVersion: null })}
        />
      )}

      {inConflict && (
        <Banner
          tone="crit"
          title={`${titled ? `${game.title?.name} · ${treeName}` : game.name} is waiting on you`}
          detail={titled
            ? `This device and the cloud both changed ${treeName}'s save. The other emulators' saves are fine. Open Conflicts to keep one.`
            : 'This device and the cloud both changed. Open Conflicts to keep one.'}
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

      {otherPt ? otherPanel : <>
      {enrolled ? (
        <div className="sl-actions">
          <Button variant="primary" data-pad-action="sync-this-game" disabled={locked} onClick={() => void run('sync')}>
            {pending === 'sync' ? 'Syncing…' : titled ? `Sync ${treeName} save` : 'Sync this game'}
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
          {titled && versions && versions.length > 0 && (
            <Button data-pad-label="Copy to another emulator"
              onClick={() => setCopyFrom(versions.find(v => v.id === state?.head?.id)?.id ?? versions.find(v => !v.mergedFrom)?.id ?? null)}>
              Copy to another emulator…
            </Button>
          )}
          <Button variant="quiet" disabled={locked} onClick={() => void checkNow()}>
            {pending === 'check' ? 'Checking…' : 'Check now'}
          </Button>
          {several && selectedPt && pts?.offline === false && (renaming === null ? (
            <Button variant="quiet" disabled={locked} data-pad-label="Rename this playthrough" onClick={() => setRenaming(selectedPt.name)}>
              Rename playthrough…
            </Button>
          ) : (
            <span className="sl-inline">
              <input className="sl-input" style={{ width: 200 }} aria-label="New name" maxLength={60} value={renaming} autoFocus
                onChange={e => setRenaming(e.target.value)} />
              <Button variant="primary" disabled={!renaming.trim()} onClick={() => void changePlaythrough('rename', selectedPt.id, renaming)}>Save name</Button>
              <Button variant="quiet" onClick={() => setRenaming(null)}>Cancel</Button>
            </span>
          ))}
        </div>
      ) : (
        <Banner
          tone="warn"
          title={titled ? `${treeName}'s save for ${game.title?.name} has no folder on this device` : `${game.name} has no save folder on this device`}
          detail={titled
            ? `It is on your server, but this machine does not sync ${treeName}'s save yet. Choose its save folder below to start; the other emulators' saves are not affected.`
            : 'It is on your server, but this machine does not sync it yet. Choose its save folder below to start.'}
        />
      )}

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

      <div className="sl-grid2">
        <Card title="On the server">
          {stateError && <p className="sl-empty" style={{ padding: 0 }}>{stateError}</p>}
          {!stateError && !state && <p className="sl-empty" style={{ padding: 0 }}>Loading…</p>}
          {state && (
            <dl className="sl-kv">
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

      <div className="sl-grid2">
        <VersionsCard gameId={game.id} headId={head?.id ?? null} refreshKey={syncCount} onLoaded={setVersions}
          title={[titled ? treeName : null, several ? selectedPt?.name : null].some(Boolean)
            ? `Versions on the server · ${[titled ? treeName : null, several ? selectedPt?.name : null].filter(Boolean).join(' · ')}`
            : undefined}
          onCopy={titled ? setCopyFrom : undefined}
          playthrough={selectedPt && several ? selectedPt.id : undefined}
          onFork={v => setCreating({ fromVersion: { id: v, label: `a version of ${selectedPt?.name ?? 'this playthrough'}` } })} />
        <GameManagement game={game} platform={platform} onChanged={onChanged} onRemoved={onRemoved} />
      </div>

      {enrolled && <SaveFilesCard gameId={game.id} refreshKey={syncCount} />}
      </>}

      {copyFrom && versions && (
        <CopySaveModal
          game={game} versions={versions} versionId={copyFrom} headId={state?.head?.id ?? null} trees={trees}
          onClose={() => setCopyFrom(null)}
          onCopied={(target, text) => {
            setCopyFrom(null)
            onSynced()
            if (onNote && onPickTree) { onNote(text); onPickTree(target) }
            else setToast({ text, failed: false })
          }}
        />
      )}

      {switching && (
        <PlaythroughProgress
          game={game} title={`Play ${switching.name} on this device`}
          run={() => api.switchPlaythrough(game.id, switching.id)}
          onDone={r => {
            setSwitching(null)
            if (r.ok) setViewing(null)
            setToast({ text: r.message, failed: !r.ok })
            setSyncCount(n => n + 1); refreshActivity(); load(); onSynced(); onChanged()
          }}
        />
      )}
      {creating && pts && (
        <NewPlaythroughDialog
          game={game} data={pts} fromVersion={creating.fromVersion}
          onClose={() => setCreating(null)}
          onDone={r => {
            setCreating(null)
            setViewing(null)
            setToast({ text: r.message, failed: !r.ok })
            setSyncCount(n => n + 1); refreshActivity(); load(); onSynced(); onChanged()
          }}
        />
      )}

      {toastView}
    </div>
  )
}
