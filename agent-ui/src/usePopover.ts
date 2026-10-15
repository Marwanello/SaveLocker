import { useEffect, useRef, useState } from 'react'

const canHover = () => typeof window !== 'undefined' && !!window.matchMedia?.('(hover: hover)').matches

/**
 * The hover/pin popover behind ReleaseChip and DuplicateChip: hovering opens while the pointer is
 * inside, a click pins it, Esc or a click elsewhere closes a pinned one. The pointer leaving gets a
 * short grace period before it closes — the popover sits 8px below the chip, and without it the
 * pointer crossing that gap on the way in closed the popover before it could be read.
 */
export function usePopover() {
  const [open, setOpen] = useState<'hover' | 'pinned' | null>(null)
  const rootRef = useRef<HTMLSpanElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const closeTimer = useRef<number | undefined>(undefined)

  useEffect(() => {
    if (open !== 'pinned') return
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(null) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(null); buttonRef.current?.focus() } }
    document.addEventListener('mousedown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey) }
  }, [open])

  useEffect(() => () => window.clearTimeout(closeTimer.current), [])

  return {
    open,
    rootRef,
    buttonRef,
    expanded: open !== null,
    toggle: () => setOpen(o => (o === 'pinned' ? null : 'pinned')),
    enter: () => {
      if (!canHover()) return
      window.clearTimeout(closeTimer.current)
      setOpen(o => o ?? 'hover')
    },
    leave: () => {
      window.clearTimeout(closeTimer.current)
      closeTimer.current = window.setTimeout(() => setOpen(o => (o === 'hover' ? null : o)), 150)
    },
  }
}
