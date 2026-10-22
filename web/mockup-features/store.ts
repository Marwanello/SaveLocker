import { useSyncExternalStore } from 'react'
import { G, M, MACHINE_NAMES, iso, DAY, HOUR, MIN } from './fixtures'

/** The console feature mockups' own state. Nothing here is in the real console. */

export type Variant = 'A' | 'B' | 'C' | 'D' | 'E'
export type Where = { view: 'games' | 'config' | 'backups'; game?: string; screen?: 'first-run' | 'sign-in' }

export interface FeatureDef {
  id: string
  task: 'Playthroughs' | 'Version history' | 'Storage'
  name: string
  where: (v: Variant) => Where
  variants: { id: Variant; name: string; what: string; try: string }[]
  pick: string
  always: string[]
  sims?: [string, string][]
}

export const FEATURES: FeatureDef[] = [
  {
    id: 'pt-console', task: 'Playthroughs', name: 'Playthroughs card', where: () => ({ view: 'games', game: G.elden }),
    sims: [['ok', 'Device ready'], ['running', 'Game running on the device']],
    pick: 'D or E, asked for on 2026-10-23: playthroughs and their versions on one card. Versions belong to one playthrough, so A–C’s separate Versions card has no playthrough to show; D and E pick one and show its versions.',
    always: ['Switching a device sends it a command; the device runs the switch with its own checks.', 'Delete is refused while a device plays it or it’s the default; it goes to trash for 14 days.', 'Archive hides it with no time limit.'],
    variants: [
      { id: 'A', name: 'Table', what: 'A Playthroughs card under the stats: latest save, versions, size and which devices play each one, with a row menu.', try: '“Switch a device…” → Steam Deck → Hard mode run. Then “Delete…” on Main (refused).' },
      { id: 'B', name: 'Lanes over time', what: 'Each playthrough is a lane of version dots; clicking one opens its details and actions beside the list.', try: 'Click “Sara’s run” → “Switch a device to this…”.' },
      { id: 'C', name: 'List and detail', what: 'Playthroughs on the left; the chosen one on the right with every device and a “Switch to this” per device.', try: 'Pick “Main”, then “Switch to this” on Battlestation.' },
      { id: 'D', name: 'Side by side', what: 'One card replaces Versions: playthroughs on the left with their devices; the picked one’s versions on the right, with its devices and switch above them.', try: 'Pick “Hard mode run”, name a version, then “Switch a device to this…” → Steam Deck.' },
      { id: 'E', name: 'Tabs over versions', what: 'One card replaces Versions: a tab per playthrough with the devices that play it; under the tab its devices, actions and versions.', try: 'Open the “Sara’s run” tab, Set as Latest on an older version, then switch Battlestation to it.' },
    ],
  },
  {
    id: 'vh-trash', task: 'Version history', name: 'Trash', where: v => (v === 'A' ? { view: 'backups' } : v === 'B' ? { view: 'games', game: G.hollow } : { view: 'config' }),
    pick: 'Chosen: A (2026-10-23), on the Backups page renamed Storage, and always last on it: backups first, then storage use and off-site copies.',
    always: ['Pruned and deleted versions go to trash; nothing is gone until the days run out.', '14 days by default, 7–90 in Configuration.', 'Restoring never makes a version Latest.'],
    variants: [
      { id: 'A', name: 'Trash card on Backups', what: 'A Trash card on the Backups page lists every deleted or pruned version with Restore, and Empty trash.', try: 'Restore the “Patch 7 test” row (its playthrough was deleted), then “Empty trash…”.' },
      { id: 'B', name: 'In each game’s Versions', what: 'The Versions card gets a “Deleted” segment with the game’s trashed versions and Restore.', try: 'Open the “Deleted 2” segment and restore one.' },
      { id: 'C', name: 'Recently deleted panel', what: 'Configuration’s storage line opens a “Recently deleted” panel grouped by game, with multi-select restore.', try: 'Open “Recently deleted”, tick two, Restore.' },
    ],
  },
  {
    id: 'vh-names', task: 'Version history', name: 'Name a version', where: () => ({ view: 'games', game: G.hollow }),
    pick: 'Chosen: B · Name dialog (2026-10-23). Layout fixed: the name has its own column with a pencil to add or change it, so the date and the row’s actions stay as they were.',
    always: ['A named version is never pruned.', 'Removing the name leaves Protect as it was.', 'Names show in conflict cards and Set as Latest too.'],
    variants: [
      { id: 'A', name: 'Inline edit', what: 'A tag button in each row turns the row into a text field.', try: 'Tag a version, then rename “Before the Radiance”.' },
      { id: 'B', name: 'Name dialog', what: 'A Name column: its pencil opens a dialog with suggestions that says named versions are kept.', try: 'Click “Name” on a row, pick a suggestion; click a name to rename or remove it.' },
      { id: 'C', name: 'Named saves strip', what: 'Named versions are pinned above the table; naming is in the row actions.', try: '“Name…” on a row, then use the strip’s “Set as Latest”.' },
    ],
  },
  {
    id: 'vh-keep', task: 'Version history', name: 'How much to keep', where: () => ({ view: 'games', game: G.hollow }),
    pick: 'Chosen: B · Timeline (2026-10-23), behind “Keep history…” on the game’s Versions card; the default for every game is in Configuration › Defaults & maintenance.',
    always: ['Per playthrough; the latest, named and protected versions are always kept.', 'Preview and prune use the same rule.', 'Saving a change that removes versions asks first, and they go to trash.'],
    variants: [
      { id: 'A', name: 'Presets and preview', what: 'Count or thinning; presets with a sentence; custom numbers; a line saying how many go to trash, with the list on request.', try: 'Pick “Short”, “Show which”, then Save.' },
      { id: 'B', name: 'Timeline', what: 'The same controls over a 120-day line where each version is a dot, filled if kept and hollow if it would go. Opened from “Keep history…” on the Versions card; the server-wide default has the same planner in Configuration.', try: 'Versions › “Keep history…” → “Its own setting” → Short. Then Configuration › History to keep › Change….' },
      { id: 'C', name: 'One slider', what: 'One slider from least space to most history, with before/after bars; Advanced shows the full controls.', try: 'Drag the slider, then “Advanced”.' },
    ],
  },
  {
    id: 'vh-restore', task: 'Version history', name: 'Restore one file', where: () => ({ view: 'games', game: G.hollow }),
    sims: [['ok', 'Device ready'], ['running', 'Game running on the device']],
    pick: 'Chosen: B · Pick several (2026-10-23). A restore makes a new save, so the button says “Restore and create save”.',
    always: ['The server makes a new version: the latest save with the picked files taken from the old one. It becomes Latest and each device pulls it, closed-game check included.', 'Download a single file is always there.', 'The old latest stays in history, so Set as Latest undoes it.'],
    variants: [
      { id: 'A', name: 'Per file', what: 'An opened version lists its files; each has “Restore…” and Download.', try: 'Open a version, restore “user3.dat”.' },
      { id: 'B', name: 'Pick several', what: 'Tick files in an opened version and restore them together as a new save.', try: 'Tick two files, “Restore 2 files…”, then “Restore and create save”.' },
      { id: 'C', name: 'Compare with latest', what: 'An opened version is compared with the latest; files that differ get a restore button.', try: 'Restore the file marked “Differs”.' },
    ],
  },
  {
    id: 'st-setup', task: 'Storage', name: 'Set up off-site', where: () => ({ view: 'backups' }), sims: [['ok', 'Works'], ['bad', 'Wrong credentials']],
    pick: 'Chosen: A · Wizard (2026-10-23). Once set up, Off-site copies shows a tile per remote and “Add another remote”: rclone keeps any number of remotes, and each gets its own copy job and settings.',
    always: ['rclone runs inside the server.', 'Google Drive, OneDrive and Dropbox sign in from the browser, with “rclone authorize” as a fallback.', 'Saving needs a passing test.', 'Encryption is off by default so the copy stays readable.'],
    variants: [
      { id: 'A', name: 'Wizard', what: 'A dialog with five steps: where, sign in, what to copy, advanced, test. Afterwards the remote is a tile under Off-site copies, with “Add another remote”.', try: 'Google Drive: open the sign-in page, Connect, Next… Test, turn on. Then “Add another remote” → Folder. Try “Wrong credentials”.' },
      { id: 'B', name: 'One long card', what: 'The Off-site card is the whole form, test at the bottom.', try: 'Pick S3-compatible, fill in, Test.' },
      { id: 'C', name: 'Provider tiles', what: 'Tiles to pick a provider; then only its sign-in and test, then a summary.', try: 'Pick Folder, test, “Change these…”.' },
    ],
  },
  {
    id: 'st-status', task: 'Storage', name: 'Off-site status', where: () => ({ view: 'backups' }),
    pick: 'Chosen: A · Compact card (2026-10-23), one tile per remote, with “What’s on the remote” as a real folder tree.',
    always: ['History on the remote has its own setting, a size cap and per-game skip.', 'Named versions always go; each latest.zip stays.', 'Only files this server uploaded are ever deleted on the remote.'],
    variants: [
      { id: 'A', name: 'Compact card', what: 'A tile per remote: status line, Run now, Pause, the space bar, and two panels: settings, and the remote’s files as a folder tree.', try: 'Run now, then “What’s on the remote” and open Elden Ring › Main › versions.' },
      { id: 'B', name: 'Dashboard', what: 'Four numbers, the space bar, the history setting and the games table on the card.', try: 'Skip two games, watch the space.' },
      { id: 'C', name: 'Activity log', what: 'Tabs: what each run did, games and history, what’s on the remote.', try: 'Read Activity, then “On the remote”.' },
    ],
  },
  {
    id: 'st-full', task: 'Storage', name: 'Remote full', where: v => (v === 'A' ? { view: 'games', game: G.elden } : { view: 'backups' }),
    sims: [['full', 'Drive full'], ['cap', 'Cap reached'], ['breaker', 'Big delete'], ['expired', 'Sign-in expired']],
    pick: 'Chosen: A · Banner and dialog (2026-10-23).',
    always: ['Only the off-site copy pauses; syncing between devices keeps going.', 'The banner stays until it’s resolved.', 'Big deletes stop and ask the same way.'],
    variants: [
      { id: 'A', name: 'Banner + dialog', what: 'A banner on every page; Resolve opens a dialog with the options for this case and a preview of each.', try: 'Switch the cases above; Resolve → Trim → Apply.' },
      { id: 'B', name: 'Banner + panel', what: 'Resolve expands a panel under the banner with the options as cards.', try: 'Resolve, “Skip some games”, Apply.' },
      { id: 'C', name: 'Make-room page', what: 'A page about the remote’s space: per-game use, skip toggles, fewer versions slider.', try: 'Drag the slider, Apply and resume.' },
    ],
  },
  {
    id: 'st-recover', task: 'Storage', name: 'Restore a lost server', where: v => (v === 'A' ? { view: 'games', screen: 'first-run' } : v === 'B' ? { view: 'backups' } : { view: 'games', screen: 'sign-in' }),
    sims: [['linked', 'A remote is set up'], ['none', 'No remote set up']],
    pick: 'Chosen: A and B, both (2026-10-23). B searches the remote this server already has straight away.',
    always: ['Restores a backup from the remote, then adopts the remote so nothing there is deleted.', 'Can import older versions from the copy afterwards.', 'Devices reconnect with their existing keys.'],
    variants: [
      { id: 'A', name: 'First-run screen', what: 'An empty server opens on “Start fresh” or “Restore from an off-site copy”.', try: 'Restore → Folder → Find backups → Restore.' },
      { id: 'B', name: 'On Storage', what: '“Restore from off-site…” next to the local backups. With a remote already set up it lists that remote’s backups at once; otherwise it asks where to look.', try: 'Restore from off-site… (Google Drive is searched at once). Then “No remote set up” and try again.' },
      { id: 'C', name: 'Link on sign-in', what: 'The sign-in page links to the restore, only while the server is empty.', try: '“Lost your server?”.' },
    ],
  },
  {
    id: 'st-convert', task: 'Storage', name: 'Converting to save space', where: () => ({ view: 'backups' }), sims: [['ok', 'Disk OK'], ['low', 'Low disk']],
    pick: 'Chosen: B with A’s big progress bar (2026-10-23): it starts on the first run after upgrading, and the per-game rows fold away.',
    always: ['Starts by itself after the update; syncing never pauses.', 'Each version is checked before it switches; originals stay in trash 14 days.', 'Pauses under 5 GB free; resumes by itself.'],
    variants: [
      { id: 'A', name: 'Popup + progress', what: 'A one-time popup, then a Storage card with progress, saved space, Pause, Verify and Revert.', try: 'Got it, watch it, “Low disk”, then Verify storage.' },
      { id: 'B', name: 'Banner + per game', what: 'On the first run after the upgrade: a notice once, a banner on every page while it runs, and on Storage a big progress bar with the per-game rows folded under it.', try: 'OK the notice, open “Per game”, then “Low disk”.' },
      { id: 'C', name: 'Stacked bar', what: 'A bar of old zips, stored-once data and originals in trash, with four counters.', try: 'Watch the bar shift.' },
    ],
  },
]

