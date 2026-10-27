import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { formatBytes } from '../format'
import type { PlaythroughCopies, PlaythroughCopyMode } from '../types'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Seg } from './ui/Seg'

const MODES: { value: PlaythroughCopyMode; label: string; what: string }[] = [
  {
    value: 'UntilUploaded', label: 'Only until uploaded',
    what: 'When you switch playthrough, the save you are leaving is kept until the server confirms it. Never lose a save to a failed push; costs nothing afterwards.',
  },
  {
    value: 'KeepForOffline', label: 'Keep for offline switching',
    what: 'Keeps the latest save of every playthrough this device has played, so you can switch with no server — on a plane, say. The copies take disk space.',
  },
  {
    value: 'Off', label: 'Off',
    what: 'Keeps nothing. For huge saves and little disk. A switch then needs the server, and is refused if it cannot be reached.',
  },
]

/**
 * Settings › Sync › Playthrough copies on this device (tasks/save-playthroughs Phase 5): one row with a three-way switch, the space
 * the copies take, and Clear copies — which never clears a save that has not reached the server yet, and says which it kept.
 */
export function PlaythroughCopiesCard() {
  const [state, setState] = useState<PlaythroughCopies | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)
  const [note, setNote] = useState<string | null>(null)

  const load = useCallback(() => {
    api.playthroughCopies().then(s => { setState(s); setError(null) }).catch(e => setError(e instanceof Error ? e.message : 'Could not read the setting.'))
  }, [])
  useEffect(() => { load() }, [load])

  const mode = state?.mode ?? 'UntilUploaded'
  const what = MODES.find(m => m.value === mode)?.what

  async function choose(next: PlaythroughCopyMode) {
    try { setState(await api.setPlaythroughCopies(next)); setError(null); setNote(null) }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not save the setting.') }
  }

  async function clear() {
    setConfirming(false)
    try {
      const next = await api.clearPlaythroughCopies()
      setState(next)
      setNote(next.notUploaded.length > 0
        ? `Cleared. Kept ${next.notUploaded.length} that ${next.notUploaded.length === 1 ? 'has' : 'have'} not reached the server yet.`
        : 'Cleared.')
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not clear the copies.') }
  }

  return (
    <Card title="Playthrough copies on this device">
      <div className="sl-stack">
        <div className="sl-setting">
          <div className="sl-setting__label">
            Copies kept when you switch
            <small>{what}</small>
          </div>
          <Seg<PlaythroughCopyMode>
            aria-label="Playthrough copies" value={mode} onChange={v => void choose(v)}
            options={MODES.map(m => ({ value: m.value, label: m.label }))}
          />
        </div>
        <div className="sl-inline">
          <span style={{ fontSize: 12.5, color: 'var(--color-dim)' }}>
            {state ? `${formatBytes(state.usedBytes)} used on this device` : 'Reading…'}
          </span>
          {confirming ? (
            <span className="sl-confirm">
              <span>
                Delete this device's copies?
                {state && state.notUploaded.length > 0 &&
                  ` ${state.notUploaded.map(c => `${c.gameName} · ${c.playthroughName ?? 'a playthrough'}`).join(', ')} ${state.notUploaded.length === 1 ? 'has' : 'have'} not reached the server yet and stays.`}
              </span>
              <Button size="sm" variant="primary" onClick={() => void clear()}>Clear copies</Button>
              <Button size="sm" variant="quiet" onClick={() => setConfirming(false)}>Keep</Button>
            </span>
          ) : (
            <Button size="sm" disabled={!state || state.usedBytes === 0} data-pad-label="Clear copies" onClick={() => setConfirming(true)}>Clear copies…</Button>
          )}
        </div>
        {state && state.notUploaded.length > 0 && !confirming && (
          <div className="sl-setting__note" style={{ marginTop: 0 }}>
            Waiting for the server: {state.notUploaded.map(c => `${c.gameName} · ${c.playthroughName ?? 'a playthrough'}`).join(', ')}. They go up on their own when it is reachable.
          </div>
        )}
        {note && <div role="status" style={{ fontSize: 12.5, color: 'var(--color-dim)' }}>{note}</div>}
        {error && <div role="alert" style={{ fontSize: 12.5, color: 'var(--color-watch-ink)' }}>{error}</div>}
      </div>
    </Card>
  )
}
