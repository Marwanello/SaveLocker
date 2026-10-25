import { useState } from 'react'
import { Banner } from '../../src/components/ui/Banner'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Mark } from '../../src/components/ui/Mark'
import { toast } from '../../src/toast'
import { endFlow, runFlow, useMock, type Remote } from '../store'
import { useRemotes } from './storage'
import { Choice, Dialog, Steps, fieldCls } from './common'
import { PROVIDERS, ProviderPicker, SignInFields } from './offsite'

const BACKUPS = [
  ['savelocker-20261022-030000-scheduled.zip', 'Today 03:00', '2.2 GB'],
  ['savelocker-20261021-030000-scheduled.zip', 'Yesterday 03:00', '2.2 GB'],
  ['savelocker-20261020-191144-manual.zip', 'Oct 20, 19:11', '2.1 GB'],
]

/** Connect to the remote, pick a server backup there, restore it and adopt the remote. Shared by every variant. */
export function RecoverFlow({ onDone, linked = [] }: { onDone?: () => void; linked?: Remote[] }) {
  const flow = useMock(s => s.flow)
  // The remotes this server already copies to are searched straight away (B): one is opened at once, several are picked
  // from first. With none, ask where to look.
  const [step, setStep] = useState<'remote' | 'connect' | 'pick'>(linked.length > 1 ? 'remote' : linked.length ? 'pick' : 'connect')
  const [from, setFrom] = useState<string | undefined>(linked.length === 1 ? linked[0].provider : undefined)
  const [which, setWhich] = useState(linked[0]?.id ?? '')
  const [prov, setProv] = useState('folder')
  const [connected, setConnected] = useState(false)
  const [pick, setPick] = useState(0)
  const [enc, setEnc] = useState(false)
  const p = PROVIDERS.find(x => x.id === prov)!
  const restore = () => runFlow([
    { t: `Download ${BACKUPS[pick][0]}`, d: BACKUPS[pick][2] },
    { t: 'Check the backup', d: 'Database and 31 saves present' },
    { t: 'Restore the database' },
    { t: 'Put every playthrough’s latest save back' },
    { t: 'Adopt the off-site copy', d: 'Read _index.json · 412 files recognised · nothing deleted' },
    { t: 'Restart the server' },
  ])
  if (flow) return (
    <div className="flex flex-col gap-3">
      <Steps />
      {flow.state === 'ok' && <>
        <Banner tone="safe" title="Your server is back">12 games, 31 playthroughs, 4 devices. Devices reconnect with their existing keys.</Banner>
        <Banner tone="watch" title="Older versions are on the remote too" action={<Button size="sm" onClick={() => toast('Importing 380 older versions in the background.')}>Import older versions</Button>}>
          The backup held only the latest saves. Import the 380 older versions from the off-site copy? (4.1 GB)
        </Banner>
        {onDone && <div className="flex justify-end"><Button variant="primary" onClick={() => { endFlow(); onDone() }}>Open the console</Button></div>}
      </>}
    </div>
  )
  if (step === 'remote') return (
    <div className="flex flex-col gap-3">
      <p className="text-[12.5px] text-dim">This server copies to {linked.length} remotes. Which one should the backup come from?</p>
      <div className="flex flex-col gap-2" role="radiogroup" aria-label="Remote">
        {linked.map((r, i) => <Choice key={r.id} checked={which === r.id} onPick={() => setWhich(r.id)} title={r.provider}
          sub={<><span className="font-mono">{r.where}</span><span className="block">{i === 0 ? '3 server backups · newest today 03:00' : '3 server backups · newest yesterday 03:00'}</span></>} />)}
        <Choice checked={which === 'other'} onPick={() => setWhich('other')} title="Somewhere else" sub="Connect to another remote" />
      </div>
      <div className="flex justify-end"><Button variant="primary" onClick={() => {
        const r = linked.find(x => x.id === which)
        if (r) { setFrom(r.provider); setStep('pick') } else { setFrom(undefined); setStep('connect') }
      }}>{which === 'other' ? 'Next' : 'Find backups'}</Button></div>
    </div>
  )
  if (step === 'connect') return (
    <div className="flex flex-col gap-3">
      <p className="text-[12.5px] text-dim">Connect to where your off-site copy is. Use the same account as before.</p>
      <ProviderPicker value={prov} onChange={id => { setProv(id); setConnected(false) }} />
      <SignInFields p={p} connected={connected} onConnected={() => setConnected(true)} />
      <div className="flex justify-end"><Button variant="primary" disabled={p.kind === 'oauth' && !connected} onClick={() => setStep('pick')}>Find backups</Button></div>
    </div>
  )
  return (
    <div className="flex flex-col gap-3">
      <p className="text-[12.5px] text-dim flex flex-wrap items-center gap-x-1.5">Found 3 server backups in <code className="font-mono">SaveLocker/_server</code> on {from ?? p.name}{from && ', the remote this server copies to'}.
        {from && <button type="button" className="underline underline-offset-2 text-fg cursor-pointer" onClick={() => { if (linked.length > 1) setStep('remote'); else { setFrom(undefined); setStep('connect') } }}>{linked.length > 1 ? 'Pick another remote' : 'Look somewhere else'}</button>}</p>
      <div className="flex flex-col gap-2" role="radiogroup">
        {BACKUPS.map((b, i) => <Choice key={b[0]} checked={pick === i} onPick={() => setPick(i)} title={`${b[1]} · ${b[2]}`} sub={<span className="font-mono">{b[0]}</span>} />)}
      </div>
      <label className="flex items-center gap-2 text-[12.5px] text-dim"><input type="checkbox" className="accent-[var(--color-accent)]" checked={enc} onChange={e => setEnc(e.target.checked)} />The copy is encrypted (enter the recovery key)</label>
      {enc && <input className={`${fieldCls} font-mono`} placeholder="tide-ember-…" aria-label="Recovery key" />}
      <div className="flex justify-end gap-2">{!from && <Button variant="quiet" onClick={() => setStep('connect')}>Back</Button>}<Button variant="primary" onClick={restore}>Restore this backup</Button></div>
    </div>
  )
}

