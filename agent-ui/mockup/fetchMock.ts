import {
  IDS, MACHINE_NAME, OTHER_MACHINE, appearance, artSvg, browse, candidateLinks, db,
  enrollFixture, folderSuggestions, iso, offlineQueue, openConflict, recentLog, state,
  stats, syncFixture, versions,
} from './fixtures'

/**
 * The mock backend: every route `src/api.ts` can call, served from fixtures with just enough
 * mutation to make the flows real (sync shows progress, a resolve closes the conflict, enrolling
 * adds the game). Shapes match `api-types.ts`; anything unmapped answers 404 loudly.
 */

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))

/** Counters for the mockup's own test harness. */
const counters = { rescan: 0 }

function json(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    blob: async () => new Blob([JSON.stringify(body)], { type: 'application/json' }),
  }
}

function blobOf(blob: Blob) {
  return { ok: true, status: 200, json: async () => ({}), text: async () => '', blob: async () => blob }
}

/* ---------------------------------------------------------------- activity */

function activityResponse() {
  const s = syncFixture
  let current = {
    gameName: null as string | null, phase: 'Idle', bytesDone: 0, bytesTotal: 0,
    startedAtUtc: null as string | null, index: 0, total: 0, cancelRequested: false,
  }
  const runMs = 5200
  if (s.startedAt !== null) {
    const elapsed = Date.now() - s.startedAt
    const bytesTotal = 118_000_000
    if (elapsed < runMs && !s.cancelRequested) {
      current = {
        gameName: s.game ?? 'Cyberpunk 2077', phase: 'Pushing',
        bytesDone: Math.round(bytesTotal * Math.min(1, elapsed / runMs)), bytesTotal,
        startedAtUtc: new Date(s.startedAt).toISOString(), index: 2, total: 4, cancelRequested: false,
      }
    } else {
      s.lastRun = {
        ...s.lastRun, finishedAtUtc: new Date().toISOString(), games: 4, uploaded: 1, pulled: 1,
        alreadyCurrent: 1, conflicts: db.conflictOpen ? 1 : 0, cancelled: s.cancelRequested,
        bytesSent: bytesTotal,
      }
      s.startedAt = null
      s.cancelRequested = false
      s.game = undefined
    }
  }
  return { current, recent: recentLog, lastRun: s.lastRun }
}

/* ---------------------------------------------------------------- helpers */

function synthVersion(gameId: string, n: number) {
  const name = db.games.find(g => g.id === gameId)?.name ?? 'Game'
  return {
    id: `b0000000-0000-4000-8000-0000000001${String(n).padStart(2, '0')}`,
    gameId, machineId: IDS.machine, machineName: MACHINE_NAME,
    createdAt: iso(n * 60 * 60_000), contentHash: `f${n}acade`, size: 5_000_000 + n * 100_000,
    parentVersionId: null, protected: false, __name: name,
  }
}

function gameState(gameId: string) {
  const game = db.games.find(g => g.id === gameId)
  if (!game) return null
  const head = gameId === IDS.hades ? versions[IDS.v8] : synthVersion(gameId, 2)
  return {
    game: {
      id: game.id, name: game.name, manifestKey: null, customPathsJson: null, enabled: true,
      suggestedSaveDir: game.path || null, machineSavePath: game.path || null,
      gridUrl: null, heroUrl: null, logoUrl: null, iconUrl: null, retainVersions: 10,
      excludeGlobs: gameId === IDS.hades ? ['*.dmp', 'logs/**'] : [], conflictPolicy: 'Manual',
      preferredMachineId: null, includeGlobs: gameId === IDS.chrono ? ['Chrono Trigger.*'] : [],
      extraPaths: [], machineSource: game.source ?? null,
      emulators: game.source?.kind === 'emulator' ? [game.source.detail] : [],
      release: game.release ?? null,
    },
    head, lease: null,
    hasOpenConflict: gameId === IDS.hades && db.conflictOpen,
    totalStorageBytes: 87_000_000,
  }
}

