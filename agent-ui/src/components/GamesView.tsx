import { useMemo, useState } from 'react'
import type { Conflict, TrackedGame, View } from '../types'
import { GameArt } from './GameArt'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Row } from './ui/Row'
import { Seg } from './ui/Seg'

interface Props {
  games: TrackedGame[]
  conflicts: Conflict[]
  onOpen: (id: string) => void
  onNavigate: (v: View) => void
}

type Mode = 'list' | 'grid'
const MODE_KEY = 'savelocker.agent.games.mode'

function readMode(): Mode {
  try { return localStorage.getItem(MODE_KEY) === 'grid' ? 'grid' : 'list' } catch { return 'list' }
}

/**
 * The games this machine tracks (plan.md Phase 5). Renders from `GET /api/games` only — that list is
 * config, so it costs nothing. `sync-status` hashes a save folder and is never asked for here: it is
 * for the one game a person has opened, on an explicit "Check now".
 */
export function GamesView({ games, conflicts, onOpen, onNavigate }: Props) {
  const [mode, setMode] = useState<Mode>(readMode)
  const [query, setQuery] = useState('')

  const conflicted = useMemo(() => new Set(conflicts.map(c => c.gameId)), [conflicts])
  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return [...games]
      .sort((a, b) => a.name.localeCompare(b.name))
      .filter(g => !q || g.name.toLowerCase().includes(q) || g.path.toLowerCase().includes(q))
  }, [games, query])

  function pick(next: Mode) {
    setMode(next)
    try { localStorage.setItem(MODE_KEY, next) } catch { /* private window: the choice just lasts this visit */ }
  }

  if (games.length === 0) {
    return (
      <div className="sl-page">
        <Card>
          <div className="sl-empty">
            <p>No games are tracked on this machine yet.</p>
            <p style={{ marginTop: 12 }}>
              <Button variant="primary" onClick={() => onNavigate('addGames')}>Add games</Button>
            </p>
          </div>
        </Card>
      </div>
    )
  }

  return (
    <div className="sl-page">
      <div className="sl-tools">
        <input
          type="search"
          className="sl-search"
          placeholder="Search by name or folder"
          aria-label="Search tracked games"
          value={query}
          onChange={e => setQuery(e.target.value)}
        />
        <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>
          {visible.length === games.length ? `${games.length} games` : `${visible.length} of ${games.length}`}
        </span>
        <div style={{ marginLeft: 'auto' }}>
          <Seg<Mode>
            aria-label="Layout"
            value={mode}
            onChange={pick}
            options={[{ value: 'list', label: 'List' }, { value: 'grid', label: 'Grid' }]}
          />
        </div>
      </div>

      {visible.length === 0 && (
        <Card><div className="sl-empty">No tracked game matches “{query.trim()}”.</div></Card>
      )}

      {mode === 'list' && visible.length > 0 && (
        <div className="sl-list">
          {visible.map(g => (
            <Row
              key={g.id}
              onClick={() => onOpen(g.id)}
              cover={<GameArt id={g.id} name={g.name} kind="icon" w={96} />}
              title={g.name}
              subtext={g.path || 'No save folder set'}
              end={conflicted.has(g.id) ? <Chip tone="crit">Conflict</Chip> : undefined}
            />
          ))}
        </div>
      )}

      {mode === 'grid' && visible.length > 0 && (
        <div className="sl-wall">
          {visible.map(g => (
            <button key={g.id} type="button" className="sl-tile" onClick={() => onOpen(g.id)} title={g.name}>
              <span className="sl-tile__cover">
                <GameArt id={g.id} name={g.name} kind="grid" w={256} />
                {conflicted.has(g.id) && <span className="sl-tile__pin"><Chip tone="crit">Conflict</Chip></span>}
              </span>
              <span className="sl-tile__name">{g.name}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
