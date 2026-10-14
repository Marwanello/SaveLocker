import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { formatAgo } from '../format'
import type { SwitchCopy } from '../types'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip, type Tone } from './ui/Chip'
import { Switch } from './ui/Switch'

interface Props {
  gameId: string
  gameName: string
  machineName: string
  /** Bumped by the page after every sync it runs. */
  refreshKey: number
  /** The synced copy moved to another emulator: the shell re-reads the game, so the page's header and source follow. */
  onChanged: () => void
}

const STATE: Record<string, { label: string; tone: Tone }> = {
  same: { label: 'Same save', tone: 'ok' },
  newer: { label: 'Different save, newer', tone: 'warn' },
  older: { label: 'Different save, older', tone: 'default' },
  empty: { label: 'No save yet', tone: 'default' },
  'no-slot': { label: 'Not started here yet', tone: 'default' },
  conflict: { label: 'Both changed', tone: 'crit' },
}

/**
 * A Switch game in every Switch emulator on this machine (SaveLocker tasks/emulator-saves Phase 6). One copy syncs with
 * the server and is marked; any other can take the synced save once, be kept in step with it (the one played last wins),
 * or become the copy that syncs. When both changed since they were last the same, the user picks.
 * <p>
 * Every action is a real button or switch, in reading order down each panel's right column, so the controller's spatial
 * navigation reaches them with up/down and A presses the one in focus; the synced panel has none and is passed over.
 */
export function SwitchCopiesCard({ gameId, gameName, machineName, refreshKey, onChanged }: Props) {
  const [rows, setRows] = useState<SwitchCopy[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ tone: 'ok' | 'warn'; text: string } | null>(null)
  const load = useCallback(() => { api.switchCopies(gameId).then(setRows).catch(() => setRows([])) }, [gameId])
  useEffect(() => { load() }, [load, refreshKey])
  if (!rows) return null

  const synced = rows.find(r => r.synced)
  const syncedName = synced?.emulator ?? 'the synced copy'

  async function act(emulator: string, action: string, done: string) {
    setBusy(true)
    try {
      setRows(await api.switchCopyAction(gameId, emulator, action))
      setMessage({ tone: 'ok', text: done })
      if (action === 'make-synced') onChanged()
    } catch (e) {
      setMessage({ tone: 'warn', text: e instanceof Error ? e.message : String(e) })
      load()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card title="Switch emulators on this device">
      <div className="sl-stack">
        <p style={{ fontSize: 12.5, color: 'var(--color-dim)', margin: 0, lineHeight: 1.55 }}>
          {synced
            ? <>The <b style={{ color: 'var(--color-fg)' }}>{synced.emulator}</b> copy syncs with the server. If you also play {gameName} in
                another emulator on {machineName}, copy the save there or keep the two in step. With <b>Keep in sync</b> on, whichever
                was played last wins, and nothing is copied while an emulator is open.</>
            : <>No Switch emulator here holds the folder this game syncs.</>}
        </p>
        {message && <Banner tone={message.tone} title={message.text} />}
        <div className="sl-emucopies">
          {rows.map(r => {
            const state = STATE[r.state] ?? { label: r.state, tone: 'default' as Tone }
            const actionable = !r.synced && r.state !== 'no-slot'
            return (
              <div key={r.emulator} className={`sl-emucopy${r.synced ? ' sl-emucopy--synced' : ''}`}>
                <div className="sl-emucopy__main">
                  <div className="sl-emucopy__name">
                    {r.emulator}
                    {r.synced ? <Chip tone="ok">Synced copy</Chip> : <Chip tone={state.tone}>{state.label}</Chip>}
                  </div>
                  <div className="sl-emucopy__meta">
                    {[r.user && `User ${r.user}`, r.writtenUtc && `saved ${formatAgo(r.writtenUtc)}`,
                      r.synced && 'syncs with the server'].filter(Boolean).join(' · ')}
                  </div>
                  {r.folder && <code className="sl-emucopy__path">{r.folder}</code>}
                  {r.note && <div className="sl-emucopy__meta">{r.note}</div>}
                  {r.state === 'conflict' && (
                    <div className="sl-emucopy__ask">
                      Both saves changed since they were last the same. Keep:
                      <Button size="sm" data-pad-label="Keep this one" disabled={busy}
                        onClick={() => void act(r.emulator, 'keep-synced', `Kept ${syncedName}'s save and copied it to ${r.emulator}.`)}>
                        {synced ? `${synced.emulator}’s` : 'The synced one'}
                      </Button>
                      <Button size="sm" data-pad-label="Keep this one" disabled={busy}
                        onClick={() => void act(r.emulator, 'keep-copy', `Kept ${r.emulator}'s save. It syncs with the next push.`)}>
                        {r.emulator}’s
                      </Button>
                    </div>
                  )}
                </div>
                {actionable && (
                  <div className="sl-emucopy__actions">
                    <label className="sl-emucopy__toggle">
                      Keep in sync
                      <Switch
                        aria-label={`Keep ${r.emulator}'s copy in sync`}
                        checked={r.keepInSync}
                        disabled={busy}
                        onChange={next => void act(r.emulator, next ? 'keep-in-sync' : 'stop-keeping',
                          next ? `${r.emulator}'s copy is kept in step with ${syncedName}'s.` : `${r.emulator}'s copy is no longer kept in step.`)}
                      />
                    </label>
                    {r.canCopyHere && r.state !== 'conflict' && (
                      <Button size="sm" data-pad-label="Copy here" disabled={busy}
                        onClick={() => void act(r.emulator, 'copy-here', `Copied the synced save to ${r.emulator}. The old one is backed up.`)}>
                        Copy the synced save here
                      </Button>
                    )}
                    {r.canMakeSynced && (
                      <Button size="sm" variant="quiet" data-pad-label="Make synced" disabled={busy}
                        onClick={() => void act(r.emulator, 'make-synced', `${r.emulator}'s copy now syncs with the server.`)}>
                        Make this the synced copy
                      </Button>
                    )}
                  </div>
                )}
              </div>
            )
          })}
        </div>
        {rows.length <= 1 && (
          <p style={{ fontSize: 12.5, margin: 0 }}>No other Switch emulator is installed on {machineName}.</p>
        )}
        <small style={{ color: 'var(--color-dim)', lineHeight: 1.55 }}>
          A save about to be overwritten is zipped first, the last five per game, in the agent's <code>emulator-copies</code> folder.
          A Switch save doesn’t belong to an emulator user, so a copy goes under that emulator’s current user.
        </small>
      </div>
    </Card>
  )
}