function gameFiles(gameId: string) {
  const game = db.games.find(g => g.id === gameId)
  const folders = gameId === IDS.hades
    ? [{
        key: 'main', label: 'Main save folder', path: game?.path ?? '',
        files: [
          { path: 'Profile1.sav', size: 8_388_608, modifiedUtc: iso(8 * 60_000), state: 'here', missing: false, notInHead: false },
          { path: 'Profile2.sav', size: 7_340_032, modifiedUtc: iso(12 * 60_000), state: 'same', missing: false, notInHead: false },
          { path: 'Settings.json', size: 2_104, modifiedUtc: iso(3 * 60 * 60_000), state: 'same', missing: false, notInHead: false },
        ],
        other: [{ path: 'crash-2026-10-12.dmp', size: 3_145_728, why: 'Excluded by pattern *.dmp' }],
        otherCount: 1,
      }]
    : [{
        key: 'main', label: 'Main save folder', path: game?.path ?? '',
        files: [
          { path: 'save.dat', size: 1_048_576, modifiedUtc: iso(2 * 60 * 60_000), state: 'same', missing: false, notInHead: false },
        ],
        other: [], otherCount: 0,
      }]
  return { folders, head: { versionId: versions[IDS.v8].id, when: iso(12 * 60_000), machine: OTHER_MACHINE }, reachable: true }
}

/* ---------------------------------------------------------------- the router */

