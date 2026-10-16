import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api } from '../api'
import { formatAgo } from '../format'
import type { SwitchUserSharing } from '../types'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

interface Props {
  /** On a game's page: shown only while there is something to do (users differ, a choice waits, it can be put back). */
  onlyWhenNeeded?: boolean
}

type Report = SwitchUserSharing['devices'][number]['users'][number]
type Ask = { kind: 'share'; id: string; name: string } | { kind: 'undo' } | { kind: 'stop' }

const short = (id: string) => `${id.slice(0, 8)}…`
const label = (u: { emulator: string; install: string }) => `${u.emulator} (${u.install})`

/**
 * One Switch user on every device (SaveLocker's SwitchUserSharing, 2026-10-16). Some Switch games record the emulator user
 * who played them — Animal Crossing links each island resident to one — so devices whose Yuzu-based emulators have
 * different users make the game ask to relink on every switch. The user picks one user (never picked for them); every
 * Yuzu-based install on every device then takes its ID, with its saves backed up, moved, and its games following. Titles
 * saved under both users wait for the user's pick; each device can put its original user back.
 */
export function SwitchUserCard({ onlyWhenNeeded }: Props) {
  const [state, setState] = useState<SwitchUserSharing | null>(null)
  const [picked, setPicked] = useState<string | null>(null)
  const [choosing, setChoosing] = useState(false)
  const [keep, setKeep] = useState<Record<string, 'here' | 'shared'>>({})
  const [ask, setAsk] = useState<Ask | null>(null)
  const [working, setWorking] = useState(false)
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null)

  const load = useCallback(() => { api.switchUsers().then(setState).catch(() => setState(null)) }, [])
  useEffect(() => { load() }, [load])
  if (!state) return null

  const shared = state.shared ?? null
  const currents = state.devices.flatMap(d => d.users.filter(u => u.current).map(u => ({ device: d, user: u })))
  const differ = new Set(currents.map(c => c.user.id)).size > 1
  const here = state.here
  const collisions = here.flatMap(h => h.collisions.map(c => ({ install: h, c })))
  const pendingHere = shared != null && !state.declined && here.some(h => h.userId && !h.matches)
  const canUndo = here.some(h => h.canUndo)
  if (onlyWhenNeeded && !differ && !pendingHere && collisions.length === 0 && !(shared && state.declined)) return null

  // Every user any device has, once, with where it is: the choice is the user's, never made for them.
  const choices = new Map<string, { name: string; where: string[] }>()
  for (const d of state.devices)
    for (const u of d.users) {
      const entry = choices.get(u.id) ?? { name: u.name, where: [] }
      entry.where.push(`${d.machineId === state.thisMachineId ? 'this device' : d.machineName}, ${label(u)}${u.current ? '' : ', not current'}`)
      choices.set(u.id, entry)
    }

  async function run(work: () => Promise<{ state: SwitchUserSharing; message: string; ok: boolean }>) {
    setWorking(true)
    try {
      const r = await work()
      setState(r.state)
      setMessage({ ok: r.ok, text: r.message })
      if (r.ok) { setChoosing(false); setPicked(null); setKeep({}) }
      setAsk(null)
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : String(e) })
      setAsk(null)
    } finally { setWorking(false) }
  }

  const deviceLine = (machineName: string, isThis: boolean, users: Report[]) => {
    const current = users.filter(u => u.current)
    const uses = shared && current.length > 0 && current.every(u => u.id === shared.id)
    return (
      <li key={machineName}>
        <b>{machineName}{isThis ? ' (this device)' : ''}</b>
        {' · '}
        {current.map(u => `${label(u)}: ${u.name} · ${short(u.id)}`).join('; ') || 'no Yuzu-based emulator'}
        {shared && current.length > 0 && (
          <span className={uses ? 'sl-switchuser__ok' : 'sl-switchuser__wait'}>
            {uses ? ' · uses it' : isThis ? '' : ' · switches on its next check'}
          </span>
        )}
      </li>
    )
  }

  const picker = (
    <div className="sl-stack">
      <div className="sl-plays" role="radiogroup" aria-label="The Switch user every device uses">
        {[...choices.entries()].map(([id, c]) => (
          <button
            key={id} type="button" role="radio" aria-checked={picked === id}
            className="sl-plays__opt" data-pad-label={`Pick ${c.name}`} disabled={working}
            onClick={() => setPicked(id)}
          >
            <span className="sl-plays__radio" aria-hidden="true" />
            <span className="sl-plays__txt">
              <b>{c.name} · {short(id)}</b>
              {c.where.map(w => <span key={w}>{w}</span>)}
            </span>
          </button>
        ))}
      </div>
      <div className="sl-switchuser__acts">
        {shared && <Button disabled={working} onClick={() => { setChoosing(false); setPicked(null) }}>Cancel</Button>}
        <Button
          variant="primary" data-pad-label="Use everywhere" disabled={working || !picked || picked === shared?.id}
          onClick={() => picked && setAsk({ kind: 'share', id: picked, name: choices.get(picked)!.name })}
        >
          {picked ? `Use ${choices.get(picked)!.name} on every device` : 'Pick a user'}
        </Button>
      </div>
    </div>
  )

  return (
    <Card title="One Switch user on every device" headerRight={<span className="sl-switchuser__hint">Eden, Citron, Yuzu and the other Yuzu-based emulators</span>}>
      <div className="sl-stack">
        <p className="sl-plays__lede">
          Some Switch games record the emulator user who played them: Animal Crossing links each island resident to one. When
          your devices’ emulators have different users, such a game asks to link its residents again each time you switch
          device. Give every device the same user, and it never asks.
        </p>
        {message && <Banner tone={message.ok ? 'ok' : 'warn'} title={message.text} />}
        {!state.reachable && <Banner tone="warn" title="Can’t reach the server, which knows your other devices’ users." />}

        {state.devices.length > 0 && (
          <ul className="sl-switchuser__devices">
            {state.devices.map(d => deviceLine(d.machineName, d.machineId === state.thisMachineId, d.users))}
          </ul>
        )}

        {shared && !choosing && (
          <Banner
            tone={pendingHere || collisions.length > 0 ? 'warn' : 'ok'}
            title={`${shared.name} (${short(shared.id)}) is the user every device uses.`}
            detail={state.declined
              ? 'This device put its own user back, so it keeps it.'
              : pendingHere && collisions.length === 0
                ? 'This device switches once no Switch emulator is open, or now.'
                : undefined}
            action={<span className="sl-switchuser__acts">
              {(pendingHere || state.declined) && collisions.length === 0 && (
                <Button size="sm" variant="primary" data-pad-label="Switch now" disabled={working}
                  onClick={() => void run(() => api.applySwitchUser())}>{state.declined ? 'Use it here too' : 'Switch this device now'}</Button>
              )}
              <Button size="sm" data-pad-label="Choose another" disabled={working} onClick={() => setChoosing(true)}>Choose another user</Button>
            </span>}
          />
        )}

        {here.filter(h => h.note).map(h => <Banner key={h.saves} tone="warn" title={`${label(h)}: ${h.note}`} />)}

        {collisions.length > 0 && shared && (
          <div className="sl-stack">
            <b>Saved under both users on this device: pick which to keep</b>
            {collisions.map(({ install, c }) => (
              <div key={`${install.saves}|${c.titleId}`} className="sl-switchuser__collision" role="radiogroup" aria-label={c.gameName ?? c.titleId}>
                <span>{c.gameName ?? c.titleId}</span>
                <Button size="sm" variant={keep[c.titleId] === 'here' ? 'primary' : 'default'} role="radio" aria-checked={keep[c.titleId] === 'here'}
                  data-pad-label="Keep this device's" disabled={working} onClick={() => setKeep(k => ({ ...k, [c.titleId]: 'here' }))}>
                  This device’s ({label(install)}, {short(install.userId ?? '')}){c.hereWritten ? `, saved ${formatAgo(c.hereWritten)}` : ''}
                </Button>
                <Button size="sm" variant={keep[c.titleId] === 'shared' ? 'primary' : 'default'} role="radio" aria-checked={keep[c.titleId] === 'shared'}
                  data-pad-label={`Keep ${shared.name}'s`} disabled={working} onClick={() => setKeep(k => ({ ...k, [c.titleId]: 'shared' }))}>
                  The one already under {short(shared.id)}{c.sharedWritten ? `, saved ${formatAgo(c.sharedWritten)}` : ''}
                </Button>
              </div>
            ))}
            <small className="sl-plays__lede">The one not kept is zipped in the agent’s <code>switch-users</code> folder.</small>
            <div className="sl-switchuser__acts">
              <Button variant="primary" data-pad-label="Switch this device" disabled={working || collisions.some(({ c }) => !keep[c.titleId])}
                onClick={() => void run(() => api.applySwitchUser(keep))}>Switch this device to {shared.name}</Button>
            </div>
          </div>
        )}

        {(!shared || choosing) && (choices.size > 0
          ? <>{!shared && <b>{differ ? 'Your devices use different users. Pick the one every device keeps:' : 'Pick the user every device keeps:'}</b>}{picker}</>
          : <p className="sl-plays__lede">No device has reported a Yuzu-based emulator user yet.</p>)}

        {(canUndo || shared) && (
          <div className="sl-switchuser__acts">
            {canUndo && <Button variant="quiet" data-pad-label="Put back" disabled={working} onClick={() => setAsk({ kind: 'undo' })}>Put back this device’s original user</Button>}
            {shared && <Button variant="quiet" data-pad-label="Stop sharing" disabled={working} onClick={() => setAsk({ kind: 'stop' })}>Stop sharing a user</Button>}
          </div>
        )}
      </div>

      {ask && <Confirm ask={ask} working={working} onClose={() => setAsk(null)}
        onYes={() => void run(() => ask.kind === 'share' ? api.shareSwitchUser(ask.id, ask.name)
          : ask.kind === 'undo' ? api.undoSwitchUser() : api.stopSharingSwitchUser())} />}
    </Card>
  )
}

