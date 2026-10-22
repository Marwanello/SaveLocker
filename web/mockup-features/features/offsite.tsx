import { useState, type ReactNode } from 'react'
import { Banner } from '../../src/components/ui/Banner'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { DataTable } from '../../src/components/ui/DataTable'
import { Icon } from '../../src/components/ui/Icon'
import { Seg } from '../../src/components/ui/Seg'
import { Switch } from '../../src/components/ui/Switch'
import { toast } from '../../src/toast'
import { endFlow, getState, runFlow, setState, useMock } from '../store'
import { Choice, Dialog, Steps, fieldCls } from './common'

/* ------------------------------------------------------------------ providers and sign-in */

export const PROVIDERS = [
  { id: 'gdrive', name: 'Google Drive', kind: 'oauth', note: 'Free 15 GB', about: true },
  { id: 'onedrive', name: 'OneDrive', kind: 'oauth', note: 'Free 5 GB', about: true },
  { id: 'dropbox', name: 'Dropbox', kind: 'oauth', note: 'Free 2 GB', about: true },
  { id: 's3', name: 'S3-compatible', kind: 's3', note: 'Backblaze B2, Cloudflare R2, Wasabi, MinIO, AWS', about: false },
  { id: 'sftp', name: 'SFTP', kind: 'sftp', note: 'Another server or NAS over SSH', about: true },
  { id: 'smb', name: 'SMB share', kind: 'smb', note: 'A Windows or NAS share', about: true },
  { id: 'webdav', name: 'WebDAV', kind: 'webdav', note: 'Nextcloud, ownCloud', about: true },
  { id: 'folder', name: 'Folder', kind: 'folder', note: 'Another disk mounted in the container', about: true },
] as const
type Provider = (typeof PROVIDERS)[number]

const FIELDS: Record<string, [string, string][]> = {
  s3: [['Provider', 'Backblaze B2'], ['Endpoint', 'https://s3.us-west-004.backblazeb2.com'], ['Bucket', 'savelocker-offsite'], ['Access key ID', '004a1f…'], ['Secret access key', '••••••••••••']],
  sftp: [['Host', 'nas.local'], ['Port', '22'], ['User', 'marwan'], ['Password or key', '••••••••']],
  smb: [['Host', '192.168.1.20'], ['Share', 'backups'], ['User', 'marwan'], ['Password', '••••••••']],
  webdav: [['URL', 'https://cloud.example.com/remote.php/dav/files/me'], ['User', 'me'], ['App password', '••••••••']],
  folder: [['Path in the container', '/offsite']],
}

const Label = ({ children }: { children: ReactNode }) => <span className="block text-[10px] tracking-[0.13em] uppercase text-faint mb-1.5">{children}</span>

export function ProviderPicker({ value, onChange }: { value: string; onChange: (id: string) => void }) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(170px,1fr))] gap-2" role="radiogroup" aria-label="Where">
      {PROVIDERS.map(p => <Choice key={p.id} checked={value === p.id} onPick={() => onChange(p.id)} title={p.name} sub={p.note} />)}
    </div>
  )
}

