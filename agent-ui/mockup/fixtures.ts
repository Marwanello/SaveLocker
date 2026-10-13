/**
 * Fixtures for the clickable mockup: a fleet, a library with every filter axis populated (every
 * emulator, several consoles, Heroic stores, Playnite, shortcuts) and one genuine open conflict.
 * All of it is plain data shaped like the agent's own DTOs — nothing here is UI.
 */

const now = Date.now()
export const iso = (msAgo: number) => new Date(now - msAgo).toISOString()
const MIN = 60_000
const HOUR = 60 * MIN
const DAY = 24 * HOUR

export const IDS = {
  hades: 'a0000000-0000-4000-8000-000000000001',
  cyberpunk: 'a0000000-0000-4000-8000-000000000002',
  bg3: 'a0000000-0000-4000-8000-000000000003',
  chrono: 'a0000000-0000-4000-8000-000000000004',
  pop: 'a0000000-0000-4000-8000-000000000005',
  v6: 'b0000000-0000-4000-8000-000000000006',
  v7: 'b0000000-0000-4000-8000-000000000007',
  v8: 'b0000000-0000-4000-8000-000000000008',
  conflict: 'c0000000-0000-4000-8000-000000000001',
  machine: 'd0000000-0000-4000-8000-000000000001',
  otherMachine: 'd0000000-0000-4000-8000-000000000002',
}

export const MACHINE_NAME = 'Steam Deck — Maro'
export const OTHER_MACHINE = 'Living Room PC'

/* ------------------------------------------------------------------ tracked games */

export const db = {
  conflictOpen: true,
  games: [
    {
      id: IDS.hades, name: 'Hades II', path: '/home/deck/Games/Hades II/saves',
      processNames: ['Hades2'], alias: null, steamAppId: 1145350,
      pullBeforeLaunchEnabled: null, hasSteamCloud: true, pushAfterExitEnabled: true,
      installDir: '/home/deck/.local/share/Steam/steamapps/common/Hades II',
      lastPushBytes: 4_194_304, lastPushAt: iso(2 * HOUR),
      source: { kind: 'steam', detail: 'Installed game', tags: ['Proton', 'SteamOS'] },
      release: null,
    },
    {
      id: IDS.cyberpunk, name: 'Cyberpunk 2077', path: '/home/deck/Games/Cyberpunk 2077/saves',
      processNames: ['Cyberpunk2077'], alias: null, steamAppId: 1091500,
      pullBeforeLaunchEnabled: true, hasSteamCloud: true, pushAfterExitEnabled: true,
      installDir: '/home/deck/.local/share/Steam/steamapps/common/Cyberpunk 2077',
      lastPushBytes: 118_000_000, lastPushAt: iso(2 * HOUR),
      source: { kind: 'steam', detail: 'Installed game', tags: ['Proton'] },
      release: null,
    },
    {
      id: IDS.bg3, name: "Baldur's Gate 3", path: '/home/deck/Games/BG3/saves',
      processNames: ['bg3'], alias: null, steamAppId: 1086940,
      pullBeforeLaunchEnabled: null, hasSteamCloud: false, pushAfterExitEnabled: true,
      lastPushBytes: 52_428_800, lastPushAt: iso(DAY),
      source: { kind: 'steam', detail: 'Installed game', tags: ['Proton', 'SteamOS'] },
      release: null,
    },
    {
      id: IDS.chrono, name: 'Chrono Trigger', path: '/home/deck/Emulation/saves/retroarch/saves',
      processNames: [], alias: null, steamAppId: null,
      pullBeforeLaunchEnabled: null, hasSteamCloud: false, pushAfterExitEnabled: false,
      lastPushBytes: 12_288, lastPushAt: iso(3 * DAY),
      source: { kind: 'emulator', detail: 'RetroArch', tags: ['SNES', 'Flatpak', 'EmuDeck'] },
      release: { console: 'SNES', region: 'USA', gameIds: ['AQC'], languages: ['English'], namedBy: 'Redump' },
    },
    {
      id: IDS.pop, name: 'Prince of Persia - Warrior Within', path: '',
      processNames: [], alias: null, steamAppId: null,
      pullBeforeLaunchEnabled: null, hasSteamCloud: false, pushAfterExitEnabled: null,
      lastPushBytes: null, lastPushAt: null,
      source: { kind: 'emulator', detail: 'PCSX2', tags: ['PS2', 'Europe', 'EmuDeck'] },
      release: { console: 'PS2', region: 'Europe', gameIds: ['SLES-52822'], languages: ['English', 'French'], namedBy: 'Redump' },
    },
  ] as TrackedGameLike[],

  candidates: [] as CandidateLike[],
}

