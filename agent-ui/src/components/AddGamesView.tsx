import { useState, useEffect, useCallback, useMemo } from 'react'
import { RefreshCw, FolderSearch } from 'lucide-react'
import type { Candidate, EnrollProgress } from '../types'
import { api } from '../api'
import { useFolderPicker } from '../useFolderPicker'
import { PathBrowserModal } from './PathBrowserModal'
import { LaunchSetupCard } from './LaunchSetupCard'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { PageHead } from './ui/PageHead'

interface Props {
  onEnrolled: () => void
}

/**
 * The list filters, in the order they appear.
 *
 * `suggested` is the default and is NOT the same as `all`: SaveLocker exists for games Steam does
 * not back up, so a library of hundreds of Cloud-backed titles would otherwise bury the handful the
 * user came to enroll. It is a starting view, not a restriction — `all` is one click away, which is
 * the part the Linux agent was missing entirely: it filtered Cloud games out with no control to
 * bring them back, so an installed Steam game could not be reached at all.
 */
type FilterId = 'suggested' | 'all' | 'steam' | 'shortcut' | 'heroic' | 'playnite'

const FILTERS: { id: FilterId; label: string; hint: string; match: (c: Candidate) => boolean }[] = [
  { id: 'suggested', label: 'Suggested', hint: 'Everything except games Steam Cloud already backs up', match: c => !c.hasSteamCloud },
  { id: 'all', label: 'All', hint: 'Every game found, including Steam Cloud titles', match: () => true },
  { id: 'steam', label: 'Steam', hint: 'Games installed from the Steam store', match: c => c.source === 'SteamInstalled' },
  { id: 'shortcut', label: 'Added to Steam', hint: 'Non-Steam games you added to your Steam library', match: c => c.source === 'SteamShortcut' },
  { id: 'heroic', label: 'Heroic', hint: 'Games installed through Heroic Games Launcher', match: c => c.source === 'Heroic' },
  { id: 'playnite', label: 'Playnite', hint: 'Games in your Playnite library', match: c => c.source === 'Playnite' },
]

/**
 * Path-detection state as its own axis, not a source filter — it used to be a `nopath` entry in
 * FILTERS ("Needs path"), which meant it competed with Steam/Heroic/etc. instead of combining with
 * them: there was no way to ask for "Heroic games still missing a path". Always visible, same idea
 * as the Store row under Heroic, and stacks with the source filter and store above.
 */
type PathMode = 'all' | 'has' | 'missing'

const PATH_MODES: { id: PathMode; label: string; match: (c: Candidate) => boolean }[] = [
  { id: 'all', label: 'All', match: () => true },
  { id: 'has', label: 'Detected', match: c => !!c.path },
  { id: 'missing', label: 'Not detected', match: c => !c.path },
]

/** Storefronts, as a second axis under the Heroic and Playnite filters. `Unknown` covers a runner
 * or library plugin we don't map. `Steam` only ever has entries under Playnite — Heroic manages no
 * Steam games — but counting it costs nothing and lets a Playnite user narrow to just their
 * Steam-owned titles the same way a Heroic user already narrows to Epic/GOG/Amazon. */
const STORES: { id: string; label: string }[] = [
  { id: 'Steam', label: 'Steam' },
  { id: 'Epic', label: 'Epic' },
  { id: 'Gog', label: 'GOG' },
  { id: 'Amazon', label: 'Amazon' },
  { id: 'Sideload', label: 'Sideloaded' },
  { id: 'Unknown', label: 'Other' },
]

function EnrollProgressBar({ progress, picked }: { progress: EnrollProgress | null; picked: number }) {
  const total = progress?.total || picked
  // Counting starts with the first game: before it (and while the list is re-read afterwards) there is
  // no "game 0 of N" to show, only the sweep.
  const started = progress !== null && progress.total > 0 && progress.index > 0
  // Fills as games finish, and a game part-way through counts as half — each is several round trips,
  // so a bar that only moved between games would sit still for most of the wait.
  const pct = started ? Math.min(100, Math.round(((progress.index - 0.5) / total) * 100)) : 0
  const what = progress?.game
    ? `${progress.step} — ${progress.game}`
    : progress?.step || 'Getting ready…'
  return (
    <div className="sl-enrollbar" role="status" aria-live="polite">
      <div className="sl-enrollbar__head">
        <span>{started ? `Adding game ${progress.index} of ${total}` : 'Adding games…'}</span>
        {started && <span>{pct}%</span>}
      </div>
      <div
        className="sl-meter"
        role="progressbar"
        aria-label="Adding games"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={started ? pct : undefined}
      >
        <i className={started ? undefined : 'sl-meter__sweep'} style={started ? { width: `${pct}%` } : undefined} />
      </div>
      <div className="sl-enrollbar__step">{what}</div>
    </div>
  )
}