/* ------------------------------------------------------------------ data */

export interface Pt { id: string; name: string; when: string; by: string; versions: number; size: number; archived?: boolean }
export interface Remote { id: string; provider: string; where: string; used: number; cap: number; free: number | null; total: number | null }
export interface TrashItem { id: string; game: string; pt: string; made: string; why: string; size: number; left: number; ptGone?: boolean }

interface MockState {
  feature: string
  variant: Variant
  sim: string
  notes: boolean
  navSeq: number
  pts: Pt[]
  def: string
  plays: Record<string, string>
  trash: TrashItem[]
  trashDays: number
  labels: Record<string, string | null>
  flow: { steps: { t: string; d?: string }[]; i: number; state: 'run' | 'ok' | 'fail'; failMsg?: string } | null
  offsite: { on: boolean; provider: string; skip: Record<string, boolean>; paused: boolean; fixed: string | null; remotes: Remote[] }
  conv: number
  convPaused: boolean
  convSeen: boolean
}

export const GDRIVE: Remote = { id: 'r1', provider: 'Google Drive', where: 'marwan@gmail.com · My Drive/SaveLocker', used: 6.4, cap: 10, free: 11.2, total: 15 }

function fresh(feature: string, variant: Variant, navSeq: number): MockState {
  const f = FEATURES.find(x => x.id === feature)!
  return {
    feature, variant, sim: f.sims?.[0][0] ?? 'ok', notes: true, navSeq,
    pts: [
      { id: 'main', name: 'Main', when: iso(9 * MIN), by: 'Steam Deck', versions: 9, size: 2_830_000 },
      { id: 'hard', name: 'Hard mode run', when: iso(2 * HOUR), by: 'Battlestation', versions: 6, size: 2_790_000 },
      { id: 'sara', name: 'Sara’s run', when: iso(3 * DAY), by: 'Sara’s laptop', versions: 9, size: 2_610_000 },
      { id: 'patch', name: 'Patch 1.12 test', when: iso(35 * DAY), by: 'Battlestation', versions: 2, size: 2_600_000, archived: true },
    ],
    def: 'main',
    plays: { [M.pc]: 'hard', [M.deck]: 'main', [M.living]: 'main', [M.sara]: 'sara' },
    trash: [
      { id: 't1', game: 'Hollow Knight', pt: 'Main', made: 'Oct 3, 18:40 · Battlestation', why: 'Pruned', size: 1_460_000, left: 13 },
      { id: 't2', game: 'Hollow Knight', pt: 'Main', made: 'Oct 2, 21:12 · Steam Deck', why: 'Pruned', size: 1_450_000, left: 12 },
      { id: 't3', game: 'Elden Ring', pt: 'Sara’s run', made: 'Sep 30, 20:05 · Sara’s laptop', why: 'Deleted by you', size: 2_610_000, left: 9 },
      { id: 't4', game: 'Elden Ring', pt: 'Patch 7 test', made: 'Sep 12, 10:30 · Battlestation', why: 'Playthrough deleted', size: 2_600_000, left: 6, ptGone: true },
      { id: 't5', game: 'Cyberpunk 2077', pt: 'Main', made: 'Sep 28, 23:51 · Living Room PC', why: 'Pruned', size: 18_900_000, left: 2 },
    ],
    trashDays: 14,
    labels: {},
    flow: null,
    offsite: {
      on: feature !== 'st-setup', provider: 'Google Drive', skip: {}, paused: false, fixed: null,
      remotes: feature === 'st-setup' ? [] : [GDRIVE],
    },
    conv: 0, convPaused: false, convSeen: false,
  }
}