export function SignInFields({ p, connected, onConnected }: { p: Provider; connected: boolean; onConnected: () => void }) {
  const [how, setHow] = useState<'browser' | 'rclone'>('browser')
  const [opened, setOpened] = useState(false)
  if (p.kind === 'oauth') {
    return (
      <div className="flex flex-col gap-3">
        <Seg size="sm" value={how} onChange={setHow} aria-label="How to sign in"
          options={[{ value: 'browser', label: 'Sign in from this browser' }, { value: 'rclone', label: 'Use rclone on another computer' }]} />
        {how === 'browser' ? (
          <ol className="flex flex-col gap-3 text-[12.5px] text-dim list-decimal pl-5">
            <li>Open the {p.name} sign-in page and allow SaveLocker access.
              <div className="mt-1.5"><Button size="sm" onClick={() => { setOpened(true); toast(`Opened the ${p.name} sign-in page in a new tab.`) }}>Open sign-in page</Button></div></li>
            <li>Your browser then shows “This site can’t be reached”. That’s expected. Copy that page’s address and paste it here.
              <div className="mt-1.5 flex gap-1.5 flex-wrap"><input className={`${fieldCls} flex-1 min-w-[220px] font-mono`} placeholder="http://127.0.0.1:53682/?state=…&code=…" aria-label="Address" />
                <Button size="sm" variant="primary" disabled={!opened} onClick={onConnected}>Connect</Button></div></li>
          </ol>
        ) : (
          <div className="flex flex-col gap-2 text-[12.5px] text-dim">
            <span>1 · On any computer with a browser, run:</span>
            <span className="flex gap-1.5"><code className="flex-1 font-mono text-[12px] bg-raise border border-line rounded-lg px-2.5 py-1.5 text-fg">rclone authorize "{p.id === 'gdrive' ? 'drive' : p.id}"</code>
              <Button size="sm" onClick={() => toast('Copied.')}>Copy</Button></span>
            <span>2 · Paste what it prints:</span>
            <textarea className={`${fieldCls} font-mono h-16`} placeholder='{"access_token":…}' aria-label="Token" />
            <div><Button size="sm" variant="primary" onClick={onConnected}>Connect</Button></div>
          </div>
        )}
        {connected && <Banner tone="safe" title={`Connected to ${p.name}`}>The sign-in is stored encrypted on this server.</Banner>}
        {p.id === 'gdrive' && <details className="text-[12px] text-dim"><summary className="cursor-pointer">Advanced: your own client ID</summary>
          <input className={`${fieldCls} mt-2 w-full`} placeholder="Client ID" aria-label="Client ID" />
          <p className="mt-1">rclone’s shared ID is rate-limited by Google. Help › Off-site copies says how to make your own.</p></details>}
      </div>
    )
  }
  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-[repeat(auto-fit,minmax(200px,1fr))] gap-3">
        {FIELDS[p.kind].map(([l, ph]) => (
          <label key={l} className="flex flex-col"><Label>{l}</Label>
            <input className={fieldCls} placeholder={ph} type={ph.includes('••') ? 'password' : 'text'} /></label>
        ))}
      </div>
      {p.kind === 'folder' && <p className="text-xs text-dim">Add it as a volume in the compose file, e.g. <code className="font-mono">- /mnt/disk2/offsite:/offsite</code>.</p>}
      {p.kind === 's3' && <Banner tone="watch" title="S3 doesn’t report free space">Set a size cap in the next step so SaveLocker knows when to stop.</Banner>}
    </div>
  )
}

export function WhatFields() {
  const [backups, setBackups] = useState(true)
  const [saves, setSaves] = useState(true)
  const [hist, setHist] = useState('thin')
  return (
    <div className="flex flex-col gap-3">
      <Row title="Server backups" sub="The database and every playthrough’s latest save, one zip per backup"><Switch checked={backups} onChange={setBackups} aria-label="Server backups" /></Row>
      <Row title="Saves and their history" sub="Normal zips with readable names, you can unzip into a save folder by hand"><Switch checked={saves} onChange={setSaves} aria-label="Saves" /></Row>
      {saves && <div className="flex flex-col gap-2" role="radiogroup" aria-label="History on the remote">
        <Label>History on the remote</Label>
        <Choice checked={hist === 'latest'} onPick={() => setHist('latest')} title="Latest only" sub="Each playthrough’s latest save, nothing older" />
        <Choice checked={hist === 'count'} onPick={() => setHist('count')} title="Last 5 versions" sub="The newest 5 of each playthrough" />
        <Choice checked={hist === 'thin'} onPick={() => setHist('thin')} title="Thinning · Short" sub="Everything from 2 days, one a day for 14 days, one a week for 8 weeks" />
        <span className="text-xs text-dim">Named versions always go. Each game can have its own setting later.</span>
      </div>}
      <label className="flex items-center gap-2 text-[12.5px] text-dim">Size cap <input type="number" defaultValue={10} className={`${fieldCls} w-[80px] tabular-nums`} /> GB · 0 = no cap · about 6.4 GB now</label>
    </div>
  )
}

