import type { ReactNode } from 'react'

interface Props {
  cover: ReactNode
  /** A string title also becomes the row's tooltip: the column is narrow and the text truncates. */
  title: ReactNode
  subtext: ReactNode
  end?: ReactNode
  onClick?: () => void
}

/** Counterpart of web/src/components/ui/Row.tsx (plan.md "Layout rules"): two-line rows — the name on
 *  line one, everything else on line two, status pinned right and centred across both. */
export function Row({ cover, title, subtext, end, onClick }: Props) {
  const Tag = onClick ? 'button' : 'div'
  return (
    <Tag onClick={onClick} className={`sl-row ${onClick ? 'sl-row--click' : ''}`.trim()}>
      <span className="sl-row__cover">{cover}</span>
      <span className="sl-row__title" title={typeof title === 'string' ? title : undefined}>{title}</span>
      <span className="sl-row__sub">{subtext}</span>
      {end && <span className="sl-row__end">{end}</span>}
    </Tag>
  )
}
