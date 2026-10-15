import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api } from '../api'
import { formatAgo } from '../format'
import type { SaveVsCloud, SwitchCopy } from '../types'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

interface Props {
  gameId: string
  gameName: string
  machineName: string
  /** Bumped by the page after every sync it runs. */
  refreshKey: number
  /** The synced copy moved to another emulator: the shell re-reads the game, so the page's header and source follow. */
  onChanged: () => void
}

/** One emulator install on the picker: a row from this machine, or one that isn't installed here. */
type Choice = SwitchCopy

/** The Switch emulators EmuDeck installs, always offered so one not installed yet can be picked and explained. */
const LISTED = ['Eden', 'Citron', 'Yuzu', 'Ryujinx']

/** Which save goes into the emulator picked: the one played here (the synced copy's), the cloud's, or its own. */
type Carry = 'here' | 'cloud' | 'own'

type Ask =
  | { kind: 'switch'; to: Choice }
  | { kind: 'absent'; to: Choice }
  | { kind: 'no-slot'; to: Choice }
  | { kind: 'take-cloud' }
  | { kind: 'upload' }

/**
 * A Switch game's Emulator settings (SaveLocker's "which emulator plays it", direction A, 2026-10-15): which emulator the
 * game plays in on this device is one visible choice, and every action names it. The status line compares that emulator's
 * save with the cloud's ("Put the cloud save in Yuzu"); the picker shows every Switch emulator install here, plus the ones
 * EmuDeck offers and any the game's other machines use, with what each holds. Picking one moves the save to it: the save
 * played here when it is the newest, the cloud's when that is, the user's pick when both changed — so the game carries on
 * in the new emulator, and on every device, from where it was left.
 */