export function AdvancedFields() {
  const [enc, setEnc] = useState(false)
  const [saved, setSaved] = useState(false)
  return (
    <div className="flex flex-col gap-3">
      <Row title="Encrypt the copy" sub="Off by default, so you can open the files yourself. On: you get a recovery key to keep somewhere else."><Switch checked={enc} onChange={setEnc} aria-label="Encrypt" /></Row>
      {enc && <Banner tone="watch" title="Recovery key: tide-ember-9412-lantern-orbit-55" action={<Button size="sm" onClick={() => toast('Downloaded savelocker-recovery-key.txt.')}>Download</Button>}>
        If this server’s disk is lost, the off-site copy can only be read with this key.
        <label className="flex items-center gap-2 mt-2 text-fg"><input type="checkbox" className="accent-[var(--color-accent)]" checked={saved} onChange={e => setSaved(e.target.checked)} />I saved the recovery key</label>
      </Banner>}
      <div className="grid grid-cols-[repeat(auto-fit,minmax(200px,1fr))] gap-3">
        <label className="flex flex-col"><Label>When to copy</Label><select className={fieldCls}><option>After each backup, and hourly for saves</option><option>Daily at 04:00</option></select></label>
        <label className="flex flex-col"><Label>Upload speed limit</Label><select className={fieldCls}><option>No limit</option><option>2 MB/s</option><option>500 KB/s</option></select></label>
      </div>
    </div>
  )
}

function Row({ title, sub, children }: { title: string; sub: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 py-2.5 border-b border-row">
      <div className="min-w-0"><div className="text-[13px] font-semibold text-fg">{title}</div><div className="text-xs text-dim mt-0.5">{sub}</div></div>
      {children}
    </div>
  )
}

export function TestBlock({ p }: { p: Provider }) {
  const flow = useMock(s => s.flow)
  const test = () => runFlow([
    { t: 'Write a test file' }, { t: 'Read it back' }, { t: 'List the folder' }, { t: 'Delete the test file' },
    { t: p.about ? 'Check free space' : 'Free space: not reported by this provider' },
  ], { failAt: getState().sim === 'bad' ? 0 : -1, failMsg: p.kind === 'oauth' ? 'The sign-in was refused (401). Sign in again.' : 'Couldn’t connect: wrong key or bucket name (403).' })
  return (
    <div className="flex flex-col gap-3">
      {flow ? <Steps /> : <p className="text-[12.5px] text-dim">Writes, reads back, lists and deletes a small file on {p.name}.</p>}
      {flow?.state === 'ok' && <Banner tone="safe" title={`${p.name} works`}>{p.about ? '11.2 GB free of 15 GB.' : 'Free space isn’t reported; the 10 GB cap applies.'}</Banner>}
      {flow?.state !== 'run' && <div><Button size="sm" onClick={test}><Icon name="refresh-cw" size={13} />{flow ? 'Test again' : 'Test connection'}</Button></div>}
    </div>
  )
}

/* ------------------------------------------------------------------ setup (st-setup) */

