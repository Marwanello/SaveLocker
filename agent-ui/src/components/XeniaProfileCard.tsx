import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import type { XeniaProfile } from '../types'
import { Card } from './ui/Card'

interface Props {
  gameId: string
  /** Bumped by the page after every sync it runs. */
  refreshKey: number
}

const POLICY: Record<XeniaProfile['policy'], string> = {
  Ask: 'Switch to the game’s profile, and ask where I can',
  Switch: 'Switch to the game’s profile without asking',
  Leave: 'Leave Xenia as it is',
}

/**
 * An Xbox 360 game's profile on this machine (SaveLocker tasks/emulator-saves Phase 5c): the profile its saves are under,
 * the one Xenia is set to start as right now, and what happens at launch when the two differ — the machine's setting, or
 * this game's own. Hidden for any game that isn't Xenia's.
 */
export function XeniaProfileCard({ gameId, refreshKey }: Props) {
  const [p, setP] = useState<XeniaProfile | null>(null)
  const load = useCallback(() => { api.xeniaProfile(gameId).then(setP).catch(() => setP(null)) }, [gameId])
  useEffect(() => { load() }, [load, refreshKey])
  if (!p) return null

  const name = (xuid?: string | null, tag?: string | null) => (xuid ? (tag ? `${tag} (${xuid})` : xuid) : 'no profile')
  const differs = !p.legacy && p.configuredXuid !== p.gameXuid

  async function setPolicy(value: string) {
    await api.setGameProfileAtLaunch(gameId, value === '' ? null : value as XeniaProfile['policy']).catch(() => {})
    load()
  }

  return (
    <Card title="Xbox 360 profile">
      {p.legacy ? (
        <p style={{ fontSize: 12.5, color: 'var(--color-dim)', margin: 0 }}>
          This game's saves are in Xenia's layout from before profiles (<code>content/&lt;title ID&gt;</code>), so they load
          whoever Xenia signs in as. Nothing is checked at launch.
        </p>
      ) : (
        <div className="sl-stack">
          <div style={{ fontSize: 12.5, lineHeight: 1.6 }}>
            <div>Saves are under <b>{name(p.gameXuid, p.gameTag)}</b></div>
            <div style={{ color: differs ? 'var(--color-watch-ink)' : undefined, fontWeight: differs ? 600 : 400 }}>
              Xenia starts as {name(p.configuredXuid, p.configuredTag)}{differs ? ' — the game would start without its saves' : ''}
            </div>
          </div>
          <div className="sl-field">
            <label htmlFor={`x360-game-${gameId}`}>At launch, for this game</label>
            <select
              id={`x360-game-${gameId}`} className="sl-input" style={{ maxWidth: 360 }} value={p.gamePolicy ?? ''}
              onChange={e => void setPolicy(e.target.value)}
            >
              <option value="">This machine's setting ({POLICY[p.policy].toLowerCase()})</option>
              <option value="Ask">{POLICY.Ask}</option>
              <option value="Switch">{POLICY.Switch}</option>
              <option value="Leave">{POLICY.Leave}</option>
            </select>
            <small>
              Checked when SaveLocker starts the game: Steam's launch option, Decky or Playnite. Read from{' '}
              {p.configs.length === 1 ? 'Xenia’s config' : `${p.configs.length} Xenia configs`} (
              {p.configs.map(c => c.split(/[\\/]/).pop()).join(', ')}).
            </small>
          </div>
        </div>
      )}
    </Card>
  )
}
