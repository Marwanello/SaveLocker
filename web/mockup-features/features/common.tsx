import type { ReactNode } from 'react'
import { Icon } from '../../src/components/ui/Icon'
import { useMock } from '../store'

/** The console's dialog: AddGameDialog's overlay and panel. */
export function Dialog({ title, sub, children, actions, onClose, wide = false }: {
  title: ReactNode; sub?: ReactNode; children: ReactNode; actions?: ReactNode; onClose?: () => void; wide?: boolean
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/45 backdrop-blur-sm"
      onKeyDown={e => { if (e.key === 'Escape' && onClose) onClose() }}>
      <div role="dialog" aria-modal="true"
        className={`w-full ${wide ? 'max-w-[640px]' : 'max-w-[480px]'} max-h-[calc(100vh-32px)] overflow-y-auto bg-panel border border-line rounded-xl p-5 flex flex-col gap-4 animate-rise`}>
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="text-lg font-bold text-fg text-balance">{title}</h2>
            {sub && <p className="text-[13px] text-dim mt-1">{sub}</p>}
          </div>
          {onClose && (
            <button type="button" onClick={onClose} aria-label="Close"
              className="p-1 rounded-md text-dim hover:text-fg hover:bg-hover cursor-pointer"><Icon name="x" size={16} /></button>
          )}
        </div>
        <div className="flex flex-col gap-3 text-[13px] text-dim">{children}</div>
        {actions && <div className="flex justify-end gap-2 flex-wrap pt-1">{actions}</div>}
      </div>
    </div>
  )
}

/** The running flow's steps, ticked off as they finish. */
export function Steps() {
  const flow = useMock(s => s.flow)
  if (!flow) return null
  return (
    <ol className="flex flex-col gap-2.5">
      {flow.steps.map((s, i) => {
        const st = flow.state === 'ok' || i < flow.i ? 'done' : i === flow.i ? (flow.state === 'fail' ? 'fail' : 'run') : 'todo'
        const detail = st === 'fail' && flow.failMsg ? flow.failMsg : s.d
        return (
          <li key={i} className={`flex gap-2.5 items-start text-[13px] ${st === 'todo' ? 'text-faint' : st === 'fail' ? 'text-accent-ink' : 'text-fg'}`}>
            <span className={`mt-px w-[18px] h-[18px] rounded-full border-[1.5px] grid place-items-center flex-none
              ${st === 'done' ? 'bg-safe-soft border-safe-line text-safe-ink' : st === 'fail' ? 'bg-accent-soft border-accent-line text-accent-ink'
                : st === 'run' ? 'border-info border-t-transparent animate-spin' : 'border-line'}`}>
              {st === 'done' && <Icon name="check" size={11} />}
              {st === 'fail' && <Icon name="x" size={11} />}
            </span>
            <span className="min-w-0">{s.t}{detail && <span className="block text-xs text-dim">{detail}</span>}</span>
          </li>
        )
      })}
    </ol>
  )
}

/** A radio card, the console's pick-one-of-several pattern (Seg is for short labels only). */
export function Choice({ checked, onPick, title, sub, children }: { checked: boolean; onPick: () => void; title: ReactNode; sub?: ReactNode; children?: ReactNode }) {
  return (
    <button type="button" role="radio" aria-checked={checked} onClick={onPick}
      className={`w-full text-left flex gap-2.5 items-start rounded-xl border px-3.5 py-3 cursor-pointer transition-colors
        ${checked ? 'border-accent-line bg-accent-soft' : 'border-line bg-panel hover:bg-hover'}`}>
      <span className={`mt-0.5 w-[15px] h-[15px] rounded-full border-[1.5px] grid place-items-center flex-none ${checked ? 'border-accent' : 'border-faint'}`}>
        {checked && <span className="w-[7px] h-[7px] rounded-full bg-accent" />}
      </span>
      <span className="min-w-0 flex flex-col gap-0.5">
        <span className="text-[13px] font-semibold text-fg">{title}</span>
        {sub && <span className="text-xs text-dim">{sub}</span>}
        {children}
      </span>
    </button>
  )
}

export const fieldCls = 'bg-tile text-fg border border-line rounded-lg px-2 py-[5px] text-[12.5px] focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent'

export const fmtGb = (bytes: number) => `${(bytes / 1e9).toFixed(1)} GB`
