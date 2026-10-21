import { useRef } from 'react'
import type { KeyboardEvent } from 'react'
import type { TrackedGame } from '../types'
import { emulatorIcon } from '../platformIcon'
import { formatAgo } from '../format'
import { emulatorOf, treeLabel } from '../titles'
import { PadGlyph } from '../gamepad/PadGlyph'
import { PlatformIcon } from './ui/PlatformIcon'

/** A title's emulators as small icons, two letters where an emulator has none. */
export function EmulatorBadges({ trees }: { trees: TrackedGame[] }) {
  const names = [...new Set(trees.map(t => emulatorOf(t) ?? 'Game'))]
  return (
    <span className="sl-emubadges" title={names.join(' · ')}>
      {names.map(n => emulatorIcon(n)
        ? <PlatformIcon key={n} id={emulatorIcon(n)} size={1.05} />
        : <span key={n} className="sl-emubadge">{n.replace(/[^A-Za-z0-9]/g, '').slice(0, 2)}</span>)}
    </span>
  )
}

interface Props {
  trees: TrackedGame[]
  current: string
  conflicted: Set<string>
  onPick: (id: string) => void
}

/**
 * One tab per emulator tree of a title (tasks/multi-emulator-games Phase 6, the console's mockup A in the agent). Each tab
 * says whether that tree is set up here and carries its own dot, so a conflict in a closed tab still shows. Arrow keys
 * move between tabs; on a controller, R3 does (gamepad/nav.ts), since LB/RB already move between sections.
 */
export function EmulatorTabs({ trees, current, conflicted, onPick }: Props) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const index = Math.max(0, trees.findIndex(t => t.id === current))

  function onKey(e: KeyboardEvent<HTMLButtonElement>) {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return
    e.preventDefault()
    const next = (index + (e.key === 'ArrowRight' ? 1 : -1) + trees.length) % trees.length
    onPick(trees[next].id)
    refs.current[next]?.focus()
  }

  return (
    <div className="sl-emutabs" role="tablist" aria-label="Emulators">
      {trees.map((t, i) => {
        const here = !!t.path
        const tone = conflicted.has(t.id) ? 'crit' : here ? 'ok' : ''
        const name = emulatorOf(t)
        return (
          <button
            key={t.id}
            ref={el => { refs.current[i] = el }}
            type="button"
            role="tab"
            className="sl-emutab"
            aria-selected={i === index}
            tabIndex={i === index ? 0 : -1}
            data-pad-label={`${treeLabel(t, trees)} tab`}
            onClick={() => onPick(t.id)}
            onKeyDown={onKey}
          >
            {emulatorIcon(name) ? <PlatformIcon id={emulatorIcon(name)} size={1.35} /> : <EmulatorBadges trees={[t]} />}
            <span>
              <span className="sl-emutab__t1">{treeLabel(t, trees)}</span>
              <span className={`sl-emutab__t2${here ? '' : ' sl-emutab__t2--away'}`}>
                {!here ? 'Not set up here' : t.lastPushAt ? `Last push ${formatAgo(t.lastPushAt)}` : 'Set up here'}
              </span>
            </span>
            <span className={`sl-dot${tone ? ` sl-dot--${tone}` : ''}`} aria-label={conflicted.has(t.id) ? 'Conflict' : here ? 'Set up here' : 'Not set up here'} />
          </button>
        )
      })}
      <span className="sl-emutabs__pad"><PadGlyph id="r3" /></span>
    </div>
  )
}
