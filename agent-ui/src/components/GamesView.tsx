import { PlatformIcon } from './ui/PlatformIcon'
import { consoleIcon } from '../platformIcon'
import { useMemo, useState } from 'react'
import { RefreshCw } from 'lucide-react'
import type { Conflict, TrackedGame, View } from '../types'
import { regionLabel } from '../format'
import { PadGlyph } from '../gamepad/PadGlyph'
import { GameArt } from './GameArt'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { Row } from './ui/Row'
import { Seg } from './ui/Seg'
import { EmulatorBadges } from './EmulatorTabs'
import { emulatorOf, groupTitles } from '../titles'
import type { GameTitle } from '../titles'

interface Props {
  games: TrackedGame[]
  /** False until the first `/api/games` request has settled — `games` is empty either way until then,
   *  so without this "Add games" would flash on open even when games are on their way in. */
  loaded: boolean
  conflicts: Conflict[]
  onOpen: (id: string) => void
  onNavigate: (v: View) => void
}

type Mode = 'list' | 'grid'
const MODE_KEY = 'savelocker.agent.games.mode'

function readMode(): Mode {
  try { return localStorage.getItem(MODE_KEY) === 'grid' ? 'grid' : 'list' } catch { return 'list' }
}

interface GroupProps {
  title: string
  note: string
  games: GameTitle[]
  mode: Mode
  conflicted: Set<string>
  onOpen: (id: string) => void
}

// Two releases of one game share its name; the region tells them apart. Its details are on the game's page.
const region = (g: TrackedGame) => (g.release ? regionLabel(g.release) : null)

/** A title opens on a tree set up here, if it has one. */
const entry = (t: GameTitle) => t.trees.find(g => g.path) ?? t.lead

/** The row's second line: a game's folder, or a title's emulators and which of them are not set up here. */
function subtext(t: GameTitle) {
  if (t.trees.length === 1) {
    const g = t.lead
    const playing = g.playthroughCount > 1 && g.playthroughName ? ` · Playing ${g.playthroughName}` : ''
    return (g.path || 'No save folder on this machine') + playing
  }
  const away = t.trees.filter(g => !g.path).map(g => emulatorOf(g) ?? g.name)
  return <><EmulatorBadges trees={t.trees} />{t.trees.map(g => emulatorOf(g) ?? g.name).join(' · ')}
    {away.length > 0 && <i> · {away.join(', ')} not set up here</i>}</>
}

function Group({ title, note, games, mode, conflicted, onOpen }: GroupProps) {
  return (
    <section className="sl-group" aria-label={title}>
      <div className="sl-group__head">
        <h3>{title} · {games.length}</h3>
        <span>{note}</span>
      </div>
      {mode === 'list' ? (
        <div className="sl-list">
          {games.map(t => {
            const g = t.lead
            const clash = t.trees.find(x => conflicted.has(x.id))
            return (
              <Row
                key={t.key}
                onClick={() => onOpen(entry(t).id)}
                cover={<GameArt id={g.id} name={t.name} kind="icon" w={96} console={g.release?.console} />}
                title={t.name}
                subtext={subtext(t)}
                end={region(g) || clash
                  ? <>
                      {region(g) && <span className="sl-tag">{region(g)}</span>}
                      {clash && <Chip tone="crit">{t.trees.length > 1 ? `Conflict · ${emulatorOf(clash) ?? clash.name}` : 'Conflict'}</Chip>}
                    </>
                  : undefined}
              />
            )
          })}
        </div>
      ) : (
        <div className="sl-wall">
          {games.map(t => {
            const g = t.lead
            return (
              <button key={t.key} type="button" className="sl-tile" onClick={() => onOpen(entry(t).id)} title={t.name}>
                <span className="sl-tile__cover">
                  <GameArt id={g.id} name={t.name} kind="grid" w={256} console={g.release?.console} />
                  {t.trees.length > 1
                    ? <span className="sl-tile__badge"><EmulatorBadges trees={t.trees} /></span>
                    : consoleIcon(g.release?.console) && (
                      <span className="sl-tile__badge" title={g.release!.console}><PlatformIcon id={consoleIcon(g.release?.console)} /></span>
                    )}
                  {t.trees.some(x => conflicted.has(x.id)) && <span className="sl-tile__pin"><Chip tone="crit">Conflict</Chip></span>}
                </span>
                <span className="sl-tile__name">{t.name}</span>
                {t.trees.length > 1
                  ? <span className="sl-tile__sub">{t.trees.length} emulators</span>
                  : g.playthroughCount > 1 && g.playthroughName
                    ? <span className="sl-tile__sub">{g.playthroughName}</span>
                    : region(g) && <span className="sl-tile__sub">{region(g)}</span>}
              </button>
            )
          })}
        </div>
      )}
    </section>
  )
}

