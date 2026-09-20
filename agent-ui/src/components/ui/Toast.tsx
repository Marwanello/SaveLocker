import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'

interface Props {
  children: ReactNode
  onDismiss: () => void
  /** `warn` for something that failed — it also dwells longer, because it is the one a person has
   *  to read and act on. */
  tone?: 'default' | 'warn'
  /** plan.md motion table: "2.6s dwell". */
  dwellMs?: number
}

/** A single toast, fixed bottom-right. Confirms in the past tense (plan.md "Voice"): "Sync all
 *  complete." rather than "Sync completed successfully!". */
export function Toast({ children, onDismiss, tone = 'default', dwellMs = tone === 'warn' ? 6000 : 2600 }: Props) {
  // Read through a ref, not listed as a dependency: a parent that re-renders on every poll and
  // passes an inline arrow would otherwise restart the dwell timer each time, and the toast would
  // outlive its dwell for as long as anything above it kept re-rendering.
  const dismissRef = useRef(onDismiss)
  useEffect(() => { dismissRef.current = onDismiss })
  useEffect(() => {
    const id = setTimeout(() => dismissRef.current(), dwellMs)
    return () => clearTimeout(id)
  }, [dwellMs])

  return (
    <div role="status" className={`sl-toast ${tone === 'warn' ? 'sl-toast--warn' : ''}`.trim()}>
      {children}
    </div>
  )
}