export function OffsiteSetup() {
  const variant = useMock(s => s.variant)
  const on = useMock(s => s.offsite.on)
  const flow = useMock(s => s.flow)
  const [prov, setProv] = useState('gdrive')
  const [connected, setConnected] = useState(false)
  const [step, setStep] = useState(0)
  const [wizard, setWizard] = useState(false)
  const [picked, setPicked] = useState(false)
  const [drawer, setDrawer] = useState(false)
  const p = PROVIDERS.find(x => x.id === prov)!
  const tested = flow?.state === 'ok'
  const finish = () => { setState({ offsite: { ...getState().offsite, on: true, provider: p.name } }); endFlow(); setWizard(false); toast(`Off-site copies to ${p.name} are on. The first copy is starting.`) }
  const pickProv = (id: string) => { setProv(id); setConnected(false); endFlow() }
  const done = on && <Banner tone="safe" title={`Off-site copies to ${getState().offsite.provider} are on`}>First copy: 6.4 GB, about 25 minutes at full speed.</Banner>

  if (variant === 'A') {
    const STEPS = ['Where', 'Sign in', 'What to copy', 'Advanced', 'Test']
    const can = step === 1 ? (p.kind !== 'oauth' || connected) : true
    return (
      <>
        {done}
        <Card title="Off-site copies" headerRight={on ? <Chip tone="ok">On · {getState().offsite.provider}</Chip> : <Chip>Off</Chip>}>
          <p className="text-[12.5px] text-dim mb-3">Copies the server backups, and every save with its history if you want, to a cloud drive or another machine. If this server’s disk dies, your saves are still there as normal zips you can open.</p>
          <Button variant={on ? 'default' : 'primary'} onClick={() => { setStep(0); setWizard(true) }}>{on ? 'Edit…' : 'Set up off-site copies'}</Button>
        </Card>
        {wizard && (
          <Dialog wide title="Set up off-site copies" onClose={() => setWizard(false)}
            sub={<span className="flex gap-1.5 flex-wrap mt-1">{STEPS.map((s, i) => <Chip key={s} tone={i === step ? 'crit' : i < step ? 'ok' : 'default'}>{i + 1} · {s}</Chip>)}</span>}
            actions={<>{step ? <Button variant="quiet" onClick={() => setStep(step - 1)}>Back</Button> : <Button variant="quiet" onClick={() => setWizard(false)}>Cancel</Button>}
              {step < 4 ? <Button variant="primary" disabled={!can} onClick={() => setStep(step + 1)}>Next</Button> : <Button variant="primary" disabled={!tested} onClick={finish}>Turn on off-site copies</Button>}</>}>
            {[<ProviderPicker key="0" value={prov} onChange={pickProv} />, <SignInFields key="1" p={p} connected={connected} onConnected={() => setConnected(true)} />, <WhatFields key="2" />, <AdvancedFields key="3" />, <TestBlock key="4" p={p} />][step]}
          </Dialog>
        )}
      </>
    )
  }
  if (variant === 'B') {
    const sec = (n: number, t: string, body: ReactNode) => (
      <section className="flex flex-col gap-2.5 py-4 border-b border-line last:border-b-0"><Label>{n} · {t}</Label>{body}</section>
    )
    return (
      <>
        {done}
        <Card title="Off-site copies" headerRight={on ? <Chip tone="ok">On</Chip> : <Chip>Not set up</Chip>}>
          {sec(1, 'Where', <ProviderPicker value={prov} onChange={pickProv} />)}
          {sec(2, `Sign in to ${p.name}`, <SignInFields p={p} connected={connected} onConnected={() => setConnected(true)} />)}
          {sec(3, 'What to copy', <WhatFields />)}
          {sec(4, 'Advanced', <AdvancedFields />)}
          {sec(5, 'Test', <TestBlock p={p} />)}
          <div className="flex items-center justify-between gap-3 pt-3"><span className="text-[12px] text-dim">{tested ? 'Test passed' : 'Run the test to save'}</span><Button variant="primary" disabled={!tested} onClick={finish}>Save and turn on</Button></div>
        </Card>
      </>
    )
  }
  return (
    <>
      {done}
      <Card title="Off-site copies · where should they go?">
        <div className="grid grid-cols-[repeat(auto-fill,minmax(150px,1fr))] gap-2.5">
          {PROVIDERS.map(x => (
            <button key={x.id} type="button" onClick={() => { pickProv(x.id); setPicked(true) }}
              className={`text-left rounded-xl border px-3.5 py-3 cursor-pointer ${picked && prov === x.id ? 'border-accent-line bg-accent-soft' : 'border-line bg-raise hover:bg-hover'}`}>
              <span className="flex items-center gap-2 text-[13px] font-semibold text-fg"><Icon name={x.kind === 'folder' ? 'folder' : 'library'} size={15} />{x.name}</span>
              <span className="block text-xs text-dim mt-1">{x.note}</span>
            </button>
          ))}
        </div>
      </Card>
      {picked && <Card title={p.name} headerRight={<Button size="sm" variant="quiet" onClick={() => setPicked(false)}>Change</Button>}>
        <div className="flex flex-col gap-4"><SignInFields p={p} connected={connected} onConnected={() => setConnected(true)} /><TestBlock p={p} /></div>
      </Card>}
      {picked && tested && <Card title="Ready · here’s what will be copied">
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1.5 text-[12.5px] mb-3">
          <dt className="text-faint">Backups</dt><dd className="text-fg">Every server backup, 7 kept on {p.name}</dd>
          <dt className="text-faint">Saves</dt><dd className="text-fg">Every playthrough · Thinning · Short · named versions always</dd>
          <dt className="text-faint">Cap</dt><dd className="text-fg">10 GB · about 6.4 GB now</dd>
          <dt className="text-faint">Encryption</dt><dd className="text-fg">Off · files readable by hand</dd>
        </dl>
        <div className="flex gap-2"><Button onClick={() => setDrawer(true)}>Change these…</Button><Button variant="primary" onClick={finish}>Turn on off-site copies</Button></div>
      </Card>}
      {drawer && <Dialog wide title="What to copy" onClose={() => setDrawer(false)} actions={<Button variant="primary" onClick={() => setDrawer(false)}>Done</Button>}><WhatFields /><AdvancedFields /></Dialog>}
    </>
  )
}

/* ------------------------------------------------------------------ status (st-status) */

