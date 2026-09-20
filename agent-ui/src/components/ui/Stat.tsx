import type { ReactNode } from 'react'

interface Props {
  label: string
  value: ReactNode
  context?: ReactNode
}

/** plan.md "Components": eyebrow label, large tabular figure, one line of context underneath. */
export function Stat({ label, value, context }: Props) {
  return (
    <dl className="sl-stat">
      <dt>{label}</dt>
      <dd>{value}</dd>
      {context && <dd>{context}</dd>}
    </dl>
  )
}