export interface TrackedGameLike {
  id: string; name: string; path: string; processNames: string[]; alias: null | string
  steamAppId: number | null; pullBeforeLaunchEnabled: boolean | null; hasSteamCloud: boolean | null
  pushAfterExitEnabled: boolean | null; installDir?: string; lastPushBytes?: number | null
  lastPushAt?: string | null; source?: { kind: string; detail: string; tags?: string[] } | null
  release?: { console: string; region?: string | null; gameIds?: string[]; languages?: string[]; namedBy?: string | null } | null
}

interface CandidateLike {
  id: number; name: string; source: string; hasSteamCloud: boolean; path: string
  prefixPath?: string | null; processName?: string | null; store: string
  alsoFound?: { key: string; label: string | null; path: string; mapped: boolean; includeGlobs: string[] }[] | null
  enrolled: boolean; emulatorName?: string | null; emulatorSystem?: string | null
  emulatorRom?: string | null; untouchedSeed: boolean; notSyncable?: string | null
  emulatorConsole?: string | null; region?: string | null; statesOnly: boolean
}

interface Candidate { id: number; name: string; source: string; hasSteamCloud: boolean; path: string; prefixPath: string | null; processName: string | null; store: string; alsoFound: { key: string; label: string | null; path: string; mapped: boolean; includeGlobs: string[] }[] | null; enrolled: boolean; emulatorName: string | null; emulatorSystem: string | null; emulatorRom: string | null; untouchedSeed: boolean; notSyncable: string | null; emulatorConsole: string | null; region: string | null; statesOnly: boolean }

function c(row: Partial<Candidate> & { id: number; name: string; source: string }): Candidate {
  return {
    hasSteamCloud: false, path: '', prefixPath: null, processName: null, store: 'Unknown',
    alsoFound: null, enrolled: false, emulatorName: null, emulatorSystem: null, emulatorRom: null,
    untouchedSeed: false, notSyncable: null, emulatorConsole: null, region: null, statesOnly: false,
    ...row,
  } as Candidate
}

const emuPath = (file: string) => `/home/deck/Emulation/saves/${file}`