const GAMES: [string, string, number][] = [['Cyberpunk 2077', '2 playthroughs', 3.1], ['Elden Ring', '4 playthroughs', 0.61], ['Animal Crossing', '3 playthroughs', 0.42], ['Hollow Knight', '1 playthrough', 0.05], ['Stardew Valley', '2 playthroughs', 0.04], ['Server backups', '7 backups', 2.2]]
const TREE = `SaveLocker/
  README.txt
  Elden Ring/
    Main/
      latest.zip
      versions/
        2026-10-21 18-03 · Steam Deck.zip
        2026-10-14 22-10 · Battlestation · Before Malenia.zip
    Hard mode run/
      latest.zip
  Hollow Knight/
    Main/latest.zip
  _server/
    savelocker-20261022-030000-scheduled.zip
  _index.json`

function useRemote() {
  const skip = useMock(s => s.offsite.skip)
  const used = GAMES.reduce((a, g, i) => a + (skip[i] ? 0 : g[2]), 0)
  const toggle = (i: number) => setState({ offsite: { ...getState().offsite, skip: { ...skip, [i]: !skip[i] } } })
  return { skip, used, toggle }
}

function GamesTable() {
  const { skip, toggle } = useRemote()
  return (
    <DataTable caption="Games on the remote" rows={GAMES.map((g, i) => ({ g, i }))} rowKey={r => String(r.i)}
      columns={[
        { head: 'Game', kind: 'k', cell: r => <span>{r.g[0]}<span className="block text-[11.5px] text-dim font-normal">{r.g[1]}</span></span> },
        { head: 'History', cell: r => r.i === 5 ? <span className="text-dim">Keep 7</span> : <select className={`${fieldCls} w-auto`}><option>Default</option><option>Latest only</option><option>Last 5</option></select> },
        { head: 'On the remote', kind: 'n', cell: r => (skip[r.i] ? '—' : `${r.g[2].toFixed(2)} GB`) },
        { head: 'Copy', end: true, cell: r => r.i === 5 ? <Chip>Always</Chip> : <Switch checked={!skip[r.i]} onChange={() => toggle(r.i)} aria-label={`Copy ${r.g[0]}`} /> },
      ]} />
  )
}

function SpaceBar() {
  const { used } = useRemote()
  return (
    <div className="flex flex-col gap-1.5">
      <div className="h-2 rounded-full bg-tile overflow-hidden flex"><i className="block h-full bg-info" style={{ width: `${(used / 15) * 100}%` }} /><i className="block h-full bg-info-soft" style={{ width: `${((10 - used) / 15) * 100}%` }} /></div>
      <div className="flex gap-3.5 flex-wrap text-[11.5px] text-dim"><span>SaveLocker {used.toFixed(1)} GB</span><span>Cap 10 GB</span><span>Google Drive: 11.2 GB free of 15 GB</span></div>
    </div>
  )
}

function RunNow() {
  const flow = useMock(s => s.flow)
  const paused = useMock(s => s.offsite.paused)
  const run = () => runFlow([
    { t: 'Check free space on Google Drive', d: '4.8 GB free' },
    { t: 'Copy 3 new versions', d: 'Elden Ring · Hard mode run ×2, Hollow Knight · Main' },
    { t: 'Update latest.zip for 2 playthroughs' },
    { t: 'Remove 2 old versions the history setting no longer keeps', d: 'Only files this server uploaded' },
    { t: 'Write the index' },
  ], { done: ok => ok && toast('Off-site copy is up to date.') })
  return (
    <span className="flex gap-1.5">
      <Button size="sm" disabled={flow?.state === 'run'} onClick={run}><Icon name="refresh-cw" size={13} />Run now</Button>
      <Button size="sm" onClick={() => setState({ offsite: { ...getState().offsite, paused: !paused } })}>{paused ? 'Resume' : 'Pause'}</Button>
    </span>
  )
}

