import { useEffect, useState } from 'react'
import type { AgentState } from '../types'
import { api } from '../api'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'

interface Props {
  state: AgentState | null
  /** `#enroll:<link>` deep link from the pairing page: pre-filled and one click from done. */
  pendingJoin: string | null
  /** Called once the join succeeded (clear any pending value, refresh the agent state). */
  onJoined: () => void
}

/**
 * The Join card (tasks/enrollment-options, option B): enroll this machine from the console's join
 * link — the flow the enrollment file used to serve, minus the file. It is what the pairing page's
 * "Open the agent and finish" button lands on (via `#enroll:<link>`), and it doubles as the manual
 * entry point when someone reads a link or code off another screen.
 *
 * Shown only while it has something to say: hidden once this machine is registered, unless a deep
 * link has just arrived.
 */
export function JoinCard({ state, pendingJoin, onJoined }: Props) {
  const [value, setValue] = useState(pendingJoin ?? '')
  const [serverUrl, setServerUrl] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState<{ machineName: string; mapped: number; unmapped: number } | null>(null)

  useEffect(() => { if (pendingJoin !== null) setValue(pendingJoin) }, [pendingJoin])
  useEffect(() => { setServerUrl(state?.serverUrl ?? '') }, [state?.serverUrl])

  const registered = state?.connected === true
  if (registered && pendingJoin === null && done === null) return null

  const trimmed = value.trim()
  const looksLikeLink = trimmed.includes('/')
  const canSubmit = !busy && trimmed.length > 0 && (looksLikeLink || serverUrl.trim().length > 0)

  const join = async () => {
    setBusy(true)
    setError(null)
    try {
      const res = await api.join(
        looksLikeLink ? { url: trimmed } : { code: trimmed, serverUrl: serverUrl.trim() })
      setDone({ machineName: res.machineName, mapped: res.mapped, unmapped: res.unmapped })
      onJoined()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card title="Join a server" headerRight={
      done
        ? <Chip tone="ok">Enrolled</Chip>
        : registered ? <Chip tone="ok">Registered</Chip> : <Chip tone="warn">Not registered</Chip>
    }>
      <div className="sl-stack">
        {done ? (
          <>
            <div className="sl-banner sl-banner--ok">
              <div className="sl-banner__txt">
                <strong>Enrolled as {done.machineName}</strong>
                <span>
                  {done.mapped + done.unmapped > 0
                    ? `${done.mapped + done.unmapped} game(s) matched this machine; ${done.mapped} already point at a save folder here.`
                    : 'The server\u2019s games were picked up — the agent will adopt them on its first sync.'}
                </span>
              </div>
            </div>
            <p className="sl-note">The key is kept in the agent's config file and never shown here.</p>
          </>
        ) : (
          <>
            {pendingJoin !== null && (
              <div className="sl-banner" style={{
                borderColor: 'var(--color-accent-line)', background: 'var(--color-accent-soft)',
              }}>
                <div className="sl-banner__txt">
                  <strong>Joining from the link you opened</strong>
                  <span>The link is filled in — one click to finish.</span>
                </div>
              </div>
            )}

            <div className="sl-field">
              <label htmlFor="join-link">Join link or code</label>
              <input
                id="join-link" className="sl-input sl-input--mono" value={value}
                placeholder="http://192.168.1.10:5080/join/K7QM-4H2X \u2014 or just K7QM-4H2X"
                onChange={e => { setValue(e.target.value); setError(null) }}
                onKeyDown={e => { if (e.key === 'Enter' && canSubmit) void join() }}
              />
              <small>
                From the console: <strong>Configuration \u2192 Enroll a machine \u2192 Create join link</strong>.
                The link works once and expires on its own.
              </small>
            </div>

            {trimmed.length > 0 && !looksLikeLink && (
              <div className="sl-field">
                <label htmlFor="join-server">Server URL</label>
                <input
                  id="join-server" className="sl-input sl-input--mono" value={serverUrl}
                  placeholder="http://192.168.1.10:5080" style={{ maxWidth: 320 }}
                  onChange={e => setServerUrl(e.target.value)}
                />
                <small>Needed because a bare code does not say which server it belongs to.</small>
              </div>
            )}

            <div className="sl-inline">
              <Button variant="primary" onClick={() => void join()} disabled={!canSubmit}>
                {busy ? 'Joining\u2026' : pendingJoin !== null ? 'Finish enrollment' : 'Enroll this machine'}
              </Button>
              {busy && <span className="sl-note">Fetching the link \u2192 trading the code for a key \u2192 matching save folders\u2026</span>}
              {error && <span style={{ color: 'var(--color-accent-ink)', fontSize: 12.5 }} role="alert">{error}</span>}
            </div>
          </>
        )}
      </div>
    </Card>
  )
}