let state: MockState = (() => {
  let feature = FEATURES[0].id
  try { feature = localStorage.getItem('slc:feature') ?? feature } catch { /* storage off */ }
  const hash = location.hash.slice(1)
  if (FEATURES.some(f => f.id === hash)) feature = hash
  if (!FEATURES.some(f => f.id === feature)) feature = FEATURES[0].id
  return fresh(feature, 'A', 1)
})()
const subs = new Set<() => void>()
let timer: number | undefined
export const timers = { conv: undefined as number | undefined }

export function getState() { return state }
export function setState(patch: Partial<MockState>) { state = { ...state, ...patch }; subs.forEach(f => f()) }
export function useMock<T>(pick: (s: MockState) => T): T {
  return useSyncExternalStore(cb => { subs.add(cb); return () => subs.delete(cb) }, () => pick(state))
}
export const featureDef = () => FEATURES.find(f => f.id === state.feature)!
export const where = () => featureDef().where(state.variant)

function stopTimers() { window.clearTimeout(timer); window.clearTimeout(timers.conv) }
export function chooseFeature(id: string) {
  stopTimers()
  try { localStorage.setItem('slc:feature', id) } catch { /* storage off */ }
  state = fresh(id, 'A', state.navSeq + 1); subs.forEach(f => f())
}
export function chooseVariant(v: Variant) {
  stopTimers()
  state = { ...fresh(state.feature, v, state.navSeq + 1), notes: state.notes }; subs.forEach(f => f())
}

export function runFlow(steps: { t: string; d?: string }[], opts: { failAt?: number; failMsg?: string; done?: (ok: boolean) => void } = {}) {
  window.clearTimeout(timer)
  const failAt = opts.failAt ?? -1
  setState({ flow: { steps, i: 0, state: 'run', failMsg: opts.failMsg } })
  const tick = () => {
    const f = state.flow
    if (!f || f.state !== 'run') return
    if (f.i === failAt) { setState({ flow: { ...f, state: 'fail' } }); opts.done?.(false); return }
    if (f.i + 1 >= f.steps.length) { setState({ flow: { ...f, i: f.steps.length, state: 'ok' } }); opts.done?.(true); return }
    setState({ flow: { ...f, i: f.i + 1 } })
    timer = window.setTimeout(tick, 650)
  }
  timer = window.setTimeout(tick, 650)
}
export const endFlow = () => setState({ flow: null })

export const ptById = (id: string) => state.pts.find(p => p.id === id)!
export const devicesOn = (ptId: string) => Object.entries(state.plays).filter(([, p]) => p === ptId).map(([m]) => MACHINE_NAMES[m])
export { M, MACHINE_NAMES }