export function OffsiteStatus() {
  const variant = useMock(s => s.variant)
  const flow = useMock(s => s.flow)
  const paused = useMock(s => s.offsite.paused)
  const { used } = useRemote()
  const [panel, setPanel] = useState<'settings' | 'tree' | null>(null)
  const [tab, setTab] = useState('log')
  const status = (
    <div className="flex items-center gap-2 flex-wrap text-[12.5px] text-dim">
      <Chip tone={paused ? 'warn' : 'ok'}>{paused ? 'Paused by you' : 'Up to date'}</Chip>Google Drive · last copy 18 min ago · next in 42 min
    </div>
  )
  const running = flow && <Banner tone={flow.state === 'ok' ? 'safe' : 'watch'} title={flow.state === 'run' ? 'Copying…' : 'Up to date'} action={flow.state !== 'run' ? <Button size="sm" onClick={endFlow}>Dismiss</Button> : undefined}><Steps /></Banner>
  const tree = <pre className="font-mono text-[12px] leading-[1.8] bg-raise border border-line rounded-xl px-3.5 py-2.5 overflow-x-auto text-fg">{TREE}</pre>
  const hist = <label className="flex items-center gap-2 text-[12.5px] text-dim">History on the remote <select className={`${fieldCls} w-auto`}><option>Thinning · Short</option><option>Latest only</option><option>Last 5 versions</option><option>Thinning · Balanced</option></select></label>

  if (variant === 'A') return (
    <>
      <Card title="Off-site copies" headerRight={<RunNow />}>
        <div className="flex flex-col gap-3">{status}{running}<SpaceBar />
          <div className="flex gap-1.5"><Button size="sm" onClick={() => setPanel('settings')}>Saves and history…</Button><Button size="sm" variant="quiet" onClick={() => setPanel('tree')}><Icon name="folder" size={13} />What’s on the remote</Button></div>
        </div>
      </Card>
      {panel === 'settings' && <Dialog wide title="Saves and history" onClose={() => setPanel(null)} actions={<Button variant="primary" onClick={() => setPanel(null)}>Done</Button>}>
        {hist}<span className="text-xs">Named versions always go; a playthrough’s latest is never removed.</span><GamesTable /></Dialog>}
      {panel === 'tree' && <Dialog wide title="On Google Drive" sub="Normal zips with readable names. Unzip a latest.zip into the game’s save folder to play it without SaveLocker." onClose={() => setPanel(null)}>{tree}</Dialog>}
    </>
  )
  if (variant === 'B') {
    const stat = (l: string, v: string, s: string) => (
      <div className="rounded-xl border border-line bg-panel px-4 py-3"><div className="text-[10px] tracking-[0.12em] uppercase text-faint">{l}</div><div className="text-[22px] font-semibold text-fg tabular-nums">{v}</div><div className="text-xs text-dim">{s}</div></div>
    )
    return (
      <Card title="Off-site copies · Google Drive" headerRight={<RunNow />}>
        <div className="flex flex-col gap-3">{status}{running}
          <div className="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-2.5">{stat('On the remote', `${used.toFixed(1)} GB`, 'of a 10 GB cap')}{stat('Drive free', '11.2 GB', 'of 15 GB')}{stat('Waiting', '3 versions', '41 MB')}{stat('Last copy', '18 min', 'ago, no errors')}</div>
          <SpaceBar />{hist}<GamesTable />
          <details><summary className="text-[12px] text-dim cursor-pointer">What it looks like on the remote</summary>{tree}</details>
        </div>
      </Card>
    )
  }
  const LOG: [string, string, boolean][] = [['18 min ago', 'Copied 3 versions (41 MB), updated 2 latest.zip', false], ['1 h ago', 'Removed 2 old versions of Elden Ring · Main that the history setting no longer keeps', false], ['3 h ago', 'Couldn’t reach Google Drive; retried 4 times, then succeeded', true], ['Today 03:04', 'Copied server backup savelocker-20261022-030000 (2.2 GB)', false]]
  return (
    <Card title="Off-site copies" headerRight={<RunNow />}>
      <div className="flex flex-col gap-3">{status}{running}
        <Seg size="sm" value={tab} onChange={setTab} aria-label="Show" options={[{ value: 'log', label: 'Activity' }, { value: 'games', label: 'Games and history' }, { value: 'remote', label: 'On the remote' }]} />
        {tab === 'log' && <ol className="flex flex-col gap-2.5">{LOG.map(([w, t, warn]) => (
          <li key={w} className="flex gap-2.5 items-start text-[13px] text-fg"><span className={`mt-px w-[18px] h-[18px] rounded-full border grid place-items-center flex-none ${warn ? 'bg-watch-soft border-watch-line text-watch-ink' : 'bg-safe-soft border-safe-line text-safe-ink'}`}><Icon name={warn ? 'x' : 'check'} size={11} /></span><span>{t}<span className="block text-xs text-dim">{w}</span></span></li>
        ))}</ol>}
        {tab === 'games' && <>{hist}<GamesTable /></>}
        {tab === 'remote' && <><SpaceBar />{tree}</>}
      </div>
    </Card>
  )
}

