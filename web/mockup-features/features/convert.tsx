import { useEffect, useState } from 'react'
import { Banner } from '../../src/components/ui/Banner'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { DataTable } from '../../src/components/ui/DataTable'
import { Icon } from '../../src/components/ui/Icon'
import { toast } from '../../src/toast'
import { getState, runFlow, setState, timers, useMock, useVariant } from '../store'
import { Dialog, Steps } from './common'

const TOTAL = 412
const SAVED_GB = 9.8

function tick() {
  const s = getState()
  if (!s.convSeen || s.convPaused || s.sim === 'low' || s.conv >= TOTAL) return
  const n = Math.min(TOTAL, s.conv + 23)
  setState({ conv: n })
  if (n >= TOTAL) toast(`Conversion finished. ${SAVED_GB} GB saved once the originals leave trash.`)
  else timers.conv = window.setTimeout(tick, 500)
}

/** The one-time notice after the upgrade. */
function Notice({ variant }: { variant: string }) {
  return (
    <Dialog title={variant === 'B' ? 'Updated to SaveLocker 0.9: converting your saves' : 'SaveLocker is converting your saves to save space'}
      sub="This update stores each file once, even when many versions share it. It runs once, right after the upgrade."
      actions={<Button variant="primary" onClick={() => setState({ convSeen: true })}>{variant === 'B' ? 'OK' : 'Got it'}</Button>}>
      <p>Your 412 versions are converted in the background. You can keep using SaveLocker and syncing normally the whole time. Each version is checked before it switches over, and its original zip stays in trash for 14 days.</p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-[12.5px]">
        <dt className="text-faint">Expected saving</dt><dd className="text-fg">about {SAVED_GB} GB of 14.2 GB</dd>
        <dt className="text-faint">Time</dt><dd className="text-fg">about 20 minutes</dd>
        <dt className="text-faint">Progress</dt><dd className="text-fg">Storage, at the top</dd>
      </dl>
    </Dialog>
  )
}