export function SwitchPlaysIn({ gameId, gameName, machineName, refreshKey, onChanged }: Props) {
  const [rows, setRows] = useState<SwitchCopy[] | null>(null)
  const [cloud, setCloud] = useState<SaveVsCloud | null>(null)
  const [working, setWorking] = useState<string | null>(null)
  const [ask, setAsk] = useState<Ask | null>(null)
  const [message, setMessage] = useState<{ tone: 'ok' | 'warn'; text: string } | null>(null)

  const loadCloud = useCallback(() => {
    api.vsCloud(gameId).then(setCloud).catch(() => setCloud({ state: 'unreachable', headMachineName: null, headCreatedAt: null, emulators: [] }))
  }, [gameId])
  const load = useCallback(() => {
    api.switchCopies(gameId).then(setRows).catch(() => setRows([]))
    loadCloud()
  }, [gameId, loadCloud])
  useEffect(() => { load() }, [load, refreshKey])
  if (!rows) return null

  const synced = rows.find(r => r.synced) ?? null
  const S = synced?.emulator ?? ''
  const absent = (e: string): Choice => ({
    emulator: e, user: null, folder: null, synced: false, state: 'not-installed', writtenUtc: null, keepInSync: false,
    canCopyHere: false, canMakeSynced: false, note: null, tellWhenReady: false, saves: null, install: null,
  })
  const has = (e: string) => rows.some(r => r.emulator.toLowerCase() === e.toLowerCase())
  const choices: Choice[] = [
    ...rows,
    ...[...LISTED, ...(cloud?.emulators ?? [])]
      .filter((e, i, all) => !has(e) && all.findIndex(x => x.toLowerCase() === e.toLowerCase()) === i)
      .map(absent),
  ].sort((a, b) => a.emulator.localeCompare(b.emulator) || (a.install ?? '').localeCompare(b.install ?? ''))
  const twice = (c: Choice) => choices.filter(x => x.emulator === c.emulator).length > 1
  const label = (c: Choice) => twice(c) && c.install ? `${c.emulator} · ${c.install}` : c.emulator
  const elsewhere = (e: string) => cloud?.emulators.some(x => x.toLowerCase() === e.toLowerCase()) ?? false
  const hasCloud = cloud != null && cloud.state !== 'no-cloud' && cloud.state !== 'unreachable'
  const cloudFrom = cloud?.headMachineName && cloud.headCreatedAt
    ? `${cloud.headMachineName}, ${formatAgo(cloud.headCreatedAt)}` : 'the cloud'

  /** One action on one install; false (and the reason shown) when it didn't happen. */
  async function act(c: Pick<Choice, 'emulator' | 'saves'>, action: string): Promise<boolean> {
    try {
      setRows(await api.switchCopyAction(gameId, c.emulator, action, c.saves))
      return true
    } catch (e) {
      setMessage({ tone: 'warn', text: e instanceof Error ? e.message : String(e) })
      load()
      return false
    }
  }

  async function run(text: string, steps: () => Promise<string | null>) {
    setWorking(text)
    try {
      const done = await steps()
      if (done) setMessage({ tone: 'ok', text: done })
      setAsk(null)
    } finally {
      setWorking(null)
      loadCloud()
    }
  }

  const takeCloud = () => synced && run(`Putting the cloud save in ${S}…`, async () =>
    await act(synced, 'take-cloud') ? `The cloud save from ${cloudFrom} is in ${S} now. Open ${gameName} in ${S} to play it.` : null)
  const upload = () => synced && run(`Uploading ${S}'s save…`, async () =>
    await act(synced, 'upload') ? `${S}'s save is the cloud save now. The one it replaced is under Versions.` : null)

  /** Make `to` the emulator that syncs, with the save `carry` names in it. */
  const switchTo = (to: Choice, carry: Carry) => run(`Moving ${gameName} to ${label(to)}…`, async () => {
    const name = label(to)
    if (carry === 'here' && synced && to.state !== 'same' && !await act(to, 'copy-here')) return null
    if (!await act(to, 'make-synced')) return null
    onChanged()
    if (carry === 'cloud') {
      return await act(to, 'take-cloud')
        ? `${gameName} plays in ${name} now, with the cloud save from ${cloudFrom}.` : null
    }
    if (carry === 'here' && synced) {
      if (cloud?.state === 'same') return `${gameName} plays in ${name} now, with the save you played in ${S}.`
      return await act(to, 'upload')
        ? `${gameName} plays in ${name} now, with the save you played in ${S}. It’s the cloud save too, so your other devices get it.`
        : null
    }
    return `${gameName} plays in ${name} now, with its own save.${synced ? ` ${S}’s save is left as it was.` : ''}`
  })
  const tell = (to: Choice, on: boolean) => run(on ? `Asking to be told about ${to.emulator}…` : `Stopping…`, async () =>
    await act(to, on ? 'tell-when-ready' : 'stop-telling')
      ? on ? `You'll be told when ${to.emulator} can sync ${gameName} here.` : `You won't be told about ${to.emulator}.`
      : null)

  function choose(c: Choice) {
    setMessage(null)
    if (c.synced) return
    if (c.state === 'not-installed') setAsk({ kind: 'absent', to: c })
    else if (c.state === 'no-slot') setAsk({ kind: 'no-slot', to: c })
    else setAsk({ kind: 'switch', to: c })
  }

  return (
    <>
      {synced && cloud && <Status cloud={cloud} synced={synced} gameName={gameName} cloudFrom={cloudFrom}
        busy={working != null} onTakeCloud={() => setAsk({ kind: 'take-cloud' })} onUpload={() => setAsk({ kind: 'upload' })} />}
      {message && <Banner tone={message.tone} title={message.text} />}

      <Card title={`On ${machineName}, you play ${gameName} in`}>
        <div className="sl-stack">
          <p className="sl-plays__lede">
            Pick the emulator you’ll play in. Your save moves to it, and from then on it’s the one that syncs: a newer save
            from another device goes into it, and what you play in it uploads.
          </p>
          <div className="sl-plays" role="radiogroup" aria-label={`The emulator ${gameName} plays in on ${machineName}`}>
            {choices.map(c => (
              <button
                key={`${c.emulator}|${c.saves ?? ''}`}
                type="button"
                role="radio"
                aria-checked={c.synced}
                className={`sl-plays__opt${c.state === 'not-installed' ? ' sl-plays__opt--absent' : ''}`}
                data-pad-label={c.synced ? undefined : c.state === 'not-installed' ? 'How to get it' : `Play in ${c.emulator}`}
                disabled={working != null}
                onClick={() => choose(c)}
                title={c.folder ?? undefined}
              >
                <span className="sl-plays__radio" aria-hidden="true" />
                <span className="sl-plays__txt">
                  <b>{label(c)}</b>
                  {lines(c, S, machineName, elsewhere(c.emulator)).map((l, i) => <span key={i} className={l.tone ? `sl-plays__line--${l.tone}` : undefined}>{l.text}</span>)}
                </span>
              </button>
            ))}
          </div>
          {!synced && (
            <p className="sl-plays__lede">No Switch emulator here holds the folder {gameName} syncs. Pick one to sync it.</p>
          )}
          <small className="sl-plays__lede">
            A save about to be replaced is zipped first, the last five per game, in the agent’s <code>emulator-copies</code> folder.
            The emulator you leave keeps its save as it was.
          </small>
        </div>
      </Card>

      {ask && (
        <AskDialog
          ask={ask} synced={synced} label={label} gameName={gameName} machineName={machineName} cloud={cloud} cloudFrom={cloudFrom}
          hasCloud={hasCloud} working={working} onClose={() => setAsk(null)}
          onTakeCloud={() => void takeCloud()} onUpload={() => void upload()}
          onSwitch={(to, carry) => void switchTo(to, carry)} onTell={(to, on) => void tell(to, on)}
          onAskTakeCloud={() => setAsk({ kind: 'take-cloud' })}
        />
      )}
    </>
  )
}

