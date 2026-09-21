import type { ReactNode } from 'react'

interface Props {
  title?: ReactNode
  headerRight?: ReactNode
  children: ReactNode
  /** Drop the 18px body padding — for a list that supplies its own row padding (a feed). */
  flush?: boolean
}

/** plan.md "Components": 14px radius, a header row when `title` is given, 18px body padding. */
export function Card({ title, headerRight, children, flush = false }: Props) {
  return (
    <section className="sl-card">
      {title && (
        <header>
          <h3>{title}</h3>
          {headerRight}
        </header>
      )}
      {flush ? children : <div className="sl-card__body">{children}</div>}
    </section>
  )
}
