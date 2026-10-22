import { useSyncExternalStore } from 'react'
import { IDS } from '../mockup/fixtures'

/** The feature mockups' own state: which feature and variant is on show, the "Mock only" switches, and the planned
 *  features' data (playthroughs, names, copies). Nothing here is in the real agent. */

export type Variant = 'A' | 'B' | 'C'
export type Sim = 'online' | 'offline' | 'running'

export interface FeatureDef {
  id: string
  task: 'Playthroughs' | 'Version history'
  name: string
  /** The app's own deep link for the screen the feature lives on. */
  route: string
  variants: { id: Variant; name: string; what: string; try: string }[]
  pick: string
  always: string[]
  sims?: Sim[]
}

export const DEMO_GAME = IDS.bg3
export const DEMO_NAME = "Baldur's Gate 3"

export const FEATURES: FeatureDef[] = [
  {
    id: 'pt-switch', task: 'Playthroughs', name: 'Switch on this device', route: `#game:${DEMO_GAME}`, sims: ['online', 'offline', 'running'],
    pick: 'B · Header switcher. It keeps the game page as it is and puts the one choice next to the game’s name.',
    always: [
      'The save you leave is uploaded to its own playthrough before the other goes in the folder.',
      'Refused while the game runs; nothing changes on a refusal.',
      'Each device picks its own playthrough.',
    ],
    variants: [
      { id: 'A', name: 'Playthroughs card', what: 'A card under the stats lists each playthrough with its latest save and device. “Play here” opens a dialog that explains, then shows each step.', try: 'Play here on “Main”. Then try “Game running”, and “Server unreachable” on “Sara’s Durge run”.' },
      { id: 'B', name: 'Header switcher', what: 'A chip under the game’s name says which playthrough this device plays. It opens a list; picking one asks once, then switches.', try: 'Open the “Honour mode” chip and pick “Main”. “New playthrough…” is at the bottom of the list.' },
      { id: 'C', name: 'Playthrough tabs', what: 'A segmented control above the stats, one segment per playthrough. Another segment shows its versions read-only, with “Play this one here”.', try: 'Pick “Main”, then “Play this one here”.' },
    ],
  },
  {
    id: 'pt-copies', task: 'Playthroughs', name: 'Copies on this device', route: '#settings',
    pick: 'B · Explained choices. The three modes trade disk for offline switching, and B says so with the space each costs.',
    always: ['Default: only until uploaded.', 'Files shared between playthroughs are stored once.', 'A copy not uploaded yet is never cleared.'],
    variants: [
      { id: 'A', name: 'One setting', what: 'One row in Settings with a three-way switch and a line about the space used.', try: 'Pick “Keep for offline”, then “Clear copies…”.' },
      { id: 'B', name: 'Explained choices', what: 'Three choice cards that say what each keeps and costs, and in “Keep” a table of what’s kept per game.', try: 'Pick “Keep for offline switching” and read the table.' },
      { id: 'C', name: 'Per-game override', what: 'A default for every game and a choice per game, so one huge save can skip copies.', try: 'Set Cyberpunk 2077 to “Only until uploaded”.' },
    ],
  },
  {
    id: 'vh-names', task: 'Version history', name: 'Name a version', route: `#game:${DEMO_GAME}`,
    pick: 'B · Name dialog. It says that named versions are kept, and its suggestions save typing on the Deck.',
    always: ['A named version is never pruned.', 'Removing the name leaves Protect as it was.', 'Up to 80 characters.'],
    variants: [
      { id: 'A', name: 'Inline edit', what: 'A tag button on each row turns it into a text field.', try: 'Tag the version from yesterday, then rename “Before the Netherbrain”.' },
      { id: 'B', name: 'Name dialog', what: '“Name…” opens a dialog with suggestions and says named versions are kept.', try: 'Name a version with a suggestion; remove the name from another.' },
      { id: 'C', name: 'Named saves strip', what: 'Named versions are pinned above the list; naming is in each row’s menu.', try: 'Open a row’s ••• menu → “Name this save…”.' },
    ],
  },
  {
    id: 'vh-restore', task: 'Version history', name: 'Restore one file', route: `#game:${DEMO_GAME}`, sims: ['online', 'running'],
    pick: 'C · Compare and restore. It shows which files differ before you choose, so you don’t restore an identical file.',
    always: ['Same guards as a pull: game closed, inside the save folder.', 'The file is checked before it replaces anything; the old one is kept for undo.', 'The latest version doesn’t change; the next sync uploads a new one.'],
    variants: [
      { id: 'A', name: 'Restore per file', what: 'Open a version to see its files; each has Restore.', try: 'Open the version from yesterday and restore “HonourMode.lsv”.' },
      { id: 'B', name: 'Pick several', what: 'Tick files in an opened version, then restore them together.', try: 'Tick two files, then “Restore 2 files”.' },
      { id: 'C', name: 'Compare and restore', what: 'An opened version shows this device beside it; files that differ have a restore arrow.', try: 'Restore the file marked “Differs”.' },
    ],
  },
]

