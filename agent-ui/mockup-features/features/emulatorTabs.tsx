import type { TrackedGame } from '../../src/types'
import { emulatorIcon } from '../../src/platformIcon'
import { formatAgo } from '../../src/format'
import { PadGlyph } from '../../src/gamepad/PadGlyph'
import { PlatformIcon } from '../../src/components/ui/PlatformIcon'
import { openGame } from '../store'

/** The multi-emulator-games branch's EmulatorTabs (agent-ui/src/components/EmulatorTabs.tsx), as it shipped there:
 *  main doesn't have it yet, so the mockup carries the same markup. */
export function EmulatorTabs({ trees, current }: { trees: TrackedGame[]; current: string }) {
  return (
    <div className="sl-emutabs" role="tablist" aria-label="Emulators">
      {trees.map(t => {
        const name = t.source?.detail ?? 'Game'
        const icon = emulatorIcon(name)
        return (
          <button key={t.id} type="button" role="tab" className="sl-emutab" aria-selected={t.id === current}
            tabIndex={t.id === current ? 0 : -1} data-pad-label={`${name} tab`} onClick={() => openGame(t.id)}>
            {icon ? <PlatformIcon id={icon} size={1.35} /> : <span className="sl-emubadge">{name.slice(0, 2)}</span>}
            <span>
              <span className="sl-emutab__t1">{name}</span>
              <span className="sl-emutab__t2">{t.lastPushAt ? `Last push ${formatAgo(t.lastPushAt)}` : 'Set up here'}</span>
            </span>
            <span className="sl-dot sl-dot--ok" aria-label="Set up here" />
          </button>
        )
      })}
      <span className="sl-emutabs__pad"><PadGlyph id="r3" /></span>
    </div>
  )
}