/** What one emulator holds, said plainly, a line each. */
function lines(c: Choice, synced: string, machineName: string, elsewhere: boolean): { text: string; tone?: 'acc' | 'info' }[] {
  const saved = c.writtenUtc ? `saved ${formatAgo(c.writtenUtc)}` : null
  const out: { text: string; tone?: 'acc' | 'info' }[] = []
  switch (c.state) {
    case 'synced': out.push({ text: saved ? `Has your save · ${saved}` : 'No save here yet' }); break
    case 'same': out.push({ text: `Same save as ${synced}${saved ? ` · ${saved}` : ''}` }); break
    case 'newer': out.push({ text: `Its own save, newer than ${synced}’s · ${saved}` }); break
    case 'older': out.push({ text: `Its own save, older than ${synced}’s · ${saved}` }); break
    case 'conflict': out.push({ text: `Its save and ${synced}’s both changed` }); break
    case 'empty': out.push({ text: 'Installed · no save of this game yet' }); break
    case 'no-slot': out.push({ text: c.note?.startsWith('No user') ? 'Installed · no user yet' : 'Installed · hasn’t started this game' }); break
    case 'not-installed': out.push({ text: `Not installed on ${machineName}` }); break
  }
  if (c.synced) out.push({ text: 'Syncs with the cloud', tone: 'acc' })
  else if (elsewhere && c.state === 'not-installed') out.push({ text: `Another device plays it in ${c.emulator}`, tone: 'info' })
  if (c.tellWhenReady) out.push({ text: 'You’ll be told when it’s ready', tone: 'info' })
  return out
}

