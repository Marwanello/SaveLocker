import type { MouseEvent } from 'react'
import { ChevronDown, Undo2 } from 'lucide-react'
import type { LinkOption } from '../types'

/** What the user picked for one row; absent means automatic. */
export interface LinkPick { choice: string; gameId?: string | null }

export const isPicked = (o: LinkOption, pick: LinkPick | undefined) =>
  pick ? o.choice === pick.choice && (o.gameId ?? null) === (pick.gameId ?? null) : o.choice === 'auto'

interface Props {
  title: string
  options: LinkOption[]
  pick: LinkPick | undefined
  open: boolean
  onToggle: () => void
  onPick: (pick: LinkPick | undefined) => void
}

/** The row lives inside the game's <label>: a click here must never tick or untick the game. */
const own = (fn: () => void) => (e: MouseEvent) => { e.preventDefault(); e.stopPropagation(); fn() }

function outcome(o: LinkOption) {
  if (o.kind === 'join') return { text: `Joins “${o.name}”`, tone: 'join' }
  if (o.kind === 'new') return { text: o.choice === 'separate' ? `Kept as its own game “${o.name}”` : `New game “${o.name}” on the server`, tone: 'new' }
  return { text: 'Can’t be added under any free name', tone: 'blocked' }
}

/**
 * Which server game an emulator save joins (tasks/emulator-saves Phase 16, version 2 of the mockup): one
 * line saying what will happen, and a Change button that opens the choices for this row only. Picking
 * anything but the automatic choice marks the row "chosen by you", with Back to automatic beside it.
 */
export function ServerGameLink({ title, options, pick, open, onToggle, onPick }: Props) {
  const current = options.find(o => isPicked(o, pick)) ?? options[0]
  const o = outcome(current)
  const mine = !!pick
  // Only the automatic choice: the line alone says it all.
  const changeable = options.length > 1
  return (
    <div className="sl-link">
      <div className="sl-link__line">
        <span className={`sl-link__dot sl-link__dot--${mine ? 'mine' : o.tone}`} aria-hidden="true" />
        <span>{o.text}</span>
        {mine && <span className="sl-link__mine">· chosen by you</span>}
        {changeable && (
          <span className="sl-link__acts">
            {mine && !open && (
              <button type="button" className="sl-link__btn sl-link__btn--undo" onClick={own(() => onPick(undefined))}>
                <Undo2 size={14} strokeWidth={1.9} aria-hidden="true" />
                Back to automatic
              </button>
            )}
            <button
              type="button"
              className="sl-link__btn"
              aria-expanded={open}
              aria-label={`${open ? 'Close' : 'Change'} the server game for ${title}`}
              onClick={own(onToggle)}
            >
              {open ? 'Close' : 'Change'}
              <ChevronDown size={14} strokeWidth={1.9} className="sl-link__chev" aria-hidden="true" />
            </button>
          </span>
        )}
      </div>
      {open && (
        <div className="sl-link__picker" role="radiogroup" aria-label={`Server game for ${title}`}>
          {options.map(x => {
            const on = isPicked(x, pick)
            const blocked = x.kind === 'blocked'
            const label = x.choice === 'auto'
              ? `Automatic: ${x.kind === 'join' ? `join “${x.name}”` : x.kind === 'new' ? `new game “${x.name}”` : x.name}`
              : x.choice === 'separate' ? 'Keep it as its own game' : x.name
            return (
              <button
                key={`${x.choice}:${x.gameId ?? ''}`}
                type="button"
                role="radio"
                aria-checked={on}
                aria-disabled={blocked || undefined}
                className="sl-link__opt"
                onClick={own(() => { if (!blocked) onPick(x.choice === 'auto' ? undefined : { choice: x.choice, gameId: x.gameId }) })}
              >
                <span className="sl-link__opt-head">
                  <span className="sl-link__radio" aria-hidden="true" />
                  <b>{label}</b>
                  <span className={`sl-link__badge sl-link__badge--${badgeTone(x)}`}>{x.badge}</span>
                </span>
                <span className="sl-link__detail">{x.detail}</span>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}

function badgeTone(o: LinkOption) {
  if (o.kind === 'blocked') return 'blocked'
  return o.kind === 'join' ? 'same' : 'new'
}
