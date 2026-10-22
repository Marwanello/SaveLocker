import type { ReactNode } from 'react'
import { Check, X } from 'lucide-react'
import { endFlow, getState, ptById, runFlow, setState, useMock } from '../store'
import { formatAgo, formatBytes } from '../../src/format'

/** The agent's dialog: the same overlay and `.sl-ask` box SwitchPlaysIn uses. */
export function Dialog({ title, children, actions, onClose }: { title: ReactNode; children: ReactNode; actions: ReactNode; onClose?: () => void }) {
  return (
    <div
      role="dialog" aria-modal="true" aria-label={typeof title === 'string' ? title : undefined}
      onKeyDown={e => { if (e.key === 'Escape' && onClose) { e.stopPropagation(); onClose() } }}
      style={{
        position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
      }}
    >
      <div className="sl-ask">
        <div className="sl-ask__title">{title}</div>
        <div className="sl-ask__body">{children}</div>
        <div className="sl-ask__actions">{actions}</div>
      </div>
    </div>
  )
}

/** The running flow's steps, ticked off as they finish. */
export function Steps() {
  const flow = useMock(s => s.flow)
  if (!flow) return null
  return (
    <ol className="slf-steps">
      {flow.steps.map((s, i) => {
        const cls = flow.state === 'ok' || i < flow.i ? 'done' : i === flow.i ? (flow.state === 'fail' ? 'fail' : 'run') : ''
        const detail = cls === 'fail' && flow.failMsg ? flow.failMsg : s.d
        return (
          <li key={i} className={cls}>
            <span className="slf-steps__mark">{cls === 'done' ? <Check size={11} strokeWidth={3} /> : cls === 'fail' ? <X size={11} strokeWidth={3} /> : null}</span>
            <span>{s.t}{detail && <small>{detail}</small>}</span>
          </li>
        )
      })}
    </ol>
  )
}

export const ptLine = (id: string) => { const p = ptById(id); return `${formatAgo(p.when)} on ${p.by}` }
export const ptSize = (id: string) => formatBytes(ptById(id).size)

/** Switch this device to another playthrough: the steps the agent runs, with the mock's failure cases. */
export function startSwitch(target: string, after?: () => void) {
  const s = getState()
  const cur = ptById(s.selected), tgt = ptById(target)
  const off = s.sim === 'offline'
  const steps = [
    { t: `Check that ${'Baldur’s Gate 3'} isn’t running`, d: 'Switching while the game has its save open could lose progress.' },
    { t: `Upload “${cur.name}” first`, d: off ? 'The server can’t be reached. A copy is kept on this device and uploads when it’s back.' : 'Changed 20 minutes ago · 50 MB' },
    { t: 'Keep a copy on this device', d: s.copies === 'keep' ? 'Kept for offline switching' : s.copies === 'off' ? 'Copies are off in Settings' : 'Until the upload is confirmed' },
    { t: `Put “${tgt.name}” in the save folder`, d: off ? 'From the copy on this device. It may be older than the server’s.' : `Latest: ${ptLine(target)}` },
    { t: `This device now plays “${tgt.name}”` },
  ]
  let failAt = -1, failMsg = ''
  if (s.sim === 'running') { failAt = 0; failMsg = 'Baldur’s Gate 3 is running. Close the game, then switch. Nothing was changed.' }
  else if (off && s.copies === 'off') { failAt = 1; failMsg = `The server can’t be reached to save “${cur.name}” first, and copies are off. Nothing was changed.` }
  else if (off && !tgt.local) { failAt = 3; failMsg = `There’s no copy of “${tgt.name}” on this device. Connect to the server to switch. You’re still on “${cur.name}”.` }
  runFlow(steps, {
    failAt, failMsg,
    done: ok => { if (ok) setState({ selected: target, pts: getState().pts.map(p => ({ ...p, devices: p.id === target ? [...new Set(['Steam Deck — Maro', ...p.devices])] : p.devices.filter(d => d !== 'Steam Deck — Maro') })) }) },
  })
  after?.()
}

export const closeFlow = endFlow
