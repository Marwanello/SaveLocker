import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { Button } from './Button'

interface Props {
  children: ReactNode
  onDismiss: () => void
  /** `warn` for something that failed. It stays until dismissed: it is the one a person has to read
   *  and act on, and a fixed dwell can end before they have finished reading it. */
  tone?: 'default' | 'warn'
  /** plan.md motion table: "2.6s dwell". `null` means no timeout — the toast shows a Dismiss button. */
  dwellMs?: number | null
}

/** A single toast, fixed bottom-right. Confirms in the past tense (plan.md "Voice"): "Sync all
 *  complete." rather than "Sync completed successfully!". */
export function Toast({ children, onDismiss, tone = 'default', dwellMs = tone === 'warn' ? null : 2600 }: Props) {
  // Read through a ref, not listed as a dependency: a parent that re-renders on every poll and
  // passes an inline arrow would otherwise restart the dwell timer each time, and the toast would
  // outlive its dwell for as long as anything above it kept re-rendering.
  const dismissRef = useRef(onDismiss)
  useEffect(() => { dismissRef.current = onDismiss })
  useEffect(() => {
    if (dwellMs === null) return
    const id = setTimeout(() => dismissRef.current(), dwellMs)
    return () => clearTimeout(id)
  }, [dwellMs])

  return (
    <div role={tone === 'warn' ? 'alert' : 'status'} className={`sl-toast ${tone === 'warn' ? 'sl-toast--warn' : ''}`.trim()}>
      <span>{children}</span>
      {dwellMs === null && <Button size="sm" variant="quiet" onClick={onDismiss}>Dismiss</Button>}
    </div>
  )
}