function Status({ cloud, synced, gameName, cloudFrom, busy, onTakeCloud, onUpload }: {
  cloud: SaveVsCloud; synced: SwitchCopy; gameName: string; cloudFrom: string; busy: boolean
  onTakeCloud: () => void; onUpload: () => void
}) {
  const S = synced.emulator
  const mine = synced.writtenUtc ? `from ${formatAgo(synced.writtenUtc)}` : 'here'
  const put = <Button size="sm" variant="primary" data-pad-label="Cloud save in" disabled={busy} onClick={onTakeCloud}>Put the cloud save in {S}</Button>
  switch (cloud.state) {
    case 'same':
      return <Banner tone="ok" title={`${S} has the cloud save.`} detail={`Open ${gameName} in ${S} to play it.`} />
    case 'cloud-newer':
      return <Banner tone="warn" title={`The cloud has a newer save than ${S} here.`}
        detail={`From ${cloudFrom}. Opening ${gameName} in ${S} now gives you ${S}’s older save.`} action={put} />
    case 'here-newer':
      return <Banner tone="warn" title={`${S} has a save the cloud doesn’t have yet.`}
        detail="It uploads with the next sync, or now."
        action={<Button size="sm" data-pad-label="Upload" disabled={busy} onClick={onUpload}>Upload {S}’s save</Button>} />
    case 'different':
      return <Banner tone="crit" title="Two different saves."
        detail={`The cloud has ${cloudFrom}; ${S} here has its own ${mine}. Opening ${gameName} in ${S} now gives you ${S}’s. Keep the one you want to carry on from.`}
        action={<span className="sl-plays__acts">
          <Button size="sm" data-pad-label="Keep and upload" disabled={busy} onClick={onUpload}>Keep {S}’s and upload it</Button>
          {put}
        </span>} />
    case 'no-cloud':
      return <Banner tone="ok" title={`The cloud has no save of ${gameName} yet.`} detail={`${S}’s save uploads with the next sync.`} />
    default:
      return <Banner tone="warn" title={`Can’t reach the server to compare ${S}’s save with the cloud’s.`} />
  }
}