db.candidates = [
  c({ id: 1, name: 'Hades II', source: 'SteamInstalled', hasSteamCloud: true, store: 'Steam', enrolled: true, path: '/home/deck/Games/Hades II/saves', processName: 'Hades2' }),
  c({ id: 2, name: 'Cyberpunk 2077', source: 'SteamInstalled', hasSteamCloud: true, store: 'Steam', enrolled: true, path: '/home/deck/Games/Cyberpunk 2077/saves', processName: 'Cyberpunk2077' }),
  c({ id: 3, name: "Baldur's Gate 3", source: 'SteamInstalled', hasSteamCloud: true, store: 'Steam', enrolled: true, path: '/home/deck/Games/BG3/saves', processName: 'bg3' }),
  c({ id: 4, name: 'Elden Ring', source: 'SteamInstalled', hasSteamCloud: true, store: 'Steam', path: '/home/deck/Games/Elden Ring/saves', processName: 'eldenring' }),
  c({ id: 5, name: 'Stardew Valley', source: 'SteamInstalled', hasSteamCloud: true, store: 'Steam' }),
  c({ id: 6, name: 'Diablo IV (Battle.net)', source: 'SteamShortcut', store: 'Unknown', path: '/home/deck/Games/Battle.net/drive_c/…/Diablo IV' }),
  c({ id: 7, name: 'Genshin Impact', source: 'SteamShortcut', path: '/home/deck/Games/Genshin/…/Genshin Impact' }),
  c({ id: 8, name: 'Death Stranding', source: 'Heroic', store: 'Epic', path: '/home/deck/Games/Heroic/Prefixes/death-stranding/…/KojimaProductions' }),
  c({ id: 9, name: 'The Witcher 3', source: 'Heroic', store: 'Gog', path: '/home/deck/Games/Heroic/Prefixes/witcher3/…/The Witcher 3' }),
  c({ id: 10, name: 'Fez', source: 'Heroic', store: 'Amazon', path: '/home/deck/Games/Heroic/Prefixes/fez/…/Fez' }),
  c({ id: 11, name: 'Hollow Knight', source: 'Playnite', store: 'Steam', path: '/home/deck/Games/HollowKnight/…/Hollow Knight' }),
  c({ id: 12, name: 'Stray', source: 'Playnite', store: 'Epic', path: '/home/deck/Games/Stray/…/Stray' }),
  c({ id: 13, name: 'Chrono Trigger', source: 'Emulator', emulatorName: 'RetroArch', emulatorSystem: 'snes', emulatorConsole: 'SNES', emulatorRom: 'Chrono Trigger (USA).sfc', region: 'USA', enrolled: true, path: emuPath('retroarch/saves/Chrono Trigger.srm') }),
  c({ id: 14, name: 'Super Mario World', source: 'Emulator', emulatorName: 'RetroArch', emulatorSystem: 'snes', emulatorConsole: 'SNES', emulatorRom: 'Super Mario World (USA).sfc', region: 'USA', path: emuPath('retroarch/saves/Super Mario World.srm'), alsoFound: [{ key: 'states', label: 'Save states', path: emuPath('retroarch/states'), mapped: true, includeGlobs: [] }, { key: 'config', label: null, path: emuPath('retroarch/config/Super Mario World.cfg'), mapped: true, includeGlobs: [] }] }),
  c({ id: 15, name: 'Castlevania - Symphony of the Night', source: 'Emulator', emulatorName: 'RetroArch', emulatorSystem: 'ps1', emulatorConsole: 'PS1', region: 'Europe', path: emuPath('retroarch/saves/Castlevania SotN.srm') }),
  c({ id: 16, name: 'Metal Slug 3', source: 'Emulator', emulatorName: 'RetroArch', emulatorSystem: 'arcade', emulatorConsole: 'Arcade', path: emuPath('retroarch/saves/metal slug 3.srm'), statesOnly: true }),
  c({ id: 17, name: 'Pokémon HeartGold', source: 'Emulator', emulatorName: 'melonDS', emulatorSystem: 'nds', emulatorConsole: 'DS', path: '/home/deck/Emulation/saves/melonds/Pokemon HeartGold.sav' }),
  c({ id: 18, name: 'Virtua Fighter 2', source: 'Emulator', emulatorName: 'Supermodel', emulatorSystem: 'arcade', emulatorConsole: 'Arcade', path: '/home/deck/Emulation/saves/supermodel/NVRAM', untouchedSeed: true }),
  c({ id: 19, name: 'The Secret of Monkey Island', source: 'Emulator', emulatorName: 'ScummVM', path: '/home/deck/Emulation/saves/scummvm/monkey', emulatorRom: 'monkey1' }),
  c({ id: 20, name: 'Crash Bandicoot', source: 'Emulator', emulatorName: 'DuckStation', emulatorSystem: 'ps1', emulatorConsole: 'PS1', region: 'Europe', path: '/home/deck/Emulation/saves/duckstation/memcards/Crash Bandicoot (E).mcd' }),
  c({ id: 21, name: 'Metal Gear Solid 3 - Snake Eater', source: 'Emulator', emulatorName: 'PCSX2', emulatorSystem: 'ps2', emulatorConsole: 'PS2', region: 'Europe', path: '/home/deck/Emulation/saves/pcsx2/MC1.ps2' }),
  c({ id: 22, name: 'PS2 memory card', source: 'Emulator', emulatorName: 'PCSX2', emulatorSystem: 'ps2', emulatorConsole: 'PS2', path: '/home/deck/Emulation/saves/pcsx2/MC2.ps2', notSyncable: 'Every PS2 game shares this card. Make a folder card in PCSX2 settings to sync saves per game.' }),
  c({ id: 23, name: 'Metroid Prime', source: 'Emulator', emulatorName: 'Dolphin', emulatorSystem: 'gc', emulatorConsole: 'GameCube', region: 'USA', path: '/home/deck/Emulation/saves/dolphin/GC/USA/Card A/Metroid Prime.gci' }),
  c({ id: 24, name: 'Crisis Core -Final Fantasy VII-', source: 'Emulator', emulatorName: 'PPSSPP', emulatorSystem: 'psp', emulatorConsole: 'PSP', path: '/home/deck/Emulation/saves/ppsspp/SAVEDATA/ULES01044' }),
  c({ id: 25, name: 'Heavenly Sword', source: 'Emulator', emulatorName: 'RPCS3', emulatorSystem: 'ps3', emulatorConsole: 'PS3', path: '/home/deck/Emulation/saves/rpcs3/dev_hdd0/home/00000001/savedata/BCES00076' }),
  c({ id: 26, name: 'Shadow of the Colossus', source: 'Emulator', emulatorName: 'shadPS4', emulatorSystem: 'ps4', emulatorConsole: 'PS4', path: '/home/deck/Emulation/saves/shadps4/CUSA08829/savedata' }),
]

