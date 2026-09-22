import { useState } from 'react'
import { api } from '../api'
import { ACCENTS, MARKS, THEMES, isLight, saveLook } from '../appearance'
import type { AccentId, Look, Theme } from '../appearance'
import { formatAgo } from '../format'
import type { AgentAppearance } from '../types'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Mark } from './ui/Mark'
import { Seg } from './ui/Seg'
import { Switch } from './ui/Switch'

interface Props {
  appearance: AgentAppearance | null
  /** The agent answered with the new state; the page adopts it at once instead of waiting for a poll. */
  onChanged: (next: AgentAppearance) => void
}

/**
 * "Follow the console" — the per-machine override (docs/tasks/checkpoint-ui/plan.md, Phase 4). On, this
 * machine draws the theme, accent and icon the console pushed on its last heartbeat. Off, it keeps its own
 * and ignores pushes (they are still stored, so turning this back on shows the console's CURRENT look).
 * Turning it off changes nothing on screen: the machine's own look starts as whatever it is drawing now.
 */
export function AppearanceCard({ appearance, onChanged }: Props) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const send = async (follow: boolean, look?: Look) => {
    setBusy(true)
    setError('')
    try {
      onChanged(await saveLook(() => api.setAppearance({ follow, look })))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the appearance.')
    } finally {
      setBusy(false)
    }
  }

  if (!appearance) return null

  const own = appearance.local as Look
  const light = isLight(appearance.effective.theme as Theme)
  const chip = appearance.follow
    ? appearance.console
      ? <Chip tone="ok">In sync with the console</Chip>
      : <Chip>Console not sharing</Chip>
    : <Chip>Local override</Chip>

  return (
    <Card title="Appearance" headerRight={chip}>
      <div className="sl-setting">
        <div className="sl-setting__label">
          Follow the console
          <small>
            Use the theme, accent and icon set on the server. Turn this off to keep a different look on this
            machine only.
          </small>
        </div>
        <Switch
          aria-label="Follow the console"
          checked={appearance.follow}
          disabled={busy}
          onChange={v => void send(v)}
        />
      </div>

      {appearance.follow ? (
        <div className="sl-setting__note">
          {appearance.console
            ? <>Drawing the console&rsquo;s look: {label(appearance.console as Look)}
                {appearance.consoleAppliedAt ? <> &middot; changed {formatAgo(appearance.consoleAppliedAt)}</> : null}.</>
            : 'The console has not sent a look yet, or is not sharing one. This machine draws the default.'}
        </div>
      ) : (
        <div className="sl-setting__grid">
          <div className="sl-setting__key">Theme</div>
          <div>
            <Seg<Theme>
              aria-label="Theme"
              value={own.theme}
              options={THEMES.map(t => ({ value: t.id, label: t.name }))}
              disabled={busy}
              onChange={v => void send(false, { ...own, theme: v })}
            />
          </div>

          <div className="sl-setting__key">Accent</div>
          <div className="sl-setting__swatches">
            {(Object.keys(ACCENTS) as AccentId[]).map(id => (
              <button
                key={id}
                type="button"
                className="sl-swatch"
                title={ACCENTS[id].name}
                aria-label={ACCENTS[id].name}
                aria-pressed={own.accent === id}
                disabled={busy}
                style={{ background: light ? ACCENTS[id].light : ACCENTS[id].dark }}
                onClick={() => void send(false, { ...own, accent: id })}
              />
            ))}
            <span className="sl-setting__hint">{ACCENTS[own.accent].name}</span>
          </div>

          <div className="sl-setting__key">App icon</div>
          <div className="sl-setting__swatches">
            {MARKS.map(m => (
              <button
                key={m.id}
                type="button"
                className="sl-swatch sl-swatch--tile"
                title={m.name}
                aria-label={m.name}
                aria-pressed={own.mark === m.id}
                disabled={busy}
                onClick={() => void send(false, { ...own, mark: m.id })}
              >
                <Mark id={m.id} size={20} title={m.name} />
              </button>
            ))}
          </div>
        </div>
      )}

      {error && <div role="alert" className="sl-setting__error">{error}</div>}
    </Card>
  )
}

function label(look: Look): string {
  const theme = THEMES.find(t => t.id === look.theme)?.name ?? look.theme
  const mark = MARKS.find(m => m.id === look.mark)?.name ?? look.mark
  return `${theme} theme, ${ACCENTS[look.accent].name} accent, ${mark}`
}
