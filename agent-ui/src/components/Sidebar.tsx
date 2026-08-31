import { GitBranch, Monitor, Plus, Settings } from 'lucide-react'
import type { View } from '../types'

interface Props {
  activeView: View
  onNavigate: (v: View) => void
  conflictCount: number
}

const NAV: { view: View; label: string; Icon: React.ComponentType<{ size: number; strokeWidth: number; color: string }> }[] = [
  { view: 'overview', label: 'Overview', Icon: Monitor },
  { view: 'addGames', label: 'Add Games', Icon: Plus },
  { view: 'conflicts', label: 'Conflicts', Icon: GitBranch },
  { view: 'settings', label: 'Settings', Icon: Settings },
]

export function Sidebar({ activeView, onNavigate, conflictCount }: Props) {
  return (
    <div style={{
      width: 212, minWidth: 212, background: '#1E252A',
      borderRight: '1px solid #494949',
      display: 'flex', flexDirection: 'column', overflow: 'hidden',
    }}>
      {/* Navigation */}
      <nav style={{ padding: '10px 8px', flex: 1, display: 'flex', flexDirection: 'column', gap: 2 }}>
        {NAV.map(({ view, label, Icon }) => {
          const active = activeView === view
          const badge = view === 'conflicts' ? conflictCount : 0
          return (
            <div
              key={view}
              onClick={() => onNavigate(view)}
              style={{
                display: 'flex', alignItems: 'center', gap: 8,
                padding: '8px 10px', borderRadius: 5, cursor: 'pointer',
                background: active ? 'rgba(18,146,113,0.14)' : 'transparent',
                color: active ? '#129271' : '#ECEFF1',
                fontSize: 13, fontWeight: active ? 600 : 400,
                borderLeft: `2px solid ${active ? '#129271' : 'transparent'}`,
                transition: 'background 0.12s ease', userSelect: 'none',
              }}
            >
              <Icon size={14} strokeWidth={1.75} color={active ? '#129271' : '#9CA3AF'} />
              <span style={{ flex: 1 }}>{label}</span>
              {badge > 0 && (
                <span style={{
                  background: '#e5534b', color: '#fff', fontSize: 10, fontWeight: 700,
                  borderRadius: 8, padding: '1px 6px', lineHeight: 1.4,
                }}>{badge}</span>
              )}
            </div>
          )
        })}
      </nav>

    </div>
  )
}
