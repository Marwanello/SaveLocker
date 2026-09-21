import type { View } from '../types'

interface Props {
  activeView: View
  onNavigate: (v: View) => void
  conflictCount: number
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
  { view: 'settings', label: 'Settings' },
]

export function Sidebar({ activeView, onNavigate, conflictCount, agentLabel, machineName, serverHost }: Props) {
  return (
    <aside className="sl-aside">
      <nav aria-label="Sections" style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
        {NAV.map(({ view, label }) => {
          const badge = view === 'conflicts' ? conflictCount : 0
          return (
            <button
              key={view}
              type="button"
              className="sl-nav"
              aria-current={activeView === view ? 'page' : undefined}
              onClick={() => onNavigate(view)}
            >
              <span>{label}</span>
              {badge > 0 && <span className="sl-count" aria-label={`${badge} open`}>{badge}</span>}
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
