import { useState, useEffect, useCallback, useRef } from 'react'
import type { AgentAppearance, AgentState, AgentVersion, TestConnection, TrackedGame } from '../types'
import { api } from '../api'
import { LaunchSetupCard } from './LaunchSetupCard'
import { DeckyPluginCard } from './DeckyPluginCard'
import { PlaynitePluginCard } from './PlaynitePluginCard'
import { AppearanceCard } from './AppearanceCard'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { PageHead } from './ui/PageHead'

interface Props {
  state: AgentState | null
  onSaved: () => void
  appearance: AgentAppearance | null
  onAppearanceChanged: (next: AgentAppearance) => void
  /** Open one game's page — where its folder, process and tracking are managed. */
  onOpenGame: (id: string) => void
}

/**
 * What the agent's last update check found.
 *
 * On Windows the tray owns the actual update — this is a read-out of what it already knows, and the
 * tray is where the user says yes. On Linux there is no tray and nothing that can toast, so this and
 * `savelocker doctor` are the only places a Deck user can see the state of an update at all.
 *
 * **Staged and available are different states and are shown differently.** Available means the
 * server is offering something newer and nothing has been downloaded. Staged means the payload is
 * already here, checked against the published SHA-256 and smoke-tested, and the only thing left is
 * the restart — so it gets the green badge and the instruction, while available gets neither.
 * `stagedVersion` is null on Windows, where the installer path stages nothing.
 */
function UpdateStatus({ platform }: { platform?: string }) {
  const [info, setInfo] = useState<AgentVersion | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    api.agentVersion().then(setInfo).catch(() => setFailed(true))
  }, [])

  if (failed) return <p className="sl-empty" style={{ padding: 0 }}>Could not read the agent version.</p>
  if (!info) return <p className="sl-empty" style={{ padding: 0 }}>Checking…</p>

  return (
    <div className="sl-stack" style={{ gap: 8 }}>
      <div className="sl-inline">
        <span style={{ fontSize: 13 }}>Running v{info.currentVersion}</span>
        {info.stagedVersion
          ? <Chip tone="ok">v{info.stagedVersion} READY</Chip>
          : info.updateAvailable && info.latestVersion
            ? <Chip tone="warn">v{info.latestVersion} AVAILABLE</Chip>
            : null}
      </div>
      <small style={{ fontSize: 11.5, color: 'var(--color-dim)', lineHeight: 1.5 }}>
        {info.stagedVersion
          // The agent's own words for what blocks it, verbatim: it knows which game is running and
          // this UI does not, and re-phrasing it here is how three surfaces start disagreeing.
          ? info.stagedBlockedReason
            ?? 'It is downloaded and verified, and installs the next time the agent starts — a ' +
               'reboot, or a log out and back in. Nothing is replaced under a running session.'
          : !info.updateAvailable
            ? 'This is the version the server is offering.'
            : platform === 'Linux'
              ? 'The agent downloads and verifies it on its next check (every few hours), then ' +
                'installs it at the following start. Run `savelocker update` to do it now.'
              : 'The tray icon offers the update — accept it there to install and restart.'}
      </small>
    </div>
  )
}

/** The TOFU pin as a chip: what the agent will do if the server's TLS key ever changes. */
function TrustChip({ trust }: { trust: string | undefined }) {
  if (trust === 'pinned') return <Chip tone="ok">Pinned on first connect</Chip>
  if (trust === 'unpinned') return <Chip tone="warn">Not pinned yet</Chip>
  return <Chip tone="warn">Not pinned (plain HTTP)</Chip>
}