export const candidateLinks = {
  reachable: true,
  links: [
    {
      id: 14,
      options: [
        { choice: 'auto', kind: 'join', name: 'Super Mario World', gameId: IDS.chrono, detail: 'A server game keeps exactly these files', badge: 'Same files' },
        { choice: 'own', kind: 'new', name: 'Keep as its own game', gameId: null, detail: 'Syncs under this name on every machine', badge: 'New' },
      ],
    },
    {
      id: 18,
      options: [
        { choice: 'auto', kind: 'join', name: 'Virtua Fighter 2', gameId: null, detail: 'A server game keeps this NVRAM seed', badge: 'Same files' },
      ],
    },
    {
      id: 20,
      options: [
        { choice: 'auto', kind: 'join', name: 'Crash Bandicoot (E)', gameId: IDS.pop, detail: 'A server game keeps exactly these files', badge: 'Same files' },
        { choice: 'own', kind: 'new', name: 'Keep as its own game', gameId: null, detail: 'Syncs under this name on every machine', badge: 'New' },
      ],
    },
  ],
}

/* ------------------------------------------------------------------ versions + conflict */

export const versions = {
  [IDS.v6]: { id: IDS.v6, gameId: IDS.hades, machineId: IDS.machine, machineName: MACHINE_NAME, createdAt: iso(DAY), contentHash: '6f6f1a', size: 21_700_000, parentVersionId: null, protected: false },
  [IDS.v7]: { id: IDS.v7, gameId: IDS.hades, machineId: IDS.machine, machineName: MACHINE_NAME, createdAt: iso(8 * MIN), contentHash: '7a7a2b', size: 24_000_000, parentVersionId: IDS.v6, protected: false },
  [IDS.v8]: { id: IDS.v8, gameId: IDS.hades, machineId: IDS.otherMachine, machineName: OTHER_MACHINE, createdAt: iso(12 * MIN), contentHash: '8b8b3c', size: 23_400_000, parentVersionId: IDS.v6, protected: false },
}

export const stats = {
  [IDS.v6]: { fileCount: 3, newestFileWriteUtc: iso(DAY) },
  [IDS.v7]: { fileCount: 4, newestFileWriteUtc: iso(8 * MIN) },
  [IDS.v8]: { fileCount: 4, newestFileWriteUtc: iso(12 * MIN) },
}

export const openConflict = {
  id: IDS.conflict, gameId: IDS.hades, versionAId: IDS.v8, versionBId: IDS.v7,
  status: 'Open', createdAt: iso(12 * MIN), resolvedVersionId: null, resolvedBy: null,
  resolvedAt: null, machineId: IDS.machine, count: 1, lastSeen: iso(MIN), escalated: false,
}

/* ------------------------------------------------------------------ misc api shapes */