/* ------------------------------------------------------------------ remote full (st-full) */

const REASONS: Record<string, { t: string; s: string; tone: 'watch' | 'accent' }> = {
  full: { t: 'Off-site copies paused: Google Drive is full', s: '41 MB is waiting to copy and the drive has 0.2 GB free', tone: 'watch' },
  cap: { t: 'Off-site copies paused: the 10 GB cap is reached', s: 'The next copy needs 0.6 GB more', tone: 'watch' },
  breaker: { t: 'Off-site copies paused: a run would delete 46 files (5.8 GB)', s: 'That’s more than 20% of the copy, so SaveLocker stopped to ask first', tone: 'accent' },
  expired: { t: 'Off-site copies paused: the Google Drive sign-in expired', s: 'Sign in again to carry on', tone: 'watch' },
}
function options(reason: string): [string, string, string][] {
  if (reason === 'breaker') return [['review', 'Review the 46 files first', 'See which files and why before anything is deleted'], ['allow', 'Delete them', 'Old versions your history setting no longer keeps'], ['keep', 'Keep them and stop removing old versions', 'Sets remote history to keep everything; watch the cap']]
  if (reason === 'expired') return [['reconnect', 'Sign in to Google Drive again', 'Opens the sign-in step of the setup']]
  return [['trim', 'Trim remote history', 'Removes old versions from the remote only · frees 1.9 GB'], ['skip', 'Skip some games', 'Stop copying the biggest games'], ['raise', 'Raise the cap', reason === 'cap' ? 'From 10 GB' : 'Won’t help: the drive itself is full'], ['retry', 'I freed space · try again', 'Checks free space and carries on']]
}
function OptionDetail({ o }: { o: string }) {
  if (o === 'trim' || o === 'review') {
    const rows = o === 'review' ? [['Elden Ring · Main', '22 versions from May–July', ''], ['Cyberpunk 2077 · Main', '14 versions from June', ''], ['Hollow Knight · Main', '10 versions from April', '']] : [['Cyberpunk 2077 · Main', '3 versions from August', '1.2 GB'], ['Elden Ring · Sara’s run', '4 versions older than 2 weeks', '0.14 GB'], ['Server backups', 'The 2 oldest of 7', '0.6 GB']]
    return (
      <>
        <div className="border border-line rounded-xl overflow-hidden">{rows.map(r => (
          <div key={r[0]} className="flex items-center justify-between gap-3 px-3.5 py-2.5 border-b border-row last:border-b-0"><span><b className="text-fg text-[13px]">{r[0]}</b><span className="block text-xs text-dim">{r[1]}</span></span><span className="font-mono text-[11.5px] text-dim">{r[2]}</span></div>
        ))}</div>
        <p className="text-xs">Only on the remote. The server keeps its own history. Named versions and each latest.zip are never removed.</p>
      </>
    )
  }
  if (o === 'skip') return <><GamesTable /><p className="text-xs">Skipped games stay on the remote as they are; they just stop updating.</p></>
  if (o === 'raise') return <label className="flex items-center gap-2 text-[12.5px]"><input type="number" defaultValue={14} className={`${fieldCls} w-[80px]`} /> GB · Google Drive has 15 GB in total</label>
  return null
}
function applyFix(o: string, after: () => void) {
  const msg: Record<string, string> = { trim: 'Trimmed 1.9 GB of old remote versions. Copying 41 MB.', skip: 'Skipping the games you turned off. Copying the rest.', raise: 'The cap is now 14 GB.', retry: 'Found 2.1 GB free. Copying.', review: 'Reviewed. Deleting 46 old files.', allow: 'Deleted 46 old files (5.8 GB).', keep: 'Remote history now keeps everything.', reconnect: 'Signed in again. Copying.' }
  runFlow([{ t: 'Apply your choice' }, { t: 'Check the remote has room' }, { t: 'Resume the off-site copy' }], {
    done: ok => { if (ok) { setState({ offsite: { ...getState().offsite, fixed: msg[o] } }); endFlow(); after() } },
  })
}