/**
 * The games this machine tracks (plan.md Phase 5), in two groups: those with a save folder here, and the rest of the server's. Renders from `GET /api/games` only — that list is
 * config, so it costs nothing. `sync-status` hashes a save folder and is never asked for here: it is
 * for the one game a person has opened, on an explicit "Check now".
 */
export function GamesView({ games, loaded, conflicts, onOpen, onNavigate }: Props) {
  const [mode, setMode] = useState<Mode>(readMode)
  const [query, setQuery] = useState('')

  const conflicted = useMemo(() => new Set(conflicts.map(c => c.gameId)), [conflicts])
  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    return [...games]
      .sort((a, b) => a.name.localeCompare(b.name))
      .filter(g => !q || g.name.toLowerCase().includes(q) || g.path.toLowerCase().includes(q) ||
        (g.title?.name ?? '').toLowerCase().includes(q) || (emulatorOf(g) ?? '').toLowerCase().includes(q) ||
        (g.release?.region ?? '').toLowerCase().includes(q) || (g.release?.gameIds ?? []).some(id => id.toLowerCase().includes(q)))
  }, [games, query])
  // One entry per title (tasks/multi-emulator-games): a game saved with two emulators is listed once. Grouped after the
  // search, so a title shows when any of its trees matches.
  const titles = useMemo(() => groupTitles(visible).sort((a, b) => a.name.localeCompare(b.name)), [visible])
  // A game is this machine's own once it has a save folder here. Everything else was adopted from the
  // server and cannot sync until a folder is chosen, so it is listed apart rather than mixed in.
  const here = useMemo(() => titles.filter(t => t.trees.some(g => g.path)), [titles])
  const elsewhere = useMemo(() => titles.filter(t => !t.trees.some(g => g.path)), [titles])

  function pick(next: Mode) {
    setMode(next)
    try { localStorage.setItem(MODE_KEY, next) } catch { /* private window: the choice just lasts this visit */ }
  }

  if (!loaded) {
    return (
      <div className="sl-page">
        <Card>
          <div className="sl-empty">
            <RefreshCw size={18} strokeWidth={1.9} className="sl-spin" aria-hidden="true" />
            <p style={{ marginTop: 10 }}>Loading…</p>
          </div>
        </Card>
      </div>
    )
  }

  if (games.length === 0) {
    return (
      <div className="sl-page">
        <Card>
          <div className="sl-empty">
            <p>No games are on this machine or the server yet.</p>
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
        <PadGlyph id="x" />
        <input
          type="search"
          className="sl-search"
          placeholder="Search by name or folder"
          aria-label="Search tracked games"
          value={query}
          onChange={e => setQuery(e.target.value)}
        />
        <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>
          {query.trim() ? `${titles.length} of ${groupTitles(games).length}` : `${titles.length} games`}
        </span>
        <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 8 }}>
          <PadGlyph id="r3" />
          <Seg<Mode>
            aria-label="Layout"
            value={mode}
            onChange={pick}
            options={[{ value: 'list', label: 'List' }, { value: 'grid', label: 'Grid' }]}
          />
        </div>
      </div>

      {visible.length === 0 && (
        <Card><div className="sl-empty">No game matches “{query.trim()}”.</div></Card>
      )}

      {here.length > 0 && (
        <Group
          title="On this machine"
          note="Save folder set here — these sync."
          games={here}
          mode={mode}
          conflicted={conflicted}
          onOpen={onOpen}
        />
      )}
      {elsewhere.length > 0 && (
        <Group
          title="On the server"
          note="Not set up on this machine yet. Open one to choose its save folder."
          games={elsewhere}
          mode={mode}
          conflicted={conflicted}
          onOpen={onOpen}
        />
      )}
    </div>
  )
}
