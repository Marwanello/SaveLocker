import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'

const canHover = () => typeof window !== 'undefined' && !!window.matchMedia?.('(hover: hover)').matches

const GAP = 8

/** What the popover must fit inside: the scrolling page around it, which clips it, else the window. */
function visibleArea(el: HTMLElement) {
  for (let p = el.parentElement; p; p = p.parentElement) {
    if (/auto|scroll/.test(getComputedStyle(p).overflowY)) return p.getBoundingClientRect()
  }
  return new DOMRect(0, 0, window.innerWidth, window.innerHeight)
}

/**
 * The hover/pin popover behind ReleaseChip and DuplicateChip: hovering opens while the pointer is
 * inside, a click pins it, Esc or a click elsewhere closes a pinned one. The pointer leaving gets a
 * short grace period before it closes — the popover sits 8px below the chip, and without it the
 * pointer crossing that gap on the way in closed the popover before it could be read.
 *
 * It opens on whichever side of the chip has the room, because a row near the bottom of the page
 * otherwise cut off the end of it, and it is measured before the first paint so it never jumps.
 */
export function usePopover() {
  const [open, setOpen] = useState<'hover' | 'pinned' | null>(null)
  const rootRef = useRef<HTMLSpanElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const popRef = useRef<HTMLSpanElement>(null)
  const [popStyle, setPopStyle] = useState<CSSProperties>()
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

  const isOpen = open !== null
  useLayoutEffect(() => {
    const pop = popRef.current
    const chip = buttonRef.current?.getBoundingClientRect()
    if (!isOpen || !pop || !chip) { setPopStyle(undefined); return }
    const area = visibleArea(pop)
    const box = pop.getBoundingClientRect()
    const below = area.bottom - chip.bottom - GAP * 2
    const above = chip.top - area.top - GAP * 2
    const up = box.height > below && above > below
    // Narrower than the area, so shifting left to fit can never push it off the other side.
    const width = Math.min(box.width, area.width - GAP * 2)
    const shift = Math.max(0, box.left + width - area.right + GAP)
    setPopStyle({
      maxWidth: width,
      maxHeight: Math.max(above, below),
      ...(up ? { top: 'auto', bottom: `calc(100% + ${GAP}px)` } : {}),
      ...(shift > 0 ? { left: -shift } : {}),
    })
  }, [isOpen])

  return {
    open,
    rootRef,
    buttonRef,
    popRef,
    popStyle,
    expanded: isOpen,
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