function AskDialog({ ask, synced, label, gameName, machineName, cloud, cloudFrom, hasCloud, working, onClose, onTakeCloud, onUpload, onSwitch, onTell, onAskTakeCloud }: {
  ask: Ask; synced: SwitchCopy | null; label: (c: Choice) => string; gameName: string; machineName: string
  cloud: SaveVsCloud | null; cloudFrom: string; hasCloud: boolean; working: string | null; onClose: () => void
  onTakeCloud: () => void; onUpload: () => void; onSwitch: (to: Choice, carry: Carry) => void
  onTell: (to: Choice, on: boolean) => void; onAskTakeCloud: () => void
}) {
  const S = synced?.emulator ?? ''
  const busy = working != null
  let title: ReactNode
  let body: ReactNode
  let actions: ReactNode
  const cancel = <Button disabled={busy} onClick={onClose}>Cancel</Button>

  if (ask.kind === 'take-cloud') {
    title = `Put the cloud save in ${S}?`
    body = <>
      <p>The cloud save from <b>{cloudFrom}</b> replaces {S}’s save on {machineName}, in:</p>
      <code className="sl-ask__path">{synced?.folder}</code>
      <p>{S}’s current save is zipped first, with the last five kept in the agent’s <code>emulator-copies</code> folder.</p>
    </>
    actions = <>{cancel}<Button variant="primary" data-pad-label="Replace" disabled={busy} onClick={onTakeCloud}>Replace {S}’s save</Button></>
  } else if (ask.kind === 'upload') {
    title = `Upload ${S}’s save?`
    body = <p>{S}’s save on {machineName} becomes the cloud save, and every device gets it. The cloud save it replaces
      {cloud?.headMachineName ? <>, from <b>{cloudFrom}</b>,</> : null} stays under Versions.</p>
    actions = <>{cancel}<Button variant="primary" data-pad-label="Upload" disabled={busy} onClick={onUpload}>Upload {S}’s save</Button></>
  } else if (ask.kind === 'switch') {
    const to = ask.to
    const name = label(to)
    const ownSave = to.state !== 'empty'
    const state = cloud?.state
    // Which save carries on: the one played here when the cloud has nothing newer, the cloud's when only it moved on.
    const fromHere = !!synced && (state === 'same' || state === 'here-newer' || state === 'no-cloud' || state === 'unreachable')
    const fromCloud = hasCloud && (!synced || state === 'cloud-newer')
    const both = !!synced && state === 'different'
    const playedHere = synced?.writtenUtc ? `saved ${formatAgo(synced.writtenUtc)}` : 'here'
    title = `Play ${gameName} in ${name}?`
    body = <>
      <p>{name} becomes the emulator {gameName} syncs through on {machineName}. {both
        ? `The save you played in ${S} and the cloud’s are different: pick the one to carry on from.`
        : fromCloud
          ? `The cloud save from ${cloudFrom} goes into it.`
          : fromHere
            ? `The save you played in ${S} (${playedHere}) moves into it${state === 'same' ? '' : ', and uploads so your other devices get it'}.`
            : ''}</p>
      <div className="sl-ask__versus">
        {synced && <div><span>Stops syncing</span><b>{S}</b>{synced.writtenUtc ? `Keeps its save, ${formatAgo(synced.writtenUtc)}` : 'No save'}</div>}
        <div><span>Plays it from now</span><b>{name}</b>{ownSave ? `Its own save is zipped first` : 'No save of its own yet'}</div>
      </div>
    </>
    const keepOwn = ownSave && <Button variant="quiet" data-pad-label="Keep its own save" disabled={busy}
      onClick={() => onSwitch(to, 'own')}>Keep {name}’s own save</Button>
    actions = both
      ? <>{cancel}{keepOwn}
          <Button data-pad-label="Cloud save" disabled={busy} onClick={() => onSwitch(to, 'cloud')}>With the cloud save ({cloudFrom})</Button>
          <Button variant="primary" data-pad-label={`${S}'s save`} disabled={busy} onClick={() => onSwitch(to, 'here')}>With {S}’s save ({playedHere})</Button>
        </>
      : <>{cancel}{keepOwn}
          <Button variant="primary" data-pad-label={`Play in ${to.emulator}`} disabled={busy}
            onClick={() => onSwitch(to, fromCloud ? 'cloud' : fromHere ? 'here' : 'own')}>Play in {name}</Button>
        </>
  } else {
    const to = ask.to
    const ryujinx = to.emulator.toLowerCase() === 'ryujinx'
    const absent = ask.kind === 'absent'
    title = absent ? `${to.emulator} isn’t installed on ${machineName}` : ryujinx ? `${to.emulator} hasn’t started ${gameName} yet` : `${to.emulator} has no user yet`
    body = <>
      <p>{absent
        ? `SaveLocker can only put a save into an emulator that’s here.${ryujinx ? '' : ` ${to.emulator} and the other Yuzu-based emulators read the same save files, so your save moves over as it is.`}`
        : ryujinx
          ? `${to.emulator} makes a game’s save folder the first time it starts the game, and SaveLocker never writes ${to.emulator}’s save list itself.`
          : `${to.emulator} files a save under its user, and has none yet.`}</p>
      <ol className="sl-ask__steps">
        {absent && <li>Open <b>EmuDeck</b> → <b>Manage Emulators</b> → <b>{to.emulator}</b> → Install.</li>}
        {(absent || ryujinx)
          ? <li>Start {gameName} in <b>{to.emulator}</b> once, and quit at the title screen.</li>
          : <li>Open <b>{to.emulator}</b> once, so it makes its user.</li>}
        <li>Come back here and pick {to.emulator}{to.tellWhenReady ? ' — you’ll be told when it’s ready.' : ', or ask to be told when it’s ready.'}</li>
      </ol>
    </>
    actions = <>
      <Button disabled={busy} onClick={onClose}>Close</Button>
      {synced && hasCloud && cloud?.state === 'cloud-newer' && (
        <Button data-pad-label="Cloud save in" disabled={busy} onClick={onAskTakeCloud}>Put the cloud save in {S} for now</Button>
      )}
      {to.tellWhenReady
        ? <Button data-pad-label="Stop" disabled={busy} onClick={() => onTell(to, false)}>Don’t tell me</Button>
        : <Button variant="primary" data-pad-label="Tell me" disabled={busy} onClick={() => onTell(to, true)}>Tell me when {to.emulator} is ready</Button>}
    </>
  }

  return (
    <div
      role="dialog" aria-modal="true" aria-label={typeof title === 'string' ? title : undefined}
      onKeyDown={e => { if (e.key === 'Escape' && !busy) { e.stopPropagation(); onClose() } }}
      style={{
        position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
      }}
    >
      <div className="sl-ask">
        <div className="sl-ask__title">{title}</div>
        <div className="sl-ask__body">{working ? <p className="sl-ask__working">{working}</p> : body}</div>
        <div className="sl-ask__actions">{actions}</div>
      </div>
    </div>
  )
}
