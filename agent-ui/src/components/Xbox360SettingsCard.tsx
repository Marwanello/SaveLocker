import { useEffect, useState } from 'react'
import { api } from '../api'
import type { Xbox360Settings } from '../types'
import { Card } from './ui/Card'

interface Props {
  /** Emulator settings shows one setting per section; Settings shows both. */
  show?: 'profile' | 'dlc'
}

const TITLE = { profile: "This machine's setting", dlc: 'Sharing DLC' } as const

/**
 * This machine's Xbox 360 (Xenia) settings (SaveLocker tasks/emulator-saves Phase 5c): what to do before a game starts when
 * Xenia would sign in as another profile than the game's saves are under, and what to do when another machine asks for
 * this one's DLC. Each saves as soon as it is picked, and covers every Xbox 360 game on this machine.
 */
export function Xbox360SettingsCard({ show }: Props) {
  const [settings, setSettings] = useState<Xbox360Settings | null>(null)
  const [note, setNote] = useState<string | null>(null)

  useEffect(() => { api.xbox360Settings().then(setSettings).catch(() => setSettings(null)) }, [])
  if (!settings) return null

  async function save(next: Xbox360Settings) {
    setSettings(next)
    try { setSettings(await api.setXbox360Settings(next)); setNote('Saved.') }
    catch (err) { setNote(err instanceof Error ? err.message : 'Could not save.') }
  }

  return (
    <Card
      title={show ? TITLE[show] : 'Xbox 360 (Xenia)'}
      headerRight={show ? <span style={{ fontSize: 11.5, color: 'var(--color-dim)' }}>every Xbox 360 game on this machine</span> : undefined}
    >
      <div className="sl-stack">
        {show !== 'dlc' && <div className="sl-field">
          <label htmlFor="x360-profile">When Xenia would start as another profile</label>
          <select
            id="x360-profile" className="sl-input" style={{ maxWidth: 360 }} value={settings.profileAtLaunch}
            onChange={e => void save({ ...settings, profileAtLaunch: e.target.value as Xbox360Settings['profileAtLaunch'] })}
          >
            <option value="Ask">Switch to the game's profile, and ask where I can (Decky, Playnite)</option>
            <option value="Switch">Switch to the game's profile without asking</option>
            <option value="Leave">Leave Xenia as it is</option>
          </select>
          <small>A game's saves and achievements are under one Xbox profile. Started as another, it starts without them.</small>
        </div>}
        {show !== 'profile' && <div className="sl-field">
          <label htmlFor="x360-dlc">When another machine asks for DLC this one has</label>
          <select
            id="x360-dlc" className="sl-input" style={{ maxWidth: 360 }} value={settings.dlcSend}
            onChange={e => void save({ ...settings, dlcSend: e.target.value as Xbox360Settings['dlcSend'] })}
          >
            <option value="Auto">Send it automatically</option>
            <option value="Ask">Ask me first</option>
            <option value="Never">Never send it</option>
          </select>
          <small>Packs pass through your server and are deleted there once delivered, or after the console's time limit.</small>
        </div>}
        {note && <small>{note}</small>}
      </div>
    </Card>
  )
}