export function SettingsView({ state, onSaved, appearance, onAppearanceChanged, onOpenGame }: Props) {
  const [serverUrl, setServerUrl] = useState('')
  const [machineName, setMachineName] = useState('')
  const [adminPassword, setAdminPassword] = useState('')
  const [startWithWindows, setStartWithWindows] = useState(false)
  const [settleQuietSeconds, setSettleQuietSeconds] = useState('10')
  const [games, setGames] = useState<TrackedGame[]>([])
  const [selectedGames, setSelectedGames] = useState<Set<string>>(new Set())
  const [confirmingRemove, setConfirmingRemove] = useState(false)
  const [saving, setSaving] = useState(false)
  const [registering, setRegistering] = useState(false)
  const [testing, setTesting] = useState(false)
  const [test, setTest] = useState<TestConnection | null>(null)
  const [status, setStatus] = useState('')
  const dirtyFields = useRef<Set<string>>(new Set())

  useEffect(() => {
    if (state) {
      if (!dirtyFields.current.has('serverUrl')) setServerUrl(state.serverUrl)
      if (!dirtyFields.current.has('machineName')) setMachineName(state.machineName)
      if (!dirtyFields.current.has('settleQuietSeconds'))
        setSettleQuietSeconds(String(state.settleQuietSeconds))
      setStartWithWindows(state.startWithWindows)
    }
  }, [state])

  const loadGames = useCallback(() => {
    api.games().then(setGames).catch(console.error)
  }, [])

  useEffect(() => { loadGames() }, [loadGames])

  const save = async () => {
    setSaving(true)
    try {
      const seconds = parseInt(settleQuietSeconds, 10)
      const res = await api.saveConfig({
        serverUrl,
        machineName,
        settleQuietSeconds: Number.isFinite(seconds) ? Math.min(Math.max(seconds, 0), 300) : undefined,
      })
      dirtyFields.current.clear()
      setTest(null)
      onSaved()
      if (res.identityCleared) {
        // Left on screen rather than auto-cleared: the agent cannot sync until the user acts on it,
        // and a message that vanishes after two seconds is how someone ends up staring at a
        // disconnected agent with no idea what changed.
        setStatus('Saved. This is a different server, so the stored machine key was cleared — ' +
                  'click Register / Re-register to enroll this machine with it.')
      } else {
        setStatus('Saved.')
        setTimeout(() => setStatus(''), 2000)
      }
    } catch (e) {
      setStatus('Save failed: ' + (e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const register = async () => {
    setRegistering(true)
    setStatus('Registering…')
    try {
      await api.saveConfig({ serverUrl, machineName })
      await api.register(adminPassword || undefined)
      setAdminPassword('')
      dirtyFields.current.clear()
      onSaved()
      setStatus('Registered successfully.')
    } catch (e) {
      setStatus('Registration failed: ' + (e as Error).message)
    } finally {
      setRegistering(false)
    }
  }

  // Tests the SAVED server URL, not what is typed: that is the one the agent actually uses, and a test
  // that answered for an unsaved address would pass right up until the next sync failed.
  const testConnection = async () => {
    setTesting(true)
    setTest(null)
    try { setTest(await api.testConnection()) }
    catch (e) { setTest({ ok: false, status: null, latencyMs: 0, error: (e as Error).message }) }
    finally { setTesting(false) }
  }

  // The toggle shows what the machine will actually do at login, not what was clicked. The registry
  // write can be refused outright (group policy, security software) or written and then reverted,
  // and the box used to stay ticked through both. WA-10.
  const toggleStartup = async (val: boolean) => {
    setStartWithWindows(val)
    try {
      const res = await api.saveConfig({ startWithWindows: val })
      setStartWithWindows(res.startWithWindows)
      setStatus(res.startWithWindows === val
        ? ''
        : 'Windows did not keep that startup setting.')
    } catch (e) {
      setStartWithWindows(!val)
      setStatus((e as Error).message)
    }
  }

  const toggleAutoUpdate = async (val: boolean) => {
    try {
      await api.saveConfig({ autoUpdate: val })
      onSaved()
    } catch (e) {
      setStatus((e as Error).message)
    }
  }

  const toggleGame = (id: string) => {
    setSelectedGames(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const removeSelected = async () => {
    for (const id of selectedGames) await api.removeGame(id)
    setSelectedGames(new Set())
    setConfirmingRemove(false)
    loadGames()
    onSaved()
  }

  const isLinux = state?.platform === 'Linux'
  const startupLabel = isLinux ? 'Start on login' : 'Start with Windows'
  const busy = saving || registering

  return (
    <div className="sl-page">
      <PageHead title="Settings" sub={`${state?.machineName ?? '…'} · ${state?.serverUrl ? state.serverUrl.replace(/^https?:\/\//, '') : 'no server'}`} />

      <Card title="Connection" headerRight={<TrustChip trust={state?.serverTrust} />}>
        <div className="sl-stack">
          <div className="sl-field">
            <label htmlFor="server-url">Server URL</label>
            <input
              id="server-url" className="sl-input sl-input--mono" value={serverUrl}
              onChange={e => { dirtyFields.current.add('serverUrl'); setServerUrl(e.target.value) }}
            />
          </div>
          <div className="sl-field">
            <label htmlFor="machine-name">Machine name</label>
            <input
              id="machine-name" className="sl-input" style={{ maxWidth: 280 }} value={machineName}
              onChange={e => { dirtyFields.current.add('machineName'); setMachineName(e.target.value) }}
            />
          </div>
          <div className="sl-field">
            <label htmlFor="admin-password">Admin password</label>
            <input
              id="admin-password" className="sl-input" style={{ maxWidth: 280 }} type="password" autoComplete="off"
              placeholder="only needed to re-register this name"
              value={adminPassword} onChange={e => setAdminPassword(e.target.value)}
            />
          </div>

          <div className="sl-inline">
            <Button variant="primary" onClick={() => void save()} disabled={busy}>Save</Button>
            <Button onClick={() => void testConnection()} disabled={busy || testing}>
              {testing ? 'Testing…' : 'Test connection'}
            </Button>
            <Button onClick={() => void register()} disabled={busy}>Register / Re-register</Button>
            {test && (
              <Chip tone={test.ok ? 'ok' : 'warn'}>
                {test.ok ? `Reachable · ${test.latencyMs} ms` : test.error ?? 'Not reachable'}
              </Chip>
            )}
          </div>
          {status && <div role="status" style={{ fontSize: 12.5, color: 'var(--color-dim)' }}>{status}</div>}

          <div className="sl-setting__note" style={{ marginTop: 0 }}>
            {state?.connected
              ? 'Registered — this machine holds a key for the server. '
              : 'Not registered yet. '}
            The key is kept in the agent's config file and never shown here. If it is ever exposed, use
            Register / Re-register to rotate it.
          </div>
        </div>
      </Card>

      <AppearanceCard appearance={appearance} onChanged={onAppearanceChanged} />

      <Card title="Sync safety">
        <div className="sl-stack">
          <div className="sl-field">
            <label htmlFor="settle">Wait for saves to settle (seconds)</label>
            <div className="sl-inline">
              <input
                id="settle" className="sl-input" style={{ width: 90 }} type="number" min={0} max={300}
                value={settleQuietSeconds}
                onChange={e => { dirtyFields.current.add('settleQuietSeconds'); setSettleQuietSeconds(e.target.value) }}
              />
              <Button variant="primary" onClick={() => void save()} disabled={busy}>Save</Button>
            </div>
            <small>
              After a game closes, SaveLocker waits until its save folder stops changing for this long before
              backing it up — so a game that keeps writing for a few seconds after exit can't be captured
              half-finished. Raise it if a game is slow to flush its save. 0 backs up immediately. Manual syncs
              are never delayed.
            </small>
          </div>

          <div className="sl-setting">
            <div className="sl-setting__label">
              {startupLabel}
              <small>{isLinux ? 'Launch the agent when you sign in.' : 'Launch the agent at login.'}</small>
            </div>
            <button
              type="button" className="sl-switch" role="switch" aria-checked={startWithWindows}
              aria-label={startupLabel} onClick={() => void toggleStartup(!startWithWindows)}
            />
          </div>

          {isLinux && (
            <div className="sl-setting">
              <div className="sl-setting__label">
                Install agent updates automatically
                <small>The agent downloads and verifies a newer version and installs it at the next start. Off, it only tells you one is waiting.</small>
              </div>
              <button
                type="button" className="sl-switch" role="switch" aria-checked={state?.autoUpdate ?? true}
                aria-label="Install agent updates automatically"
                onClick={() => void toggleAutoUpdate(!(state?.autoUpdate ?? true))}
              />
            </div>
          )}
        </div>
      </Card>

      <Card title="Updates"><UpdateStatus platform={state?.platform} /></Card>

      {/* Moved here from the Overview when it was trimmed to quick info (checkpoint-ui plan.md,
          Phase 5) — this is where the prototype puts them. The launch-options card is still the
          supported manual step, so it comes first; the plugin cards are the optional things that can
          remove it. Each renders nothing where it does not apply (Windows, no Playnite), so there is
          deliberately no section header here to be left orphaned. */}
      <div className="sl-collapse-empty" style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <LaunchSetupCard />
        <DeckyPluginCard />
        <PlaynitePluginCard />
      </div>

      <Card
        title="Tracked games"
        flush
        headerRight={selectedGames.size > 0 && (
          confirmingRemove ? (
            <div className="sl-confirm">
              <span>Stop syncing {selectedGames.size} here? Their saves stay on the server.</span>
              <Button size="sm" variant="primary" onClick={() => void removeSelected()}>Stop tracking {selectedGames.size}</Button>
              <Button size="sm" variant="quiet" onClick={() => setConfirmingRemove(false)}>Keep</Button>
            </div>
          ) : (
            <Button size="sm" onClick={() => setConfirmingRemove(true)}>Remove {selectedGames.size} selected…</Button>
          )
        )}
      >
        {games.length === 0 ? (
          <div className="sl-empty">No games tracked yet. Go to Add Games to enroll.</div>
        ) : (
          <div className="sl-table-wrap">
            <table className="sl-table">
              <tbody>
                {games.map(g => (
                  <tr key={g.id}>
                    <td style={{ width: 28 }}>
                      <input
                        type="checkbox" aria-label={`Select ${g.name}`}
                        checked={selectedGames.has(g.id)} onChange={() => toggleGame(g.id)}
                      />
                    </td>
                    <td>
                      <div style={{ fontWeight: 600 }}>{g.name}</div>
                      <div className="sl-path">{g.path || 'No save folder set'}</div>
                    </td>
                    <td>
                      {!g.path && <Chip tone="warn">No save folder</Chip>}
                      {!isLinux && g.processNames.length === 0 && <> <Chip tone="warn">Launch/exit sync off</Chip></>}
                    </td>
                    <td className="sl-num"><Button size="sm" onClick={() => onOpenGame(g.id)}>Manage</Button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
