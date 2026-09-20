import type { ReactNode } from 'react'

type BannerTone = 'ok' | 'warn' | 'crit'

interface Props {
  tone: BannerTone
  title: ReactNode
  /** One plain sentence saying what happens next (plan.md "Voice"). */
  detail?: ReactNode
  /** The action, if any — a primary `Button` for a decision, a quiet one to dismiss. */
  action?: ReactNode
}

/** The single status line of a page. `crit` = a decision is waiting, `warn` = needs a look but will
 *  not block anything, `ok` = nothing needs you. Mirrors the prototype's `.banner`. */
export function Banner({ tone, title, detail, action }: Props) {
  return (
    <div className={`sl-banner sl-banner--${tone}`} role={tone === 'ok' ? 'status' : 'alert'}>
      <span className={`sl-dot sl-dot--${tone}`} aria-hidden="true" />
      <div className="sl-banner__txt">
        <strong>{title}</strong>
        {detail && <span>{detail}</span>}
      </div>
      {action}
    </div>
  )
}
