import type { ReactNode } from 'react'

interface Props {
  title: string
  /** One line under the title: counts and state, e.g. "3 open · sync paused". */
  sub?: ReactNode
  actions?: ReactNode
}

/** The title row every full page starts with (plan.md Phase 9.1's `PageHead`, agent-ui's copy). */
export function PageHead({ title, sub, actions }: Props) {
  return (
    <div className="sl-pagehead">
      <div>
        <h2>{title}</h2>
        {sub && <p>{sub}</p>}
      </div>
      {actions && <div className="sl-pagehead__actions">{actions}</div>}
    </div>
  )
}
