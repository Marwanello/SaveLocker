import type { View } from '../types'

interface Props {
  activeView: View
  onNavigate: (v: View) => void
  /** Per-section counts. A count is only ever drawn when it is above zero. */
  counts: Partial<Record<View, number>>
  /** `state.buildLabel`, not the version number: several builds share one, and on a machine running
   *  a test build beside the installed one that is the whole question. Case is left alone — a commit
   *  hash in caps reads as a different string. */
  agentLabel: string
  machineName: string
  serverHost: string
}

const NAV: { view: View; label: string }[] = [
  { view: 'overview', label: 'Overview' },
  { view: 'games', label: 'Games' },
  { view: 'addGames', label: 'Add Games' },
  { view: 'conflicts', label: 'Conflicts' },
  { view: 'activity', label: 'Activity' },
  { view: 'settings', label: 'Settings' },
]

/** Only Conflicts is a decision waiting, so only it takes the accent (plan.md's colour rule); the other
 *  counts are information — how many games, how many suggestions, how many warnings — and stay neutral. */
const URGENT: readonly View[] = ['conflicts']

export function Sidebar({ activeView, onNavigate, counts, agentLabel, machineName, serverHost }: Props) {
  return (
    <aside className="sl-aside">
      <nav aria-label="Sections" style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
        {NAV.map(({ view, label }) => {
          const badge = counts[view] ?? 0
          return (
            <button
              key={view}
              type="button"
              className="sl-nav"
              aria-current={activeView === view ? 'page' : undefined}
              onClick={() => onNavigate(view)}
            >
              <span>{label}</span>
              {badge > 0 && (
                <span
                  className={`sl-count ${URGENT.includes(view) ? '' : 'sl-count--quiet'}`.trim()}
                  aria-label={`${badge} ${view === 'conflicts' ? 'open' : ''}`.trim()}
                >
                  {badge}
                </span>
              )}
            </button>
          )
        })}
      </nav>

      <div className="sl-aside__foot">
        <b>{machineName || '…'}</b>
        {serverHost && <span>{serverHost}</span>}
        <span style={{ display: 'block' }}>agent v{agentLabel}</span>
      </div>
    </aside>
  )
}