function Confirm({ ask, working, onClose, onYes }: { ask: Ask; working: boolean; onClose: () => void; onYes: () => void }) {
  let title: string
  let body: ReactNode
  let yes: string
  if (ask.kind === 'share') {
    title = `Use ${ask.name} (${short(ask.id)}) on every device?`
    yes = `Use ${ask.name} everywhere`
    body = <>
      <p>On every device, each Yuzu-based emulator’s current user takes this user’s ID. Its name stays. On each one, SaveLocker:</p>
      <ol className="sl-ask__steps">
        <li>waits until no Switch emulator is open;</li>
        <li>zips the emulator’s users file and the saves about to move, in the agent’s <code>switch-users</code> folder;</li>
        <li>moves that user’s saves to the new ID’s folder, so every game still finds its save, and the games SaveLocker syncs follow;</li>
        <li>asks you first about any game saved under both users.</li>
      </ol>
      <p>This device switches now; the others on their next check. Ryujinx keeps the user in its own save list, so it’s left as it is.
        Any device can put its original user back.</p>
    </>
  } else if (ask.kind === 'undo') {
    title = 'Put back this device’s original user?'
    yes = 'Put it back'
    body = <p>Each emulator here gets the user it had, from the backup, and its saves move back. This device then keeps its own
      user; the other devices keep the shared one.</p>
  } else {
    title = 'Stop sharing a user?'
    yes = 'Stop sharing'
    body = <p>No device switches to the shared user any more. Nothing is put back: each device keeps the user it has now.</p>
  }
  return (
    <div
      role="dialog" aria-modal="true" aria-label={title}
      onKeyDown={e => { if (e.key === 'Escape' && !working) { e.stopPropagation(); onClose() } }}
      style={{
        position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
      }}
    >
      <div className="sl-ask">
        <div className="sl-ask__title">{title}</div>
        <div className="sl-ask__body">{working ? <p className="sl-ask__working">Working…</p> : body}</div>
        <div className="sl-ask__actions">
          <Button disabled={working} onClick={onClose}>Cancel</Button>
          <Button variant="primary" data-pad-label={yes} disabled={working} onClick={onYes}>{yes}</Button>
        </div>
      </div>
    </div>
  )
}
