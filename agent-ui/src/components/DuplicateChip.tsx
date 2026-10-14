import { useEffect, useRef, useState } from 'react'
import type { Candidate } from '../types'
import { Chip } from './ui/Chip'

type Shortcut = NonNullable<Candidate['duplicateShortcuts']>[number]

const canHover = () => typeof window !== 'undefined' && !!window.matchMedia?.('(hover: hover)').matches

/**
 * Steam has this game under several AppIDs — a launcher re-created the shortcut instead of updating
 * the original, and Steam only ever launches one. The chip says how many; hovering (or a click,
 * which pins it) names each shortcut and which one the scan will track. Esc or a click elsewhere
 * closes it. Same pattern as the emulator release chip.
 */
export function DuplicateChip({ shortcuts }: { shortcuts: Shortcut[] }) {
  const [open, setOpen] = useState<'hover' | 'pinned' | null>(null)
  const rootRef = useRef<HTMLSpanElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (open !== 'pinned') return
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(null) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(null); buttonRef.current?.focus() } }
    document.addEventListener('mousedown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey) }
  }, [open])

  const tracked = shortcuts.find(s => s.tracked)
  return (
    <span
      className="sl-release"
      ref={rootRef}
      onMouseEnter={() => { if (canHover()) setOpen(o => o ?? 'hover') }}
      onMouseLeave={() => setOpen(o => (o === 'hover' ? null : o))}
    >
      <button
        ref={buttonRef}
        type="button"
        className="sl-release__chip sl-release__chip--warn"
        aria-haspopup="dialog"
        aria-expanded={open !== null}
        aria-label={`${shortcuts.length} shortcuts with different AppIDs — which one will be tracked`}
        onClick={() => setOpen(o => (o === 'pinned' ? null : 'pinned'))}
      >
        {shortcuts.length} shortcuts · different AppIDs
        <span className="sl-release__dot sl-release__dot--warn" aria-hidden="true" />
      </button>
      {open && (
        <span className="sl-release__pop" role="dialog" aria-label="Shortcuts with different AppIDs">
          <span className="sl-release__title">{shortcuts.length} shortcuts with different AppIDs</span>
          {shortcuts.map(s => (
            <span key={s.appId ?? 'none'} className={s.tracked ? 'sl-dup sl-dup--tracked' : 'sl-dup'}>
              <span className="sl-dup__head">
                <span className="sl-dup__name">{s.name}</span>
                <span className="sl-release__mono">AppID {s.appId ?? 'none'}</span>
                {s.tracked ? <Chip tone="ok">Will be tracked</Chip> : <Chip>Duplicate</Chip>}
              </span>
            </span>
          ))}
          <span className="sl-release__note">
            Steam launches only one of them. SaveLocker follows the one its scan resolved
            {tracked ? ` (AppID ${tracked.appId ?? 'none'})` : ''}; map another with <code>--dir</code> to switch.
          </span>
        </span>
      )}
    </span>
  )
}

/** The row-level note beside the chip: the consequence in one line, naming the tracked AppID. */
export function DuplicateNote({ shortcuts }: { shortcuts: Shortcut[] }) {
  const tracked = shortcuts.find(s => s.tracked)
  return (
    <span className="sl-check-row__why">
      Steam launches only one of them. SaveLocker will track AppID {tracked?.appId ?? 'none'}
      {' '}— if that is the stale shortcut, its saves are never backed up. Hover the chip for both;
      map the other with <code>--dir</code> to switch.
    </span>
  )
}