/* ------------------------------------------------------------------ data */

const MIN = 60_000, HOUR = 60 * MIN, DAY = 24 * HOUR
const ago = (ms: number) => new Date(Date.now() - ms).toISOString()

export interface Playthrough { id: string; name: string; when: string; by: string; versions: number; size: number; devices: string[]; local: boolean; archived?: boolean }

export interface DemoVersion { id: string; createdAt: string; machineName: string; size: number; protected?: boolean }
export const DEMO_VERSIONS: DemoVersion[] = [
  { id: 'v-1', createdAt: ago(20 * MIN), machineName: 'Steam Deck — Maro', size: 52_428_800 },
  { id: 'v-2', createdAt: ago(5 * HOUR), machineName: 'Living Room PC', size: 51_900_000 },
  { id: 'v-3', createdAt: ago(DAY + 3 * HOUR), machineName: 'Steam Deck — Maro', size: 51_200_000 },
  { id: 'v-4', createdAt: ago(3 * DAY), machineName: 'Living Room PC', size: 50_100_000, protected: true },
  { id: 'v-5', createdAt: ago(6 * DAY), machineName: 'Steam Deck — Maro', size: 49_800_000 },
]
export const DEMO_FILES = [
  { path: 'HonourMode.lsv', version: 31_457_280, here: 33_554_432, differs: true },
  { path: 'HonourMode.WebP', version: 184_320, here: 184_320 },
  { path: 'QuickSave_12.lsv', version: 19_922_944, here: 19_922_944 },
  { path: 'Autosave_44.lsv', version: 0, here: 20_971_520, onlyHere: true },
  { path: 'profile8.lsf', version: 2_048, here: 2_048 },
]

interface MockState {
  feature: string
  variant: Variant
  sim: Sim
  notes: boolean
  pts: Playthrough[]
  selected: string
  copies: 'until' | 'keep' | 'off'
  copyOverrides: Record<string, string>
  labels: Record<string, string | null>
  flow: { steps: { t: string; d?: string }[]; i: number; state: 'run' | 'ok' | 'fail'; failMsg?: string } | null
}

function fresh(feature = FEATURES[0].id, variant: Variant = 'A'): MockState {
  return {
    feature, variant, sim: 'online', notes: true,
    pts: [
      { id: 'main', name: 'Main', when: ago(2 * HOUR), by: 'Living Room PC', versions: 14, size: 52_000_000, devices: ['Living Room PC'], local: true },
      { id: 'honour', name: 'Honour mode', when: ago(20 * MIN), by: 'Steam Deck — Maro', versions: 6, size: 52_428_800, devices: ['Steam Deck — Maro'], local: true },
      { id: 'durge', name: 'Sara’s Durge run', when: ago(3 * DAY), by: 'Sara’s laptop', versions: 9, size: 48_000_000, devices: ['Sara’s laptop'], local: false },
      { id: 'patch', name: 'Patch 7 test', when: ago(40 * DAY), by: 'Steam Deck — Maro', versions: 2, size: 47_000_000, devices: [], local: false, archived: true },
    ],
    selected: 'honour',
    copies: 'until',
    copyOverrides: {},
    labels: { 'v-3': 'Before the Netherbrain' },
    flow: null,
  }
}

let state: MockState = (() => {
  let feature = FEATURES[0].id
  try { feature = localStorage.getItem('slf:feature') ?? feature } catch { /* storage off */ }
  const hash = location.hash.slice(1)
  if (FEATURES.some(f => f.id === hash)) feature = hash
  if (!FEATURES.some(f => f.id === feature)) feature = FEATURES[0].id
  return fresh(feature)
})()
const subs = new Set<() => void>()
let timer: number | undefined

export function getState() { return state }
export function setState(patch: Partial<MockState>) { state = { ...state, ...patch }; subs.forEach(f => f()) }
export function useMock<T>(pick: (s: MockState) => T): T {
  return useSyncExternalStore(cb => { subs.add(cb); return () => subs.delete(cb) }, () => pick(state))
}

export const featureDef = () => FEATURES.find(f => f.id === state.feature)!

/** Open the screen the current feature lives on (the app reads the hash and drops it). */
export function goToFeature() {
  location.hash = featureDef().route
  window.dispatchEvent(new HashChangeEvent('hashchange'))
}

export function chooseFeature(id: string) {
  window.clearTimeout(timer)
  try { localStorage.setItem('slf:feature', id) } catch { /* storage off */ }
  state = fresh(id, 'A'); subs.forEach(f => f())
  goToFeature()
}
export function chooseVariant(v: Variant) {
  window.clearTimeout(timer)
  state = { ...fresh(state.feature, v), notes: state.notes }; subs.forEach(f => f())
}

/** Run a list of steps one by one; `failAt` stops there with `failMsg`. */
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
