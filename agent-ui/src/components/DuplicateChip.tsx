import type { Candidate } from '../types'
import { usePopover } from '../usePopover'
import { Chip } from './ui/Chip'

type Shortcut = NonNullable<Candidate['duplicateShortcuts']>[number]

/**
 * Steam has this game under several AppIDs — a launcher re-created the shortcut instead of updating
 * the original, and Steam only ever launches one. The chip says how many AppIDs; hovering (or a
 * click, which pins it) names each shortcut and which one the scan will track. Esc or a click
 * elsewhere closes it. Same popover as the emulator release chip (usePopover).
 */
export function DuplicateChip({ shortcuts }: { shortcuts: Shortcut[] }) {
  const pop = usePopover()
  const tracked = shortcuts.find(s => s.tracked)
  const count = shortcuts.length
  return (
    <span className="sl-release" ref={pop.rootRef} onMouseEnter={pop.enter} onMouseLeave={pop.leave}>
      <button
        ref={pop.buttonRef}
        type="button"
        className="sl-release__chip sl-release__chip--warn"
        aria-haspopup="dialog"
        aria-expanded={pop.expanded}
        aria-label={`${count} duplicate AppIDs ${tracked ? '— which one will be tracked' : '— none of them is followed'}`}
        onClick={pop.toggle}
      >
        {count} duplicate AppIDs
        <span className="sl-release__dot sl-release__dot--warn" aria-hidden="true" />
      </button>
      {pop.open && (
        // preventDefault: an Add games row is a <label>, and a click on the popover (not interactive
        // content) would otherwise activate its checkbox — selecting the game while the user read it.
        <span
          className="sl-release__pop"
          role="dialog"
          aria-label="Duplicate AppIDs for this game"
          onClick={e => e.preventDefault()}
        >
          <span className="sl-release__title">{count} AppIDs for this game</span>
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
            {tracked
              ? <>Steam launches only one of them. SaveLocker follows the one its scan resolved
                  {' '}(AppID {tracked.appId ?? 'none'}); map another with <code>--dir</code> to switch.</>
              : <>Steam launches only one of them, but this scan resolved the game&apos;s save folder
                  another way, so none of these shortcuts is followed. Map one with <code>--dir</code> to switch.</>}
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
      {tracked ? (
        <>Steam launches only one of them. SaveLocker will track AppID {tracked.appId ?? 'none'}
          {' '}— if that is the stale shortcut, its saves are never backed up. Open the chip to see
          each; map another with <code>--dir</code> to switch.</>
      ) : (
        <>Steam launches only one of them, but SaveLocker found this game&apos;s saves another way,
          so none of these shortcuts is followed. Open the chip to see each; map one with
          {' '}<code>--dir</code> to switch.</>
      )}
    </span>
  )
}