export const state = {
  connected: true, currentVersion: '0.7.0', buildLabel: '0.7.0 · 9f2c1ab', machineName: MACHINE_NAME,
  serverUrl: 'http://savelocker.home.lan:5080', startWithWindows: true, gamesTracked: 4,
  savesBacked: 312, lastSyncAgo: '12m ago', leaseWarnings: [], settleQuietSeconds: 20,
  platform: 'Linux', machineId: IDS.machine, sentTodayBytes: 123_731_886, offlineQueueCount: 1,
  serverTrust: 'plain-http', autoUpdate: true,
}

export const appearance = {
  follow: true,
  effective: { theme: 'system', accent: 'ember', mark: 'pixel' },
  console: { theme: 'system', accent: 'ember', mark: 'pixel' },
  consoleAppliedAt: iso(2 * DAY),
  local: { theme: 'system', accent: 'ember', mark: 'pixel' },
}

export const offlineQueue = [
  { gameId: IDS.hades, gameName: 'Hades II', queuedAt: iso(6 * MIN), attempts: 2, lastAttemptAt: iso(5 * MIN), force: false, size: 24_000_000 },
]

export const folderSuggestions = [
  { gameId: IDS.chrono, gameName: 'Chrono Trigger', key: 'states', path: '/home/deck/Emulation/saves/retroarch/states', deferred: false },
]

export const recentLog = [
  { timestampUtc: iso(12 * MIN), message: 'Conflict on Hades II — this device and the cloud both changed.' },
  { timestampUtc: iso(12 * MIN), message: 'Pushed Hades II (24.0 MB) — waiting behind the conflict.' },
  { timestampUtc: iso(2 * HOUR), message: 'Pulled Cyberpunk 2077 (118 MB) from the server.' },
  { timestampUtc: iso(2 * HOUR), message: 'Pushed Hades II — 4 of 4 files needed fresh bytes (delta).' },
  { timestampUtc: iso(6 * MIN), message: 'Server unreachable — Hades II queued to upload (attempt 2).' },
]

export const syncFixture = {
  startedAt: null as number | null,
  cancelRequested: false,
  lastRun: {
    finishedAtUtc: iso(2 * HOUR), games: 4, uploaded: 2, alreadyCurrent: 2, conflicts: 0,
    failed: 0, bytesSent: 41_000_000, cancelled: false, pulled: 1, queued: 0, unreachable: 0,
  },
}

export const enrollFixture = {
  startedAt: null as number | null,
  total: 0,
  names: [] as string[],
}

/* ------------------------------------------------------------------ browse tree */

const T: Record<string, string[]> = {
  '/': ['home', 'run', 'var'],
  '/home': ['deck'],
  '/home/deck': ['.local', 'Emulation', 'Games', 'Saves'],
  '/home/deck/Saves': ['Hades II', 'Cyberpunk 2077'],
  '/home/deck/Games': ['Hades II', 'Cyberpunk 2077', 'BG3'],
  '/home/deck/Emulation': ['saves'],
  '/home/deck/Emulation/saves': ['retroarch', 'pcsx2', 'duckstation'],
}

export interface BrowseEntry { name: string; path: string }
export interface BrowseListing { path: string; parent: string | null; entries: BrowseEntry[] }

export function browse(path: string): BrowseListing | null {
  const names = T[path]
  if (!names) return null
  const parent = path === '/' ? null : path.replace(/\/[^/]+\/?$/, '') || '/'
  return { path, parent, entries: names.map(n => ({ name: n, path: path === '/' ? `/${n}` : `${path}/${n}` })) }
}

export function artSvg(id: string, name: string): string {
  let hash = 0
  for (const ch of id) hash = (hash * 31 + ch.charCodeAt(0)) % 360
  const letter = (name.trim()[0] ?? '?').toUpperCase()
  return `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
<stop offset="0" stop-color="hsl(${hash} 38% 26%)"/><stop offset="1" stop-color="hsl(${(hash + 40) % 360} 42% 12%)"/>
</linearGradient></defs>
<rect width="512" height="512" fill="url(#g)"/>
<text x="256" y="316" text-anchor="middle" font-family="Archivo, system-ui, sans-serif" font-size="200" font-weight="800" fill="rgba(255,255,255,.88)">${letter}</text>
</svg>`
}

export { MIN, HOUR, DAY }
