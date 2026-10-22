import type {
  AgentHealth, BackupStatus, Game, GameSummary, Machine, MachineGameSource, MachineSavePath, Settings, Version,
  VersionChanges, VersionFolder,
} from '../src/types'

/** Fixtures for the console feature mockups: four machines, eight games, real-looking histories. Shaped by the
 *  console's own types, so the TypeScript check catches any drift from the API. */

const now = Date.now()
export const MIN = 60_000, HOUR = 60 * MIN, DAY = 24 * HOUR
export const iso = (msAgo: number) => new Date(now - msAgo).toISOString()

export const M = {
  pc: 'd0000000-0000-4000-8000-000000000001',
  deck: 'd0000000-0000-4000-8000-000000000002',
  living: 'd0000000-0000-4000-8000-000000000003',
  sara: 'd0000000-0000-4000-8000-000000000004',
}
export const MACHINE_NAMES: Record<string, string> = { [M.pc]: 'Battlestation', [M.deck]: 'Steam Deck', [M.living]: 'Living Room PC', [M.sara]: 'Sara’s laptop' }

export const machines: Machine[] = Object.entries(MACHINE_NAMES).map(([id, name], i) => ({
  id, name, createdAt: iso(120 * DAY), lastSeen: iso((i + 1) * 3 * MIN),
}))

