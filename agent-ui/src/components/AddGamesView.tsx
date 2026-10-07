import { useState, useEffect, useCallback, useMemo } from 'react'
import { RefreshCw, FolderSearch, Check, SearchCheck, TriangleAlert, Ban } from 'lucide-react'
import type { Candidate, EnrollProgress, LinkOption } from '../types'
import { api } from '../api'
import { useFolderPicker } from '../useFolderPicker'
import { PathBrowserModal } from './PathBrowserModal'
import { ServerGameLink, type LinkPick } from './ServerGameLink'
import { LaunchSetupCard } from './LaunchSetupCard'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'
import { PageHead } from './ui/PageHead'
import { Switch } from './ui/Switch'

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
type FilterId = 'suggested' | 'all' | 'steam' | 'shortcut' | 'heroic' | 'playnite' | 'emulator'

const FILTERS: { id: FilterId; label: string; hint: string; match: (c: Candidate) => boolean }[] = [
  { id: 'suggested', label: 'Suggested', hint: 'Everything except games Steam Cloud already backs up', match: c => !c.hasSteamCloud },
  { id: 'all', label: 'All', hint: 'Every game found, including Steam Cloud titles', match: () => true },
  { id: 'steam', label: 'Steam', hint: 'Games installed from the Steam store', match: c => c.source === 'SteamInstalled' },
  { id: 'shortcut', label: 'Added to Steam', hint: 'Non-Steam games you added to your Steam library', match: c => c.source === 'SteamShortcut' },
  { id: 'heroic', label: 'Heroic', hint: 'Games installed through Heroic Games Launcher', match: c => c.source === 'Heroic' },
  { id: 'playnite', label: 'Playnite', hint: 'Games in your Playnite library', match: c => c.source === 'Playnite' },
  { id: 'emulator', label: 'Emulators', hint: 'Games found by their save files in an emulator’s saves folder', match: c => c.source === 'Emulator' },
]

/** The emulators SaveLocker reads saves from, as a second axis under the Emulators filter — one chip
 * each, so adding an emulator means adding it here too. `id` is the agent's `emulatorName`. */
const EMULATORS: { id: string; label: string }[] = [
  { id: 'RetroArch', label: 'RetroArch' },
  { id: 'melonDS', label: 'melonDS' },
  { id: 'Supermodel', label: 'Supermodel' },
  { id: 'Model 2', label: 'Model 2' },
  { id: 'ScummVM', label: 'ScummVM' },
  { id: 'PCSX2', label: 'PCSX2' },
  { id: 'DuckStation', label: 'DuckStation' },
  { id: 'Dolphin', label: 'Dolphin' },
  { id: 'PrimeHack', label: 'PrimeHack' },
]

/**
 * Path-detection state as its own axis, not a source filter — it used to be a `nopath` entry in
 * FILTERS ("Needs path"), which meant it competed with Steam/Heroic/etc. instead of combining with
 * them: there was no way to ask for "Heroic games still missing a path". Always visible, same idea
 * as the Store row under Heroic, and stacks with the source filter and store above.
 */
type PathMode = 'all' | 'has' | 'missing' | 'enrolled'

/** `enrolled` is only offered while Hide enrolled is off. Detected / Not detected leave enrolled games
 * out, so each game sits under exactly one of the three. */
const PATH_MODES: { id: PathMode; label: string; match: (c: Candidate) => boolean }[] = [
  { id: 'all', label: 'All', match: () => true },
  { id: 'has', label: 'Detected', match: c => !c.enrolled && !!c.path },
  { id: 'missing', label: 'Not detected', match: c => !c.enrolled && !c.path },
  { id: 'enrolled', label: 'Enrolled', match: c => !!c.enrolled },
]

const HIDE_ENROLLED_KEY = 'savelocker.agent.addGames.hideEnrolled'

function readHideEnrolled(): boolean {
  try { return localStorage.getItem(HIDE_ENROLLED_KEY) !== '0' } catch { return true }
}

const STATUS = {
  enrolled: { Icon: Check, label: 'Enrolled on this machine', tone: 'ok' },
  has: { Icon: SearchCheck, label: 'Save folder detected', tone: 'has' },
  missing: { Icon: TriangleAlert, label: 'Save folder not detected', tone: 'warn' },
  blocked: { Icon: Ban, label: 'Can’t be synced as it is', tone: 'warn' },
} as const