export function AddGamesView({ onEnrolled }: Props) {
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [checked, setChecked] = useState<Set<number>>(new Set())
  const [filter, setFilter] = useState<FilterId>('suggested')
  const [store, setStore] = useState<string | null>(null)
  const [pathMode, setPathMode] = useState<PathMode>('all')
  const [query, setQuery] = useState('')
  const [scanning, setScanning] = useState(false)
  const [enrolling, setEnrolling] = useState(false)
  const [status, setStatus] = useState('')
  const [enrolled, setEnrolled] = useState(false)
  const [progress, setProgress] = useState<EnrollProgress | null>(null)
  // The games are added and the list is being re-read: the agent's progress no longer describes what
  // the page is waiting on, so polling it stops.
  const [refreshing, setRefreshing] = useState(false)
  const picker = useFolderPicker()

  const scan = useCallback(async (force = false) => {
    setScanning(true)
    setStatus('Scanning…')
    try {
      const result = force ? await api.rescan() : await api.candidates()
      setCandidates(result)
      // Cleared, not set to a count. This used to report result.length — every candidate, including
      // the filtered ones — and because the foot bar prefers `status` over its computed message, it
      // won: it claimed "Found 29 candidate(s)" above a list of 16 and the hidden-count hint never
      // rendered. Leaving it empty lets the computed message describe what is on screen.
      setStatus('')
    } catch (e) {
      setStatus('Scan failed: ' + (e as Error).message)
    } finally {
      setScanning(false)
    }
  }, [])

  useEffect(() => { void scan(false) }, [scan])

  const toggle = (id: number) => {
    setChecked(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  // The native dialog (Windows tray) also writes the candidate cache server-side; on a headless
  // Deck it returns null and the browser opens inside the game's own Proton prefix. Either way the
  // chosen path is persisted with api.candidateFolder and reflected in local state.
  const pickFolderFor = (c: Candidate) => picker.pick({
    name: c.name,
    start: () => c.path || c.prefixPath || null,
    nativePick: () => api.candidateFolderPick(c.id),
    apply: async (path) => {
      await api.candidateFolder(c.id, path)
      setCandidates(prev => prev.map(x => x.id === c.id ? { ...x, path } : x))
    },
  })

  // Enrolling a game with no save folder is what produced the silent Deck failures — it lands a
  // tracked game the archiver cannot back up. Named here so the block is actionable, not just off.
  const missing = [...checked]
    .map(id => candidates.find(c => c.id === id))
    .filter((c): c is Candidate => !!c && !c.path)

  // Adding a game is a round trip to the server per game, so the request can stay open for a while.
  // Its own progress is asked for beside it, or the page would look frozen on "Adding…".
  useEffect(() => {
    if (!enrolling || refreshing) return
    let live = true
    // Only an ACTIVE answer is this batch's: until the agent has started it, the route still holds the
    // finished state of the previous one, which would flash "game 3 of 3" before "game 1 of 2".
    const tick = () => api.enrollProgress().then(p => { if (live && p.active) setProgress(p) }).catch(() => {})
    void tick()
    const id = setInterval(tick, 350)
    return () => { live = false; clearInterval(id) }
  }, [enrolling, refreshing])

  const enroll = async () => {
    if (checked.size === 0 || missing.length > 0) return
    setEnrolling(true)
    setProgress(null)
    setStatus('')
    try {
      const result = await api.enroll([...checked])
      setStatus(
        `Added ${result.enrolled} game${result.enrolled === 1 ? '' : 's'}.` +
        (result.skipped > 0 ? ` Skipped ${result.skipped} already tracked.` : '')
      )
      if (result.enrolled > 0) setEnrolled(true)
      setChecked(new Set())
      onEnrolled()
      setRefreshing(true)
      setProgress({ active: true, index: 0, total: 0, game: null, step: 'Refreshing the list of games…', enrolled: 0, skipped: 0 })
      await scan(false)
    } catch (e) {
      setStatus('Could not add them: ' + (e as Error).message)
    } finally {
      setEnrolling(false)
      setRefreshing(false)
      setProgress(null)
    }
  }

  const busy = scanning || enrolling

  // Only chips that would show something are offered — an empty "Heroic" chip on a machine with no
  // Heroic install is a dead end that reads like a bug. Suggested and All always render, so the
  // toolbar never collapses to nothing.
  const chips = useMemo(
    () => FILTERS
      .map(f => ({ ...f, count: candidates.filter(f.match).length }))
      .filter(f => f.count > 0 || f.id === 'suggested' || f.id === 'all'),
    [candidates])

  const stores = useMemo(() => {
    if (filter !== 'heroic' && filter !== 'playnite') return []
    const source = filter === 'heroic' ? 'Heroic' : 'Playnite'
    const inSource = candidates.filter(c => c.source === source)
    return STORES
      .map(s => ({ ...s, count: inSource.filter(c => c.store === s.id).length }))
      .filter(s => s.count > 0)
  }, [candidates, filter])

  const active = FILTERS.find(f => f.id === filter) ?? FILTERS[0]
  // Filtered by source (+ store) but not yet by path — the base the path row's own counts are
  // drawn against, so "Detected" and "Not detected" describe the set the user is already looking at.
  const sourceFiltered = candidates
    .filter(active.match)
    .filter(c => !((filter === 'heroic' || filter === 'playnite') && store) || c.store === store)
  const activePathMode = PATH_MODES.find(p => p.id === pathMode) ?? PATH_MODES[0]
  const needle = query.trim().toLowerCase()
  const visible = sourceFiltered
    .filter(activePathMode.match)
    .filter(c => !needle || c.name.toLowerCase().includes(needle) || c.path.toLowerCase().includes(needle))

  const enrollBlocked = missing.length > 0
  // Named, not just counted. A user looking for a game they can plainly see in Steam needs to be
  // told it was filtered and by which control — silently showing a shorter list reads as "the scan
  // didn't find it".
  const hiddenCount = candidates.length - visible.length
  // Which control to name depends on which one is actually hiding things — there are two axes now,
  // and telling someone to choose “All” while “All” is already selected is worse than saying
  // nothing. Path is named first: it is the row that can hide everything while the source row
  // looks wide open.
  const undoHint =
    needle ? ' Clear the search to see every one.'
      : pathMode !== 'all' ? ' Set Path to “All” to see every one.'
      : filter !== 'all' ? ' Choose “All” to see every one.'
      : ''

  // The line under the filters: what the current filter means, and — when a search narrows it — how far.
  const filterLine = needle
    ? `Matching “${query.trim()}” — ${visible.length} of ${sourceFiltered.length}`
    : `${active.hint}${hiddenCount > 0 ? '.' + undoHint : ''}`

  const selectedLine = checked.size === 0
    ? 'Tick the games you want to sync.'
    : `${checked.size} selected · they start syncing after the next time you quit each game`
  const footText = status || (enrollBlocked ? `Set a save folder for: ${missing.map(c => c.name).join(', ')}` : selectedLine)

  return (
    <div className="sl-page">
      <PageHead
        title="Add games"
        sub={`${candidates.length} found on this machine · ${candidates.filter(c => !c.hasSteamCloud).length} suggested`}
        actions={
          <Button disabled={busy} onClick={() => void scan(true)}>
            <RefreshCw size={13} strokeWidth={1.9} className={scanning ? 'sl-spin' : undefined} aria-hidden="true" />
            Rescan
          </Button>
        }
      />

      <div className="sl-tools">
        <input
          type="search"
          className="sl-search"
          placeholder="Search by name or folder"
          aria-label="Search found games"
          value={query}
          onChange={e => setQuery(e.target.value)}
        />
      </div>

      <div className="sl-stack" style={{ gap: 9 }}>
        <div className="sl-filters">
          {chips.map(f => (
            <button
              key={f.id}
              type="button"
              className="sl-fchip"
              aria-pressed={filter === f.id}
              title={f.hint}
              onClick={() => { setFilter(f.id); setStore(null) }}
            >
              {f.label} <b>{f.count}</b>
            </button>
          ))}
        </div>

        {/* Heroic storefronts — a second axis, shown only while the Heroic filter is on. */}
        {stores.length > 1 && (
          <div className="sl-filters">
            <span className="sl-filters__label">Store</span>
            <button type="button" className="sl-fchip" aria-pressed={store === null} onClick={() => setStore(null)}>All</button>
            {stores.map(s => (
              <button key={s.id} type="button" className="sl-fchip" aria-pressed={store === s.id} onClick={() => setStore(s.id)}>
                {s.label} <b>{s.count}</b>
              </button>
            ))}
          </div>
        )}

        {/* Save-path detection — a second axis like Store, so it stacks with the source filter
            instead of replacing it. Always visible: unlike Store it isn't specific to one source. */}
        <div className="sl-filters">
          <span className="sl-filters__label">Save folder</span>
          {PATH_MODES.map(p => (
            <button key={p.id} type="button" className="sl-fchip" aria-pressed={pathMode === p.id} onClick={() => setPathMode(p.id)}>
              {p.label}{p.id !== 'all' && <> <b>{sourceFiltered.filter(p.match).length}</b></>}
            </button>
          ))}
        </div>

        <div style={{ fontSize: 12, color: 'var(--color-dim)' }}>{filterLine}</div>
      </div>

      <Card flush>
        {visible.length === 0 ? (
          <div className="sl-empty">
            {scanning ? 'Scanning…' : candidates.length === 0 ? 'No games found yet. Rescan once a game has been installed.' : 'Nothing matches that filter.'}
          </div>
        ) : visible.map(c => (
          <label key={c.id} className="sl-check-row">
            <input type="checkbox" checked={checked.has(c.id)} onChange={() => toggle(c.id)} />
            <div className="sl-check-row__main">
              <div className="sl-check-row__name">
                <span>{c.name}</span>
                <Chip>{c.source}</Chip>
                {/* Only when it adds something the source does not already say. */}
                {c.store && c.store !== 'Unknown' && c.store !== 'Steam' && (
                  <Chip>{STORES.find(s => s.id === c.store)?.label ?? c.store}</Chip>
                )}
                {c.hasSteamCloud && <Chip>Steam Cloud</Chip>}
                <Chip tone={c.path ? 'ok' : 'warn'}>{c.path ? 'Detected' : 'Not detected'}</Chip>
              </div>
              {c.path ? (
                // A detected folder can be the wrong one (a launcher's, another profile's), and it has
                // to be correctable before the game is added — the old toolbar button did this.
                <div className="sl-inline" style={{ marginTop: 4 }}>
                  <span className="sl-path">{c.path}</span>
                  <Button
                    size="sm"
                    variant="quiet"
                    aria-label={`Change the save folder for ${c.name}`}
                    onClick={e => { e.preventDefault(); void pickFolderFor(c) }}
                  >
                    Change
                  </Button>
                </div>
              ) : (
                // The per-row button is what a Deck user actually hits — it appears exactly on the
                // rows that block enrollment.
                <div className="sl-inline" style={{ marginTop: 6 }}>
                  <span style={{ color: 'var(--color-watch-ink)', fontSize: 12 }}>No save folder set</span>
                  <Button
                    size="sm"
                    onClick={e => { e.preventDefault(); void pickFolderFor(c) }}
                  >
                    <FolderSearch size={12} strokeWidth={1.75} aria-hidden="true" />
                    Set save folder
                  </Button>
                </div>
              )}
            </div>
          </label>
        ))}
      </Card>

      {/* Launch setup appears once a game is enrolled — the "success state" (Linux only; the card
          hides itself when there is no command, i.e. on Windows). */}
      {enrolled && <LaunchSetupCard />}

      <div className="sl-footbar">
        {enrolling && <EnrollProgressBar progress={progress} picked={checked.size} />}
        <span className="sl-footbar__txt" style={enrollBlocked && !status ? { color: 'var(--color-watch-ink)' } : undefined}>
          {footText}
        </span>
        <div className="sl-inline">
          <Button size="sm" variant="quiet" disabled={checked.size === 0 || busy} onClick={() => setChecked(new Set())}>Clear</Button>
          <Button variant="primary" disabled={busy || checked.size === 0 || enrollBlocked} onClick={() => void enroll()}>
            {checked.size === 0 ? 'Add games' : `Add ${checked.size} game${checked.size === 1 ? '' : 's'}`}
          </Button>
        </div>
      </div>

      {picker.browsing && (
        <PathBrowserModal
          gameName={picker.browsing.name}
          initialPath={picker.browsing.start}
          onConfirm={path => void picker.confirmBrowsed(path)}
          onCancel={picker.cancel}
        />
      )}
    </div>
  )
}
