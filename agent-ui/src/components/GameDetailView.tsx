import { useCallback, useEffect, useState } from 'react'
import type { Conflict, GameState, GameSyncMode, SaveVersion, SyncStatus, TrackedGame, View } from '../types'
import { api } from '../api'
import { formatAgo, formatBytes, formatDateTime } from '../format'
import { refreshActivity, useActivityBusy } from '../useActivity'
import { GameArt } from './GameArt'
import { GameManagement } from './GameManagement'
import { VersionsCard } from './VersionsCard'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Stat } from './ui/Stat'
import { Toast } from './ui/Toast'

interface Props {
  game: TrackedGame
  conflicts: Conflict[]
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
 * folder is only hashed on "Check now" — `sync-status` walks every file, so it is never automatic.
 */
export function GameDetailView({ game, conflicts, machineName, platform, onBack, onNavigate, onSynced, onChanged, onRemoved }: Props) {
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

  const load = useCallback(() => {
    api.gameState(game.id)
      .then(s => { setState(s); setStateError(null) })
      .catch(err => setStateError(err instanceof Error ? err.message : 'Could not reach the server.'))
  }, [game.id])

  useEffect(() => { setState(null); setCheck(null); load() }, [load])

  // A plain directory walk on the agent (no hashing), so asking on open — and after a sync — is fine.
  // Never on a timer: the point is what is here now, not a live gauge.
  useEffect(() => {
    let live = true
    api.localSize(game.id).then(s => { if (live) setLocalBytes(s.bytes) }).catch(() => { if (live) setLocalBytes(null) })
    return () => { live = false }
  }, [game.id, syncCount, game.path])

  const inConflict = conflicts.some(c => c.gameId === game.id)

  async function run(mode: GameSyncMode) {
    setPending(mode)
    setToast(null)
    setCheck(null)
    try {
      const { message } = await api.syncGame(game.id, mode)
      setToast({ text: message, failed: false })
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

  return (
    <div className="sl-page">
      <Button size="sm" variant="quiet" className="sl-back" onClick={onBack}>← Games</Button>

      <div className="sl-gamehead">
        <div className="sl-gamehead__cover"><GameArt id={game.id} name={game.name} kind="grid" w={192} /></div>
        <div className="sl-gamehead__main">
          <h2 className="sl-gamehead__title">{game.name}</h2>
          <div className="sl-gamehead__chips">
            {inConflict && <Chip tone="crit">Conflict</Chip>}
            {heldElsewhere && <Chip tone="warn">Checked out by {holder}</Chip>}
            {check && !check.hasOpenConflict && (check.inSync ? <Chip tone="ok">Matches the cloud</Chip> : <Chip tone="warn">Differs from the cloud</Chip>)}
          </div>
        </div>
      </div>

      {inConflict && (
        <Banner
          tone="crit"
          title={`${game.name} is waiting on you`}
          detail="This device and the cloud both changed. Open Conflicts to keep one."
          action={<Button variant="primary" onClick={() => onNavigate('conflicts')}>Choose</Button>}
        />
      )}

      <div className="sl-actions">
        <Button variant="primary" disabled={locked} onClick={() => void run('sync')}>
          {pending === 'sync' ? 'Syncing…' : 'Sync this game'}
        </Button>
        <Button disabled={locked} onClick={() => void run('push')}>{pending === 'push' ? 'Pushing…' : 'Push now'}</Button>
        <Button disabled={locked} onClick={() => void run('pull')}>{pending === 'pull' ? 'Pulling…' : 'Pull latest'}</Button>
        <Button variant="quiet" disabled={locked} onClick={() => void checkNow()}>
          {pending === 'check' ? 'Checking…' : 'Check now'}
        </Button>
      </div>

      <div className="sl-grid4">
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
      </div>

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
        <VersionsCard gameId={game.id} headId={state?.head?.id ?? null} refreshKey={syncCount} onLoaded={setVersions} />
        <GameManagement game={game} platform={platform} onChanged={onChanged} onRemoved={onRemoved} />
      </div>

      {toast && (
        <Toast tone={toast.failed ? 'warn' : 'default'} onDismiss={() => setToast(null)}>{toast.text}</Toast>
      )}
    </div>
  )
}
