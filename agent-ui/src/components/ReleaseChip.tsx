import type { ReactNode } from 'react'
import type { TrackedGame } from '../types'
import { regionLabel } from '../format'
import { usePopover } from '../usePopover'

const languageNames = (() => {
  try { return new Intl.DisplayNames(['en'], { type: 'language' }) } catch { return null }
})()

/** Redump's codes ("En", "Fr") as names, the code itself when the browser has none. */
function languages(codes: string[]) {
  return codes.map(c => { try { return languageNames?.of(c.toLowerCase()) ?? c } catch { return c } }).join(', ')
}

/**
 * Counterpart of web/src/components/game/ReleaseChip.tsx: which release of a console game this is — its region
 * as a chip beside the source ("USA"), or "Details" for a save Redump does not know. Hover shows the details, a
 * click or tap keeps them open; Esc or a click elsewhere closes them.
 */
export function ReleaseChip({ game }: { game: TrackedGame }) {
  const release = game.release
  const pop = usePopover()

  if (!release) return null
  const region = regionLabel(release)
  const rows: [string, ReactNode, boolean?][] = [
    ['Region', release.region ?? 'Unknown'],
    ['Game ID', release.gameIds?.length ? release.gameIds.join(' · ') : 'None in the save', !!release.gameIds?.length],
    ['Console', release.console],
  ]
  if (game.source?.kind === 'emulator') rows.push(['Emulator', game.source.detail])
  if (release.languages?.length) rows.push(['Languages', languages(release.languages)])
  if (game.saveFiles) rows.push(['Save', game.saveFiles, true])

  return (
    <span className="sl-release" ref={pop.rootRef} onMouseEnter={pop.enter} onMouseLeave={pop.leave}>
      <button
        ref={pop.buttonRef}
        type="button"
        className="sl-release__chip"
        onClick={pop.toggle}
        aria-haspopup="dialog"
        aria-expanded={pop.expanded}
        aria-label={region ? `Region ${release.region}: show the game's details` : "Show the game's details"}
      >
        {region ?? 'Details'}
        <span className="sl-release__dot" aria-hidden="true" />
      </button>
      {pop.open && (
        <span className="sl-release__pop" role="dialog" aria-label="Game details">
          <span className="sl-release__title">{game.name}</span>
          <dl>
            {rows.map(([k, v, mono]) => (
              <div key={k}>
                <dt>{k}</dt>
                <dd className={mono ? 'sl-release__mono' : undefined}>{v}</dd>
              </div>
            ))}
          </dl>
          <span className="sl-release__note">
            {release.region
              ? 'Another region’s release has another game ID and cannot read these saves, so it is kept as its own game.'
              : release.namedBy
                ? `Title from ${release.namedBy}, which lists no region for it.`
                : 'This save carries no game ID Redump knows, so its name was read from inside the save.'}
          </span>
          {release.region && <span className="sl-release__from">Title from {release.namedBy ?? 'the save'}, found by the game ID in the save.</span>}
        </span>
      )}
    </span>
  )
}