/** The console-wide banner (A and B), under the nav like the console’s own errors. */
export function RemoteFullBanner() {
  const variant = useMock(s => s.variant)
  const reason = useMock(s => s.sim)
  const fixed = useMock(s => s.offsite.fixed)
  const flow = useMock(s => s.flow)
  const [open, setOpen] = useState(false)
  const opts = options(reason)
  const [o, setO] = useState(opts[0][0])
  const cur = opts.some(x => x[0] === o) ? o : opts[0][0]
  const r = REASONS[reason]
  if (fixed) return <div className="px-6 pt-3"><Banner tone="safe" title="Off-site copies resumed" action={<Button size="sm" variant="quiet" onClick={() => setState({ offsite: { ...getState().offsite, fixed: null } })}>Dismiss</Button>}>{fixed}</Banner></div>
  const choices = (
    <div className={variant === 'B' ? 'grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-2' : 'flex flex-col gap-2'} role="radiogroup">
      {opts.map(x => <Choice key={x[0]} checked={cur === x[0]} onPick={() => setO(x[0])} title={x[1]} sub={x[2]} />)}
    </div>
  )
  return (
    <div className="px-6 pt-3 flex flex-col gap-3">
      <Banner tone={r.tone} title={r.t} action={<Button size="sm" variant="primary" onClick={() => setOpen(true)}>{reason === 'expired' ? 'Sign in again' : 'Resolve…'}</Button>}>
        {r.s}. Syncing between your devices isn’t affected.
      </Banner>
      {open && variant === 'B' && (
        <Card title="What do you want to do?" headerRight={<Button size="sm" variant="quiet" onClick={() => setOpen(false)}>Close</Button>}>
          <div className="flex flex-col gap-3">{flow ? <Steps /> : <>{choices}<OptionDetail o={cur} /><div className="flex justify-end"><Button variant="primary" onClick={() => applyFix(cur, () => setOpen(false))}>Apply</Button></div></>}</div>
        </Card>
      )}
      {open && variant === 'A' && (
        <Dialog wide title={`Resolve: ${r.t.split(': ')[1]}`} sub={r.s} onClose={() => { endFlow(); setOpen(false) }}
          actions={flow ? undefined : <><Button variant="quiet" onClick={() => setOpen(false)}>Not now</Button><Button variant="primary" onClick={() => applyFix(cur, () => setOpen(false))}>Apply</Button></>}>
          {flow ? <Steps /> : <>{choices}<OptionDetail o={cur} /></>}
        </Dialog>
      )}
    </div>
  )
}

/** C · a page about the remote’s space, shown in Configuration. */
export function MakeRoom() {
  const reason = useMock(s => s.sim)
  const fixed = useMock(s => s.offsite.fixed)
  const flow = useMock(s => s.flow)
  const { used } = useRemote()
  const [keep, setKeep] = useState(5)
  const colors = ['bg-info', 'bg-safe', 'bg-watch', 'bg-accent', 'bg-faint', 'bg-info-line']
  const { skip } = useRemote()
  return (
    <>
      {fixed && <Banner tone="safe" title="Off-site copies resumed">{fixed}</Banner>}
      <Card title="Make room on Google Drive" headerRight={<span className="text-[11.5px] text-dim">{REASONS[reason].s}</span>}>
        <div className="flex flex-col gap-3">
          <div className="h-3.5 rounded-full bg-tile overflow-hidden flex">{GAMES.map((g, i) => <i key={i} className={`block h-full ${colors[i]}`} style={{ width: `${skip[i] ? 0 : (g[2] / 15) * 100}%` }} />)}</div>
          <span className="text-[11.5px] text-dim">SaveLocker uses {used.toFixed(1)} GB of 15 GB; other files on the drive use the rest.</span>
          <GamesTable />
          <div className="flex flex-col gap-1.5 pt-2 border-t border-row">
            <span className="text-[13px] font-semibold text-fg">Keep fewer versions on the remote</span>
            <input type="range" min={1} max={10} value={keep} onChange={e => setKeep(Number(e.target.value))} className="w-full accent-[var(--color-accent)]" aria-label="Versions to keep" />
            <span className="flex justify-between text-[12px] text-dim"><span>Keep the newest <b className="text-fg">{keep}</b> per playthrough</span><span className="font-mono">frees about {((10 - keep) * 0.25).toFixed(2)} GB</span></span>
          </div>
          <div className="flex justify-end gap-2">{flow ? <Steps /> : <><Button variant="quiet" onClick={() => applyFix('retry', () => undefined)}>Try again without changes</Button><Button variant="primary" onClick={() => applyFix('trim', () => undefined)}>Apply and resume</Button></>}</div>
        </div>
      </Card>
    </>
  )
}