/** The planned conversion of existing versions to stored-once files, in the variant on show. */
export function ConvertCard() {
  const variant = useVariant('st-convert')!
  const conv = useMock(s => s.conv)
  const paused = useMock(s => s.convPaused)
  const seen = useMock(s => s.convSeen)
  const low = useMock(s => s.sim) === 'low'
  const flow = useMock(s => s.flow)
  const [revert, setRevert] = useState(false)
  useEffect(() => { window.clearTimeout(timers.conv); if (seen && !paused && !low) timers.conv = window.setTimeout(tick, 400) }, [seen, paused, low])

  const pct = Math.round((conv / TOTAL) * 100)
  const saved = ((SAVED_GB * conv) / TOTAL).toFixed(1)
  const state = low ? 'Paused: low disk space' : paused ? 'Paused' : conv >= TOTAL ? 'Finished' : seen ? 'Converting' : 'Waiting to start'
  const verify = () => runFlow([{ t: 'Re-read every stored file', d: '18,204 files' }, { t: 'Check each file against its hash' }, { t: 'Check every version has all its files' }], { done: ok => ok && toast('Storage verified. No problems.') })
  const controls = (
    <span className="flex gap-1.5 flex-wrap">
      <Button size="sm" disabled={!seen || conv >= TOTAL} onClick={() => setState({ convPaused: !paused })}>{paused ? 'Resume' : 'Pause'}</Button>
      <Button size="sm" onClick={verify}>Verify storage</Button>
      <Button size="sm" variant="quiet" onClick={() => setRevert(true)}>Revert…</Button>
    </span>
  )
  const lowBanner = low && <Banner tone="watch" title="Conversion paused: 4.1 GB free">It needs 5 GB free to carry on safely, because the original zips stay in trash for 14 days. Empty trash or free space and it carries on by itself.</Banner>
  // B's notice comes with its banner, on every page (ConvertBanner).
  const popup = !seen && variant !== 'B' && <Notice variant={variant} />
  const revertDialog = revert && (
    <Dialog title="Revert the conversion?" onClose={() => setRevert(false)}
      actions={<><Button variant="quiet" onClick={() => setRevert(false)}>Cancel</Button><Button variant="alert" onClick={() => { setState({ conv: 0, convPaused: true }); setRevert(false); toast('Reverted. Versions use their original zips again.') }}>Revert</Button></>}>
      <p>Converted versions switch back to their original zips, which are still in trash for up to 14 days. Versions made since the update stay in the new format.</p>
    </Dialog>
  )

  let body
  if (variant === 'A') {
    body = (
      <Card title="Storage" headerRight={controls}>
        <div className="flex flex-col gap-2">
          <div className="flex justify-between text-[13px]"><b className="text-fg">{state}</b><span className="font-mono text-[11.5px] text-dim">{conv} of {TOTAL} versions · {pct}%</span></div>
          <div className="h-1.5 rounded-full bg-tile overflow-hidden"><i className="block h-full bg-info transition-[width] duration-300" style={{ width: `${pct}%` }} /></div>
          <span className="text-[12px] text-dim">Saved so far: {saved} GB · problems: none</span>
          {flow && <Steps />}
        </div>
      </Card>
    )
  } else if (variant === 'B') {
    const games: [string, number, number][] = [['Cyberpunk 2077', 120, 6.1], ['Elden Ring', 96, 2.4], ['Hollow Knight', 140, 0.6], ['Stardew Valley', 56, 0.7]]
    let left = conv
    const rows = games.map(([n, total, gb]) => { const done = Math.min(total, left); left -= done; return { n, total, gb, done } })
    body = (
      <Card title="Converting to save space" headerRight={controls}>
        <div className="flex flex-col gap-3">
          <div className="flex justify-between items-baseline gap-3 flex-wrap">
            <b className="text-[15px] text-fg">{state}</b>
            <span className="font-mono text-[12px] text-dim">{conv} of {TOTAL} versions · {pct}%</span>
          </div>
          <div className="h-3.5 rounded-full bg-tile overflow-hidden" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label="Conversion">
            <i className={`block h-full ${low || paused ? 'bg-watch' : 'bg-info'} transition-[width] duration-300`} style={{ width: `${pct}%` }} />
          </div>
          <span className="text-[12px] text-dim">Saved so far: {saved} GB of about {SAVED_GB} GB · problems: none · syncing isn’t affected</span>
          <details className="group border border-line rounded-xl overflow-hidden">
            <summary className="flex items-center gap-1.5 px-3.5 py-2.5 text-[12.5px] text-fg cursor-pointer list-none hover:bg-hover">
              <Icon name="chevron-right" size={13} className="text-faint transition-transform group-open:rotate-90" />Per game
              <span className="text-dim">· {rows.filter(r => r.done >= r.total).length} of {rows.length} done</span>
            </summary>
            <DataTable caption="Conversion by game" rows={rows} rowKey={r => r.n}
              columns={[
                { head: 'Game', kind: 'k', cell: r => r.n },
                { head: 'Progress', cell: r => <span className="block w-[160px] h-1.5 rounded-full bg-tile overflow-hidden"><i className="block h-full bg-info" style={{ width: `${(r.done / r.total) * 100}%` }} /></span> },
                { head: 'Versions', kind: 'n', cell: r => `${r.done} / ${r.total}` },
                { head: 'Saved', kind: 'n', cell: r => `${((r.gb * r.done) / r.total).toFixed(1)} GB` },
              ]} />
          </details>
          {flow && <Steps />}
        </div>
      </Card>
    )
  } else {
    const f = conv / TOTAL
    const zips = 14.2 * (1 - f), blobs = 4.4 * f, trash = 14.2 * f
    const w = (n: number) => `${(n / 40) * 100}%`
    body = (
      <Card title="Storage" headerRight={controls}>
        <div className="flex flex-col gap-3">
          <div className="h-4 rounded-full bg-tile overflow-hidden flex"><i className="block h-full bg-watch" style={{ width: w(zips) }} /><i className="block h-full bg-safe" style={{ width: w(blobs) }} /><i className="block h-full bg-watch-soft" style={{ width: w(trash) }} /></div>
          <div className="flex gap-3.5 flex-wrap text-[11.5px] text-dim"><span>Old zips {zips.toFixed(1)} GB</span><span>Stored once {blobs.toFixed(1)} GB</span><span>Originals in trash {trash.toFixed(1)} GB (gone in 14 days)</span><span>Disk 40 GB</span></div>
          <div className="grid grid-cols-[repeat(auto-fit,minmax(140px,1fr))] gap-3">
            {[['Converted', `${conv} / ${TOTAL}`], ['Saved once trash empties', `${saved} GB`], ['Problems', '0'], ['State', state]].map(([l, v]) => (
              <div key={l}><div className="text-[10px] tracking-[0.12em] uppercase text-faint">{l}</div><div className="font-semibold text-fg tabular-nums">{v}</div></div>
            ))}
          </div>
          {flow && <Steps />}
        </div>
      </Card>
    )
  }
  return <>{lowBanner}{body}{popup}{revertDialog}</>
}

/** B · on every page while the conversion runs, under the nav like the console’s own notices. */
export function ConvertBanner() {
  const conv = useMock(s => s.conv)
  const seen = useMock(s => s.convSeen)
  const paused = useMock(s => s.convPaused)
  const low = useMock(s => s.sim) === 'low'
  useEffect(() => { window.clearTimeout(timers.conv); if (seen && !paused && !low) timers.conv = window.setTimeout(tick, 400) }, [seen, paused, low])
  if (!seen) return <Notice variant="B" />
  if (conv >= TOTAL) return null
  const pct = Math.round((conv / TOTAL) * 100)
  return (
    <div className="px-6 pt-3">
      <Banner tone={low ? 'watch' : 'safe'} title={low ? 'Converting saves: paused for disk space' : `Converting saves to save space · ${pct}%`}
        action={location.hash === '#backups' ? undefined : <Button size="sm" onClick={() => { location.hash = 'backups' }}>Open Storage</Button>}>
        After the update to 0.9. Syncing isn’t affected.
      </Banner>
    </div>
  )
}