/** A · the first screen of an empty server. */
export function FirstRun() {
  const [restore, setRestore] = useState(false)
  if (!restore) return (
    <div className="signin-wash flex-1 min-h-0 overflow-y-auto grid place-items-center p-6 animate-rise">
      <div className="w-full max-w-[640px] flex flex-col gap-4">
        <Mark size={46} />
        <h1 className="text-[29px] font-bold tracking-[-0.035em] text-fg">Welcome. This server is empty.</h1>
        <p className="text-[13.5px] text-dim">Start a new setup, or bring back a server you lost from its off-site copy.</p>
        <div className="grid sm:grid-cols-2 gap-3">
          <button type="button" onClick={() => toast('Start fresh: set an admin password and add your first device.')} className="text-left rounded-xl border border-line bg-panel p-4 cursor-pointer hover:bg-hover">
            <b className="text-fg">Start fresh</b><span className="block text-[12.5px] text-dim mt-1">Set an admin password and add your first device.</span></button>
          <button type="button" onClick={() => setRestore(true)} className="text-left rounded-xl border border-accent-line bg-accent-soft p-4 cursor-pointer">
            <b className="text-fg">Restore from an off-site copy</b><span className="block text-[12.5px] text-dim mt-1">Google Drive, S3, a NAS… wherever SaveLocker was copying to.</span></button>
        </div>
      </div>
    </div>
  )
  return (
    <div className="flex-1 min-h-0 overflow-y-auto p-6 flex justify-center">
      <div className="w-full max-w-[720px]"><Card title="Restore from an off-site copy" headerRight={<Button size="sm" variant="quiet" onClick={() => { endFlow(); setRestore(false) }}>Back</Button>}>
        <RecoverFlow />
      </Card></div>
    </div>
  )
}

/** B · “Restore from off-site…” on Backups. */
export function RestoreFromOffsite() {
  const [open, setOpen] = useState(false)
  const linked = useRemotes()
  return (
    <>
      <Button onClick={() => setOpen(true)}>Restore from off-site…</Button>
      {open && <Dialog wide title="Restore from the off-site copy" sub={<span className="text-accent-ink">This replaces everything on this server with the backup.</span>} onClose={() => { endFlow(); setOpen(false) }}>
        <RecoverFlow onDone={() => setOpen(false)} linked={linked} />
      </Dialog>}
    </>
  )
}
