import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api } from '../api'
import { formatAgo } from '../format'
import type { SaveVsCloud, SwitchCopy } from '../types'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { SwitchCopiesCard } from './SwitchCopiesCard'

interface Props {
  gameId: string
  gameName: string
  machineName: string
  /** Bumped by the page after every sync it runs. */
  refreshKey: number
  /** The synced copy moved to another emulator: the shell re-reads the game, so the page's header and source follow. */
  onChanged: () => void
}

/** One emulator on the picker: a row from this machine, or one another machine plays the game in that isn't here. */
type Choice = SwitchCopy

type Ask =
  | { kind: 'switch'; to: Choice }
  | { kind: 'absent'; to: Choice }
  | { kind: 'no-slot'; to: Choice }
  | { kind: 'take-cloud' }
  | { kind: 'upload' }

/**
 * A Switch game's Emulator settings (SaveLocker's "which emulator plays it", direction A, 2026-10-15): which emulator the
 * game plays in on this device is one visible choice, and every action names it. A cloud save picked on the Deck used to
 * land in whichever emulator the scan had chosen, with nothing on screen saying which; here the status line compares that
 * emulator's save with the cloud's, its buttons say "Put the cloud save in Yuzu", and the picker shows every Switch
 * emulator here plus any the game's other machines play it in, with what each holds and what it takes to switch to it.
 * Below it, the other emulators' copies (copy here, keep in step).
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
  const choices: Choice[] = [
    ...rows,
    ...(cloud?.emulators ?? [])
      .filter(e => !rows.some(r => r.emulator.toLowerCase() === e.toLowerCase()))
      .map(e => ({
        emulator: e, user: null, folder: null, synced: false, state: 'not-installed', writtenUtc: null,
        keepInSync: false, canCopyHere: false, canMakeSynced: false, note: null, tellWhenReady: false,
      })),
  ].sort((a, b) => a.emulator.localeCompare(b.emulator))
  const elsewhere = (e: string) => cloud?.emulators.some(x => x.toLowerCase() === e.toLowerCase()) ?? false
  const hasCloud = cloud != null && cloud.state !== 'no-cloud' && cloud.state !== 'unreachable'
  const cloudFrom = cloud?.headMachineName && cloud.headCreatedAt
    ? `${cloud.headMachineName}, ${formatAgo(cloud.headCreatedAt)}` : 'the cloud'

  /** One action on one emulator; false (and the reason shown) when it didn't happen. */
  async function act(emulator: string, action: string): Promise<boolean> {
    try {
      setRows(await api.switchCopyAction(gameId, emulator, action))
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

  const takeCloud = () => run(`Putting the cloud save in ${S}…`, async () =>
    await act(S, 'take-cloud') ? `The cloud save from ${cloudFrom} is in ${S} now. Open ${gameName} in ${S} to play it.` : null)
  const upload = () => run(`Uploading ${S}'s save…`, async () =>
    await act(S, 'upload') ? `${S}'s save is the cloud save now. The one it replaced is under Versions.` : null)
  const switchTo = (to: string, thenCloud: boolean) => run(`Making ${to} the emulator that syncs…`, async () => {
    if (!await act(to, 'make-synced')) return null
    onChanged()
    if (!thenCloud) return `${gameName} syncs through ${to} on ${machineName} now. ${S}'s save is left as it was.`
    return await act(to, 'take-cloud')
      ? `${gameName} syncs through ${to} now, and the cloud save from ${cloudFrom} is in it.`
      : null
  })
  const tell = (to: string, on: boolean) => run(on ? `Asking to be told about ${to}…` : `Stopping…`, async () =>
    await act(to, on ? 'tell-when-ready' : 'stop-telling')
      ? on ? `You'll be told when ${to} can sync ${gameName} here.` : `You won't be told about ${to}.`
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
            Only this emulator’s save syncs with the cloud: a cloud save goes into it, and it’s what uploads. The others are
            left alone unless you copy into them below.
          </p>
          <div className="sl-plays" role="radiogroup" aria-label={`The emulator ${gameName} plays in on ${machineName}`}>
            {choices.map(c => (
              <button
                key={c.emulator}
                type="button"
                role="radio"
                aria-checked={c.synced}
                className={`sl-plays__opt${c.state === 'not-installed' ? ' sl-plays__opt--absent' : ''}`}
                data-pad-label={c.synced ? undefined : c.state === 'not-installed' ? 'How to get it' : `Play in ${c.emulator}`}
                disabled={working != null}
                onClick={() => choose(c)}
              >
                <span className="sl-plays__radio" aria-hidden="true" />
                <span className="sl-plays__txt">
                  <b>{c.emulator}</b>
                  {lines(c, S, machineName, elsewhere(c.emulator)).map((l, i) => <span key={i} className={l.tone ? `sl-plays__line--${l.tone}` : undefined}>{l.text}</span>)}
                </span>
              </button>
            ))}
          </div>
          {!synced && (
            <p className="sl-plays__lede">No Switch emulator here holds the folder {gameName} syncs. Pick one to sync it.</p>
          )}
        </div>
      </Card>

      <SwitchCopiesCard
        rows={rows.filter(r => r.state !== 'not-installed')}
        gameName={gameName}
        machineName={machineName}
        busy={working != null}
        onAct={async (emulator, action, done) => { if (await act(emulator, action)) setMessage({ tone: 'ok', text: done }) }}
      />

      {ask && (
        <AskDialog
          ask={ask} synced={synced} gameName={gameName} machineName={machineName} cloud={cloud} cloudFrom={cloudFrom}
          hasCloud={hasCloud} working={working} onClose={() => setAsk(null)}
          onTakeCloud={() => void takeCloud()} onUpload={() => void upload()}
          onSwitch={(to, thenCloud) => void switchTo(to, thenCloud)} onTell={(to, on) => void tell(to, on)}
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
    case 'synced': out.push({ text: saved ? `Has a save · ${saved}` : 'No save here yet' }); break
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
        detail={`The cloud has ${cloudFrom}; ${S} here has its own ${mine}. Opening ${gameName} in ${S} now gives you ${S}’s.`}
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

function AskDialog({ ask, synced, gameName, machineName, cloud, cloudFrom, hasCloud, working, onClose, onTakeCloud, onUpload, onSwitch, onTell, onAskTakeCloud }: {
  ask: Ask; synced: SwitchCopy | null; gameName: string; machineName: string; cloud: SaveVsCloud | null; cloudFrom: string
  hasCloud: boolean; working: string | null; onClose: () => void; onTakeCloud: () => void; onUpload: () => void
  onSwitch: (to: string, thenCloud: boolean) => void; onTell: (to: string, on: boolean) => void; onAskTakeCloud: () => void
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
    const to = ask.to.emulator
    const empty = ask.to.state === 'empty'
    title = `Play ${gameName} in ${to} on ${machineName}?`
    body = <>
      <p>{to} becomes the emulator that syncs: cloud saves go into it, and its save is what uploads.
        {synced ? ` ${S}’s save is left as it is, and stops syncing.` : ''}</p>
      <div className="sl-ask__versus">
        {synced && <div><span>Stops syncing</span><b>{S}</b>{synced.writtenUtc ? `Saved ${formatAgo(synced.writtenUtc)}` : 'No save'}</div>}
        <div><span>Starts syncing</span><b>{to}</b>{empty ? 'No save yet' : ask.to.writtenUtc ? `Saved ${formatAgo(ask.to.writtenUtc)}` : 'Has a save'}</div>
      </div>
      {hasCloud && <p>{empty ? `${to} has no save of its own, so the cloud save goes in.` : `Then put the cloud save from ${cloudFrom} in ${to}, or keep ${to}’s own save and upload it later.`}</p>}
    </>
    actions = <>
      {cancel}
      {!empty && <Button variant={hasCloud ? undefined : 'primary'} data-pad-label="Switch, keep its save" disabled={busy}
        onClick={() => onSwitch(to, false)}>{hasCloud ? `Switch, keep ${to}’s save` : `Play it in ${to}`}</Button>}
      {hasCloud && <Button variant="primary" data-pad-label="Switch, cloud save in" disabled={busy}
        onClick={() => onSwitch(to, true)}>Switch and put the cloud save in {to}</Button>}
    </>
  } else {
    const to = ask.to.emulator
    const ryujinx = to.toLowerCase() === 'ryujinx'
    const absent = ask.kind === 'absent'
    title = absent ? `${to} isn’t installed on ${machineName}` : ryujinx ? `${to} hasn’t started ${gameName} yet` : `${to} has no user yet`
    body = <>
      <p>{absent
        ? `SaveLocker can only put a save into an emulator that’s here.${ryujinx ? '' : ` ${to} and the other Yuzu-based emulators read the same save files, so nothing is lost either way.`}`
        : ryujinx
          ? `${to} makes a game’s save folder the first time it starts the game, and SaveLocker never writes ${to}’s save list itself.`
          : `${to} files a save under its user, and has none yet.`}</p>
      <ol className="sl-ask__steps">
        {absent && <li>Open <b>EmuDeck</b> → <b>Manage Emulators</b> → <b>{to}</b> → Install.</li>}
        {(absent || ryujinx)
          ? <li>Start {gameName} in <b>{to}</b> once, and quit at the title screen.</li>
          : <li>Open <b>{to}</b> once, so it makes its user.</li>}
        <li>Come back here and pick {to}{ask.to.tellWhenReady ? ' — you’ll be told when it’s ready.' : ', or ask to be told when it’s ready.'}</li>
      </ol>
    </>
    actions = <>
      <Button disabled={busy} onClick={onClose}>Close</Button>
      {synced && hasCloud && cloud?.state !== 'same' && (
        <Button data-pad-label="Cloud save in" disabled={busy} onClick={onAskTakeCloud}>Put the cloud save in {S} for now</Button>
      )}
      {ask.to.tellWhenReady
        ? <Button data-pad-label="Stop" disabled={busy} onClick={() => onTell(to, false)}>Don’t tell me</Button>
        : <Button variant="primary" data-pad-label="Tell me" disabled={busy} onClick={() => onTell(to, true)}>Tell me when {to} is ready</Button>}
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