async function route(method: string, pathname: string, query: URLSearchParams, body: any): Promise<unknown> {
  const seg = pathname.split('/').filter(Boolean) // ['api', ...]

  if (method === 'GET' && seg[1] === 'state') return state
  if (method === 'GET' && seg[1] === 'activity') return activityResponse()
  if (method === 'GET' && seg[1] === 'appearance') return appearance
  if (method === 'POST' && seg[1] === 'appearance') {
    if (body?.look) {
      appearance.effective = body.look
      appearance.local = body.look
    }
    if (typeof body?.follow === 'boolean') appearance.follow = body.follow
    return appearance
  }

  if (method === 'GET' && seg[1] === 'games' && seg.length === 2) return db.games
  if (seg[1] === 'games' && seg.length === 3 && method === 'POST' && seg[2] !== 'x') return { ok: true }

  if (seg[1] === 'games' && seg[3]) {
    const id = seg[2]
    const rest = seg[3]
    if (rest === 'state' && method === 'GET') { const s = gameState(id); if (s) return s }
    if (rest === 'versions' && method === 'GET') {
      if (id === IDS.hades) return [versions[IDS.v8], versions[IDS.v7], versions[IDS.v6]]
      return [synthVersion(id, 2), synthVersion(id, 1)]
    }
    if (rest === 'local-size' && method === 'GET') return { bytes: id === IDS.hades ? 24_000_000 : 4_500_000 }
    if (rest === 'sync-status' && method === 'GET') {
      const conflict = id === IDS.hades && db.conflictOpen
      return { inSync: false, hasOpenConflict: conflict, conflictId: conflict ? IDS.conflict : null }
    }
    if (rest === 'files' && method === 'GET') return gameFiles(id)
    if (rest === 'sync' && method === 'POST') {
      const name = db.games.find(g => g.id === id)?.name ?? 'Game'
      syncFixture.startedAt = Date.now()
      syncFixture.game = name
      await sleep(1200)
      return { message: `Pushed ${name} — new version uploaded.` }
    }
    if (rest === 'folder' && method === 'POST') return { ok: true }
    if (rest === 'paths' && method === 'POST') return { key: body?.key ?? 'extra', path: body?.path ?? '', template: null, joined: false }
    if (rest === 'paths' && method === 'DELETE') return { ok: true }
    if (rest === 'processes' && method === 'POST') return { ok: true }
    if (rest === 'remove' && method === 'POST') {
      db.games = db.games.filter(g => g.id !== id)
      return { ok: true }
    }
    if (rest === 'open-folder' && method === 'POST') return { opened: true, path: db.games.find(g => g.id === id)?.path ?? '' }
    if (rest === 'suggested-path' && method === 'GET') return { path: null }
    if (rest === 'art' && method === 'GET') {
      const game = db.games.find(g => g.id === id)
      return blobOf(new Blob([artSvg(id, game?.name ?? 'Game')], { type: 'image/svg+xml' }))
    }
  }

  if (method === 'GET' && seg[1] === 'candidates' && seg.length === 2) return db.candidates
  if (method === 'POST' && seg[1] === 'candidates' && seg[2] === 'rescan') { counters.rescan++; return db.candidates }
  if (method === 'GET' && seg[1] === 'candidates' && seg[2] === 'cached') {
    return { suggested: db.candidates.filter(c => !c.hasSteamCloud || c.enrolled).length }
  }
  if (method === 'GET' && seg[1] === 'candidates' && seg[2] === 'links') return candidateLinks
  if (method === 'POST' && seg[1] === 'candidates' && seg[3] === 'folder-pick') return { path: null }
  if (method === 'POST' && seg[1] === 'candidates' && seg[3] === 'folder') {
    const id = Number(seg[2])
    const cand = db.candidates.find(c => c.id === id)
    if (cand) cand.path = body?.path ?? cand.path
    return { ok: true }
  }

  if (method === 'POST' && seg[1] === 'enroll') {
    const ids: number[] = body?.ids ?? []
    enrollFixture.startedAt = Date.now()
    enrollFixture.total = ids.length
    enrollFixture.names = ids.map(id => db.candidates.find(c => c.id === id)?.name ?? 'Game')
    await sleep(900)
    for (const id of ids) {
      const cand = db.candidates.find(c => c.id === id)
      if (cand) cand.enrolled = true
      if (cand && !db.games.some(g => g.name === cand.name)) {
        db.games.push({
          id: `m0000000-0000-4000-8000-${String(id).padStart(12, '0')}`,
          name: cand.name, path: cand.path, processNames: [], alias: null, steamAppId: null,
          pullBeforeLaunchEnabled: null, hasSteamCloud: cand.hasSteamCloud, pushAfterExitEnabled: true,
          lastPushBytes: null, lastPushAt: null, source: null, release: null,
        })
      }
    }
    return { enrolled: ids.length, skipped: 0, notes: null }
  }
  if (method === 'GET' && seg[1] === 'enroll' && seg[2] === 'progress') {
    const f = enrollFixture
    if (f.startedAt === null) return { active: false, index: 0, total: 0, game: null, step: '', enrolled: 0, skipped: 0 }
    const elapsed = Date.now() - f.startedAt
    const index = Math.min(f.total, Math.floor(elapsed / 500) + 1)
    const active = elapsed < f.total * 500 + 800
    if (!active) f.startedAt = null
    return {
      active, index, total: f.total, game: f.names[index - 1] ?? f.names[0] ?? null,
      step: 'Adding to the server', enrolled: Math.max(0, index - 1), skipped: 0,
    }
  }

  if (method === 'GET' && seg[1] === 'folder-suggestions') {
    const gameId = query.get('gameId')
    return gameId ? [] : folderSuggestions
  }
  if (method === 'POST' && seg[1] === 'folder-suggestions' && seg[2] === 'answer') {
    const gameId = body?.gameId
    for (let i = folderSuggestions.length - 1; i >= 0; i--) {
      if (folderSuggestions[i].gameId === gameId) folderSuggestions.splice(i, 1)
    }
    return { ok: true }
  }

  if (method === 'GET' && seg[1] === 'browse') {
    const p = query.get('path') ?? '/'
    const listing = browse(p)
    return listing ?? json(404, { error: `mock: no folder ${p}` })
  }
  if (method === 'POST' && seg[1] === 'folder-pick') return { path: null }

  if (method === 'GET' && seg[1] === 'conflicts' && seg.length === 2) return db.conflictOpen ? [openConflict] : []
  if (method === 'GET' && seg[1] === 'conflicts' && seg[2] === 'resolved') return []
  if (method === 'POST' && seg[1] === 'conflicts' && seg[3] === 'resolve') {
    const keptCloud = body?.winningVersionId === openConflict.versionAId
    db.conflictOpen = false
    return {
      message: keptCloud
        ? "Kept the cloud's save. This device's copy was kept as a backup."
        : "Kept this device's save. The cloud copy was kept as a backup.",
    }
  }

  if (method === 'GET' && seg[1] === 'versions') {
    const id = seg[2]
    if (seg[3] === 'stats') return stats[id as keyof typeof stats] ?? { fileCount: 4, newestFileWriteUtc: iso(60_000) }
    return versions[id as keyof typeof versions] ?? synthVersion(IDS.bg3, 1)
  }

  if (method === 'GET' && seg[1] === 'offline-queue') return offlineQueue
  if (method === 'POST' && seg[1] === 'open-log') return { opened: true, path: '/home/deck/.local/share/SaveLocker/agent.log' }
  if (method === 'POST' && seg[1] === 'test-connection') return { ok: true, status: 200, latencyMs: 38, error: null }
  if (method === 'POST' && seg[1] === 'config') return { identityCleared: false, startWithWindows: body?.startWithWindows ?? state.startWithWindows }
  if (method === 'POST' && seg[1] === 'register') return { machineName: MACHINE_NAME }
  if (method === 'GET' && seg[1] === 'launch-command') return { command: 'savelocker run -- %command%', note: "Set this in the game's Launch Options in Steam." }
  if (method === 'GET' && seg[1] === 'decky') return { applicable: false, deckyPresent: false, pluginInstalled: false, pluginVersion: null, installUrl: '' }
  if (seg[1] === 'playnite-plugin') {
    if (seg[2] === 'status') return { applicable: false, pluginInstalled: false, pluginVersion: null, latestVersion: null, installUrl: '' }
    if (seg[2] === 'install') return { state: 'NotApplicable', message: 'Playnite is not installed here.', installedVersion: null, latestVersion: null }
  }
  if (method === 'GET' && seg[1] === 'agent-version') {
    return { currentVersion: '0.7.0', latestVersion: null, updateAvailable: false, stagedVersion: null, stagedBlockedReason: null }
  }
  if (method === 'POST' && seg[1] === 'lease-warnings') return { ok: true }

  if (method === 'POST' && seg[1] === 'sync' && seg.length === 2) {
    if (syncFixture.startedAt !== null) return { message: 'A sync is already running.' }
    syncFixture.startedAt = Date.now()
    syncFixture.game = 'Cyberpunk 2077'
    await sleep(2600)
    return { message: 'Synced 4 games — 1 conflict needs a decision.' }
  }
  if (method === 'POST' && seg[1] === 'sync' && seg[2] === 'cancel') {
    const running = syncFixture.startedAt !== null
    syncFixture.cancelRequested = true
    return { requested: running }
  }

  return json(404, { error: `mock: no route for ${method} ${pathname}` })
}

/* ---------------------------------------------------------------- install */

export function installFetchMock() {
  const original = window.fetch?.bind(window)
  window.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, 'http://mock.local')
    if (!url.pathname.startsWith('/api/')) {
      return original ? original(input as RequestInfo, init) : json(404, { error: 'mock: not an api route' })
    }
    const method = (init?.method ?? 'GET').toUpperCase()
    let body: unknown = null
    if (typeof init?.body === 'string') {
      try { body = JSON.parse(init.body) } catch { body = null }
    }
    const result = await route(method, url.pathname, url.searchParams, body)
    if (result && typeof result === 'object' && 'ok' in (result as object) && 'json' in (result as object)) {
      return result as Response
    }
    return json(200, result) as Response
  }) as typeof window.fetch
  ;(window as { __slMock?: unknown }).__slMock = { db, syncFixture, openConflict, counters }
}