function StatusMark({ c }: { c: Candidate }) {
  const s = STATUS[c.enrolled ? 'enrolled' : c.notSyncable ? 'blocked' : c.path ? 'has' : 'missing']
  return (
    <span className={`sl-status sl-status--${s.tone}`} role="img" aria-label={s.label} title={s.label}>
      <s.Icon size={13} strokeWidth={2.3} aria-hidden="true" />
    </span>
  )
}

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
  const [emulator, setEmulator] = useState<string | null>(null)
  const [pathMode, setPathMode] = useState<PathMode>('all')
  const [hideEnrolled, setHideEnrolledState] = useState(readHideEnrolled)
  const setHideEnrolled = (hide: boolean) => {
    setHideEnrolledState(hide)
    if (hide && pathMode === 'enrolled') setPathMode('all')
    try { localStorage.setItem(HIDE_ENROLLED_KEY, hide ? '1' : '0') } catch { /* private window / blocked storage */ }
  }
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

  // Which server game each emulator save joins, asked once per list. A fresh scan renumbers the
  // candidates, so every hand-made choice is dropped with it.
  const [links, setLinks] = useState<Map<number, LinkOption[]>>(new Map())
  // False when the agent could not ask the server: the rows say the link is decided when the game is added.
  const [linksReachable, setLinksReachable] = useState(true)
  const [linkPicks, setLinkPicks] = useState<Map<number, LinkPick>>(new Map())
  const [linkOpen, setLinkOpen] = useState<number | null>(null)
  useEffect(() => {
    setLinkPicks(new Map())
    setLinkOpen(null)
    setLinksReachable(true)
    if (!candidates.some(c => c.source === 'Emulator' && !c.enrolled)) { setLinks(new Map()); return }
    let live = true
    api.candidateLinks()
      .then(r => {
        if (!live) return
        setLinks(new Map(r.links.map(l => [l.id, l.options])))
        setLinksReachable(r.reachable)
      })
      .catch(() => { if (live) { setLinks(new Map()); setLinksReachable(false) } })
    return () => { live = false }
  }, [candidates])
  // EmuDeck's preinstalled saves, never played here, are not the user's games: one is listed only when a
  // server game keeps it, so this machine can take the fleet's save in its place.
  const listed = useMemo(
    () => candidates.filter(c => !c.untouchedSeed || c.enrolled || links.get(c.id)?.[0]?.kind === 'join'),
    [candidates, links])
  const pickLink = (id: number, pick: LinkPick | undefined) => {
    setLinkPicks(prev => {
      const next = new Map(prev)
      if (pick) next.set(id, pick)
      else next.delete(id)
      return next
    })
    setLinkOpen(null)
  }

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

  // "Also found" folders the user unticked. Ticked is the default: every folder the manifest names for
  // the game that exists here is added with it unless the user says otherwise.
  const [alsoOff, setAlsoOff] = useState<Set<string>>(new Set())
  const alsoId = (c: Candidate, path: string) => `${c.id}\n${path}`
  const toggleAlso = (c: Candidate, path: string) => setAlsoOff(prev => {
    const next = new Set(prev)
    const id = alsoId(c, path)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    return next
  })

  const enroll = async () => {
    if (checked.size === 0 || missing.length > 0) return
    setEnrolling(true)
    setProgress(null)
    setStatus('')
    try {
      const alsoSync = [...checked].flatMap(id => {
        const c = candidates.find(x => x.id === id)
        const paths = (c?.alsoFound ?? []).map(f => f.path).filter(p => c && !alsoOff.has(alsoId(c, p)))
        return c && paths.length > 0 ? [{ id, paths }] : []
      })
      const linked = [...checked].flatMap(id => {
        const pick = linkPicks.get(id)
        return pick ? [{ id, choice: pick.choice, gameId: pick.gameId ?? null }] : []
      })
      const result = await api.enroll([...checked], alsoSync, linked)
      setStatus(
        `Added ${result.enrolled} game${result.enrolled === 1 ? '' : 's'}.` +
        (result.skipped > (result.notes?.length ?? 0) ? ` Skipped ${result.skipped - (result.notes?.length ?? 0)} already tracked.` : '') +
        (result.notes?.length ? ` Not added: ${result.notes.join(' ')}` : '')
      )
      if (result.enrolled > 0) setEnrolled(true)
      setChecked(new Set())
      setAlsoOff(new Set())
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
  const pool = useMemo(
    () => hideEnrolled ? listed.filter(c => !c.enrolled) : listed,
    [listed, hideEnrolled])
  const enrolledCount = listed.filter(c => c.enrolled).length

  const chips = useMemo(
    () => FILTERS
      .map(f => ({ ...f, count: pool.filter(f.match).length }))
      .filter(f => f.count > 0 || f.id === 'suggested' || f.id === 'all'),
    [pool])

  const stores = useMemo(() => {
    if (filter !== 'heroic' && filter !== 'playnite') return []
    const source = filter === 'heroic' ? 'Heroic' : 'Playnite'
    const inSource = pool.filter(c => c.source === source)
    return STORES
      .map(s => ({ ...s, count: inSource.filter(c => c.store === s.id).length }))
      .filter(s => s.count > 0)
  }, [pool, filter])

  const emulators = useMemo(() => {
    if (filter !== 'emulator') return []
    const emulated = pool.filter(c => c.source === 'Emulator')
    return EMULATORS.map(e => ({ ...e, count: emulated.filter(c => c.emulatorName === e.id).length }))
  }, [pool, filter])

  const active = FILTERS.find(f => f.id === filter) ?? FILTERS[0]
  // Filtered by source (+ store) but not yet by path — the base the path row's own counts are
  // drawn against, so "Detected" and "Not detected" describe the set the user is already looking at.
  const sourceFiltered = pool
    .filter(active.match)
    .filter(c => !((filter === 'heroic' || filter === 'playnite') && store) || c.store === store)
    .filter(c => !(filter === 'emulator' && emulator) || c.emulatorName === emulator)
  const activePathMode = PATH_MODES.find(p => p.id === pathMode) ?? PATH_MODES[0]
  const needle = query.trim().toLowerCase()
  const visible = sourceFiltered
    .filter(activePathMode.match)
    .filter(c => !needle || c.name.toLowerCase().includes(needle) || c.path.toLowerCase().includes(needle))

  const enrollBlocked = missing.length > 0
  // Named, not just counted. A user looking for a game they can plainly see in Steam needs to be
  // told it was filtered and by which control — silently showing a shorter list reads as "the scan
  // didn't find it".
  const hiddenCount = pool.length - visible.length
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
    : `${active.hint}${hiddenCount > 0 ? '.' + undoHint : ''}` +
      (hideEnrolled && enrolledCount > 0
        ? `${hiddenCount > 0 ? '' : '.'} ${enrolledCount} enrolled game${enrolledCount === 1 ? ' is' : 's are'} hidden.`
        : '')

  const selectedLine = checked.size === 0
    ? 'Tick the games you want to sync.'
    : `${checked.size} selected · they start syncing after the next time you quit each game`
  const footText = status || (enrollBlocked ? `Set a save folder for: ${missing.map(c => c.name).join(', ')}` : selectedLine)

  return (
    <div className="sl-page">
      <PageHead
        title="Add games"
        sub={`${listed.length} found on this machine · ${listed.filter(c => !c.hasSteamCloud).length} suggested`}
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
              onClick={() => { setFilter(f.id); setStore(null); setEmulator(null) }}
            >
              {f.label} <b>{f.count}</b>
            </button>
          ))}
          <label className="sl-filters__toggle">
            <Switch checked={hideEnrolled} onChange={setHideEnrolled} aria-label="Hide enrolled games" />
            Hide enrolled
          </label>
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

        {/* Which emulator — a second axis like Store, shown while the Emulators filter is on. Shown even
            with one emulator: it says which ones SaveLocker reads saves from. */}
        {emulators.length > 0 && (
          <div className="sl-filters">
            <span className="sl-filters__label">Emulator</span>
            <button type="button" className="sl-fchip" aria-pressed={emulator === null} onClick={() => setEmulator(null)}>All</button>
            {emulators.map(e => (
              <button key={e.id} type="button" className="sl-fchip" aria-pressed={emulator === e.id} onClick={() => setEmulator(e.id)}>
                {e.label} <b>{e.count}</b>
              </button>
            ))}
          </div>
        )}

        {/* Save-path detection — a second axis like Store, so it stacks with the source filter
            instead of replacing it. Always visible: unlike Store it isn't specific to one source. */}
        <div className="sl-filters">
          <span className="sl-filters__label">Save folder</span>
          {PATH_MODES.filter(p => p.id !== 'enrolled' || !hideEnrolled).map(p => (
            <button key={p.id} type="button" className="sl-fchip" aria-pressed={pathMode === p.id} onClick={() => setPathMode(p.id)}>
              {p.id === 'enrolled' && <Check size={12} strokeWidth={2.4} className="sl-fchip__ok" aria-hidden="true" />}
              {p.label}{p.id !== 'all' && <> <b>{sourceFiltered.filter(p.match).length}</b></>}
            </button>
          ))}
        </div>

        <div style={{ fontSize: 12, color: 'var(--color-dim)' }}>{filterLine}</div>
      </div>

      <Card flush>
        {visible.length === 0 ? (
          <div className="sl-empty">
            {scanning ? 'Scanning…' : listed.length === 0 ? 'No games found yet. Rescan once a game has been installed.' : 'Nothing matches that filter.'}
          </div>
        ) : visible.map(c => (
          <label key={c.id} className={c.enrolled ? 'sl-check-row sl-check-row--enrolled' : c.notSyncable ? 'sl-check-row sl-check-row--blocked' : 'sl-check-row'}>
            {/* Enrolling a tracked game again is skipped, so its box is shown ticked and locked. */}
            {/* Named on its own: the row holds buttons too, and their text must not become the box's name. */}
            <input type="checkbox" aria-label={`Add ${c.name}`} checked={!!c.enrolled || checked.has(c.id)} disabled={!!c.enrolled || !!c.notSyncable} onChange={() => toggle(c.id)} />
            <div className="sl-check-row__main">
              <div className="sl-check-row__name">
                <StatusMark c={c} />
                <span className="sl-check-row__title">{c.name}</span>
                <Chip>{c.source}</Chip>
                {c.emulatorName && <Chip>{c.emulatorName}</Chip>}
                {/* The save file's own name: two ROMs (two regions, two consoles) can share a title. */}
                {c.emulatorRom && c.emulatorRom !== c.name && <Chip>{c.emulatorRom}</Chip>}
                {/* Only when it adds something the source does not already say. */}
                {c.store && c.store !== 'Unknown' && c.store !== 'Steam' && (
                  <Chip>{STORES.find(s => s.id === c.store)?.label ?? c.store}</Chip>
                )}
                {c.hasSteamCloud && <Chip>Steam Cloud</Chip>}
              </div>
              {c.enrolled ? (
                <div className="sl-inline" style={{ marginTop: 4 }}>
                  {c.path && <span className="sl-path">{c.path}</span>}
                  <span className="sl-check-row__note">Already added on this machine</span>
                </div>
              ) : c.notSyncable ? (
                // A memory card every game of a console shares: never a game, so no folder to change — the row
                // is here to say why those saves are not offered, and what to switch in the emulator.
                <div style={{ marginTop: 4 }}>
                  {c.path && <span className="sl-path">{c.path}</span>}
                  <span className="sl-check-row__why">{c.notSyncable}</span>
                </div>
              ) : c.path ? (
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
              {!c.enrolled && c.untouchedSeed && (
                <span className="sl-check-row__note" style={{ display: 'block', marginTop: 4 }}>
                  EmuDeck’s preinstalled save, never played here. Adding it takes the server’s save in its place.
                </span>
              )}
              {!c.enrolled && c.source === 'Emulator' && !c.notSyncable && !linksReachable && (
                <span className="sl-check-row__note" style={{ display: 'block', marginTop: 4 }}>
                  The server can’t be reached, so which server game this joins is decided when you add it.
                </span>
              )}
              {!c.enrolled && (links.get(c.id)?.length ?? 0) > 0 && (
                <ServerGameLink
                  title={c.name}
                  options={links.get(c.id)!}
                  pick={linkPicks.get(c.id)}
                  open={linkOpen === c.id}
                  onToggle={() => setLinkOpen(linkOpen === c.id ? null : c.id)}
                  onPick={pick => pickLink(c.id, pick)}
                />
              )}
              {!c.enrolled && c.path && (c.alsoFound ?? []).length > 0 && (
                // Other folders the manifest names for this game that exist here. Ticked by default; the
                // manifest cannot tell saves from settings, so each one can be left out before adding.
                <div style={{ marginTop: 6, display: 'flex', flexDirection: 'column', gap: 4 }}>
                  <span style={{ fontSize: 11.5, color: 'var(--color-dim)' }}>Also found — synced with it unless you untick:</span>
                  {(c.alsoFound ?? []).map(f => {
                    const on = !alsoOff.has(alsoId(c, f.path))
                    return (
                      <span
                        key={f.path}
                        className="sl-inline"
                        style={{ gap: 6 }}
                        // The row is the game's own label: a click here must toggle this folder, not the game.
                        onClick={e => { e.preventDefault(); toggleAlso(c, f.path) }}
                      >
                        <input
                          type="checkbox"
                          checked={on}
                          aria-label={`Also sync ${f.path}`}
                          onClick={e => e.stopPropagation()}
                          onChange={() => toggleAlso(c, f.path)}
                        />
                        <span className="sl-path">{f.path}</span>
                      </span>
                    )
                  })}
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