function art(name: string, hue: number): string {
  const letter = name.trim()[0]?.toUpperCase() ?? '?'
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="900"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue} 40% 30%)"/><stop offset="1" stop-color="hsl(${(hue + 40) % 360} 45% 11%)"/></linearGradient></defs><rect width="600" height="900" fill="url(#g)"/><text x="300" y="520" text-anchor="middle" font-family="Archivo, system-ui, sans-serif" font-size="300" font-weight="800" fill="rgba(255,255,255,.88)">${letter}</text></svg>`
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`
}

interface GameSeed { key: string; name: string; hue: number; size: number; count: number; machines: string[]; files: [string, number][]; source: string; retain?: number | null }
const SEEDS: GameSeed[] = [
  { key: 'elden', name: 'Elden Ring', hue: 38, size: 2_830_000, count: 9, machines: [M.pc, M.deck, M.living], files: [['ER0000.sl2', 2_820_000], ['steam_autocloud.vdf', 64]], source: 'Steam' },
  { key: 'hollow', name: 'Hollow Knight', hue: 220, size: 1_460_000, count: 12, machines: [M.pc, M.deck], files: [['user1.dat', 412_000], ['user2.dat', 96_000], ['shared.dat', 3_000], ['Steel Soul/user3.dat', 201_000], ['Steel Soul/user3.dat.bak1', 200_000], ['Old saves/user2.dat', 95_000], ['Old saves/Pantheon/user4.dat', 120_000]], source: 'Steam', retain: 10 },
  { key: 'cyber', name: 'Cyberpunk 2077', hue: 55, size: 18_950_000, count: 6, machines: [M.pc, M.living], files: [['ManualSave-12/sav.dat', 9_400_000], ['AutoSave-3/sav.dat', 9_500_000]], source: 'GOG' },
  { key: 'hades', name: 'Hades II', hue: 140, size: 1_890_000, count: 5, machines: [M.pc, M.deck], files: [['Profile1.sav', 1_880_000], ['Settings.json', 2_104]], source: 'Steam' },
  { key: 'stardew', name: 'Stardew Valley', hue: 95, size: 1_220_000, count: 7, machines: [M.living, M.sara], files: [['Farm_394829102/Farm_394829102', 1_200_000], ['Farm_394829102/SaveGameInfo', 20_000]], source: 'Steam' },
  { key: 'bg3', name: "Baldur's Gate 3", hue: 280, size: 52_400_000, count: 8, machines: [M.pc, M.deck], files: [['HonourMode.lsv', 31_400_000], ['QuickSave_12.lsv', 19_900_000], ['profile8.lsf', 2_048]], source: 'Steam' },
  { key: 'celeste', name: 'Celeste', hue: 330, size: 180_800, count: 4, machines: [M.deck], files: [['0.celeste', 160_000], ['settings.celeste', 20_800]], source: 'Steam' },
  { key: 'balatro', name: 'Balatro', hue: 5, size: 364_100, count: 4, machines: [M.pc, M.deck], files: [['1/save.jkr', 340_000], ['1/profile.jkr', 24_100]], source: 'Steam' },
]

export const G: Record<string, string> = {}
SEEDS.forEach((s, i) => { G[s.key] = `a0000000-0000-4000-8000-${String(i + 1).padStart(12, '0')}` })

export const games: Game[] = SEEDS.map(s => ({
  id: G[s.key], name: s.name, manifestKey: s.name, customPathsJson: null, enabled: true,
  suggestedSaveDir: null, machineSavePath: null, gridUrl: art(s.name, s.hue), heroUrl: null, logoUrl: null, iconUrl: art(s.name, s.hue),
  retainVersions: s.retain ?? null, excludeGlobs: [], conflictPolicy: 'Manual', preferredMachineId: null, includeGlobs: [],
  extraPaths: [], machineSource: { kind: 'steam', detail: s.source, tags: [] }, emulators: [], release: null,
}))

/** Every game's versions, newest first, chained by parent so the console's History view walks them all. */
export const versions: Record<string, Version[]> = {}
const hex8 = (n: number) => ((n * 2654435761) >>> 0).toString(16).padStart(8, '0')
SEEDS.forEach((s, gi) => {
  const list: Version[] = []
  const gaps = [9 * MIN, 5 * HOUR, DAY + 3 * HOUR, 3 * DAY, 6 * DAY, 9 * DAY, 13 * DAY, 20 * DAY, 28 * DAY, 41 * DAY, 55 * DAY, 70 * DAY]
  for (let i = 0; i < s.count; i++) {
    const machineId = s.machines[i % s.machines.length]
    list.push({
      id: `${hex8(gi * 100 + i + 1)}-0000-4000-8000-${String(gi * 100 + i).padStart(12, '0')}`,
      gameId: G[s.key], machineId, machineName: MACHINE_NAMES[machineId], createdAt: iso(gaps[i] ?? (i * 6 * DAY)),
      contentHash: `${s.key}${i}`, size: Math.round(s.size * (1 - i * 0.012)), parentVersionId: null,
      protected: s.key === 'hollow' && i === 5,
    })
  }
  list.forEach((v, i) => { v.parentVersionId = list[i + 1]?.id ?? null })
  versions[G[s.key]] = list
})

export const filesOf = (gameId: string) => SEEDS.find(s => G[s.key] === gameId)?.files ?? [['save.dat', 1_000]]

export function folders(gameId: string, versionIdx: number): VersionFolder[] {
  const files = filesOf(gameId)
  return [{
    key: 'main', fileCount: files.length, totalBytes: files.reduce((a, f) => a + f[1], 0),
    files: files.map(([path, size], i) => ({ path, size: Math.round(size * (1 - versionIdx * 0.01)), modifiedUtc: iso((versionIdx + 1) * HOUR + i * MIN), crc32: 1000 + i + versionIdx })),
  }]
}

export function changes(gameId: string): VersionChanges[] {
  const files = filesOf(gameId)
  return versions[gameId].map((v, i, all) => {
    const first = i === all.length - 1
    const out: VersionChanges = {
      versionId: v.id, baseMissing: false, added: first ? files.length : 0, changed: first ? 0 : 1, removed: 0, unchanged: first ? 0 : files.length - 1,
      files: first ? files.map(([path, size]) => ({ key: 'main', path, change: 'Added' as const, size }))
        : [{ key: 'main', path: files[0][0], change: 'Changed' as const, size: files[0][1] }],
    }
    return out
  })
}

export function summaries(): GameSummary[] {
  return games.map(g => {
    const list = versions[g.id]
    const seed = SEEDS.find(s => G[s.key] === g.id)!
    return {
      game: g, head: list[0] ?? null, lease: null, hasOpenConflict: false,
      totalStorageBytes: list.reduce((a, v) => a + v.size, 0), machineIds: seed.machines,
      sources: seed.machines.map(id => ({ machineId: id, machineName: MACHINE_NAMES[id], source: { kind: 'steam', detail: seed.source, tags: [] }, updatedAt: iso(DAY) })),
    } as unknown as GameSummary
  })
}

export function paths(gameId: string): MachineSavePath[] {
  const seed = SEEDS.find(s => G[s.key] === gameId)!
  const where: Record<string, string> = {
    [M.pc]: `D:\\Games\\Saves\\${seed.name}`,
    [M.deck]: `/home/deck/.local/share/Steam/steamapps/compatdata/1245620/pfx/drive_c/users/steamuser/AppData/Roaming/${seed.name}`,
    [M.living]: `C:\\Users\\Player\\AppData\\Roaming\\${seed.name}`,
    [M.sara]: `C:\\Users\\Sara\\AppData\\Roaming\\${seed.name}`,
  }
  return seed.machines.map(id => ({ machineId: id, machineName: MACHINE_NAMES[id], savePath: where[id], pathKey: 'main' }))
}

export function sources(gameId: string): MachineGameSource[] {
  const seed = SEEDS.find(s => G[s.key] === gameId)!
  return seed.machines.map(id => ({ machineId: id, machineName: MACHINE_NAMES[id], source: { kind: 'steam', detail: seed.source, tags: [] }, updatedAt: iso(DAY) }))
}

const OS: Record<string, { platform: string; os: { id: string; name: string; idLike: string | null; variantId: string | null; device: string | null } }> = {
  [M.pc]: { platform: 'Windows', os: { id: 'windows', name: 'Windows 11', idLike: null, variantId: null, device: null } },
  [M.deck]: { platform: 'Linux', os: { id: 'steamos', name: 'SteamOS', idLike: 'arch', variantId: null, device: 'Steam Deck' } },
  [M.living]: { platform: 'Windows', os: { id: 'windows', name: 'Windows 11', idLike: null, variantId: null, device: null } },
  [M.sara]: { platform: 'Windows', os: { id: 'windows', name: 'Windows 10', idLike: null, variantId: null, device: null } },
}
export const health: AgentHealth[] = machines.map((m, i) => ({
  machineId: m.id, machineName: m.name, online: i < 3, lastHeartbeat: iso((i + 1) * MIN), agentVersion: '0.8.0',
  platform: OS[m.id].platform, lastSyncTime: iso((i + 1) * 9 * MIN), trackedGames: 6 - i, unmappedGames: 0, offlineQueueDepth: 0,
  openEvents: [], os: OS[m.id].os, stagedVersion: null, followsConsoleAppearance: true,
}))

export const settings: Settings = {
  steamGridDbConfigured: true, steamGridDbKeyMasked: '••••a91f', steamGridDbFromConfig: false, adminPasswordSet: true,
  defaultExcludeGlobs: ['*.log', '*.tmp'], autoFetchHours: 24,
  schedule: { mode: 'daily', hours: 24, dayOfWeek: 1, dayOfMonth: 1, timeOfDay: '04:00' }, nextAutoFetchRunAt: iso(-6 * HOUR),
  appearance: { look: { theme: 'system', accent: 'ember', mark: 'pixel' }, pushToAgents: true }, defaultExcludeGlobsFromConsole: true,
  storage: { archiveRoot: '/data/archives', volumeTotalBytes: 40_000_000_000, volumeFreeBytes: 17_800_000_000, archivesBytes: 14_200_000_000, gamesWithArchives: 8, defaultRetainVersions: 10, escalationAfterSeconds: 86_400 },
  manifestRefreshDays: 7, manifestRefreshRequestedAt: null,
}

export const backupStatus: BackupStatus = {
  enabled: true, retentionCount: 7, hourOfDay: 3, nextRunAt: iso(-17 * HOUR), backupRoot: '/data/backups', lastError: null, lastErrorAt: null,
  archivesBytes: 14_200_000_000, archivesCount: 412, frequency: 'daily', dayOfWeek: 1,
  backups: [0, 1, 2, 3].map(i => ({ fileName: `savelocker-202610${String(22 - i).padStart(2, '0')}-030000-scheduled.zip`, sizeBytes: 2_200_000_000 - i * 40_000_000, createdAt: iso(i * DAY + 7 * HOUR), reason: 'Scheduled' as const, includesSaves: true })),
}
