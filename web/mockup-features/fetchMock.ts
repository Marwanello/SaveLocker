import {
  DAY, HOUR, MIN, MACHINE_NAMES, backupStatus, changes, folders, games, health, iso, machines, paths, settings, sources, summaries, versions,
} from './fixtures'

/** The console's mock backend: every route the console reads, from fixtures; writes answer OK. */

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms))
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

async function route(method: string, path: string, q: URLSearchParams): Promise<Response> {
  const seg = path.split('/').filter(Boolean).slice(1) // after 'api'
  const [a, b, c, d, e] = seg

  if (a === 'admin' && b === 'status') return json({ passwordRequired: true, build: { version: '0.9.0', commit: 'f1c2e3d', builtAt: iso(3 * DAY), isRelease: true, latestRelease: '0.9.0' } })
  if (method !== 'GET') { await sleep(250); return json({}) }

  if (a === 'overview') return json(summaries())
  if (a === 'conflicts') return json([])
  if (a === 'machines') return json(machines)
  if (a === 'commands') return json([])
  if (a === 'settings' && !b) return json(settings)
  if (a === 'audit') return json([
    { id: '1', timestamp: iso(9 * MIN), machineId: null, machineName: 'Steam Deck', gameId: null, gameName: 'Elden Ring', action: 'version.upload', detail: '2.83 MB' },
    { id: '2', timestamp: iso(2 * HOUR), machineId: null, machineName: null, gameId: null, gameName: 'Hollow Knight', action: 'game.retention', detail: '10' },
  ])
  if (a === 'transfers') return json({ maxBytes: 2_000_000_000, expiryDays: 7, bytesInTransit: 0, open: [] })

  if (a === 'admin') {
    if (b === 'health') return json(health)
    if (b === 'lockouts') return json([])
    if (b === 'sessions') return json([{ id: 's1', createdAt: iso(2 * DAY), lastUsedAt: iso(MIN), expiresAt: iso(-5 * DAY), clientAddress: '192.168.1.24', isCurrent: true }])
    if (b === 'enrollments' && c === 'effective-url') return json({ url: 'http://savelocker.home.lan:5080', fromConfig: false, isLoopback: false })
    if (b === 'enrollments') return json([])
    if (b === 'backups' && c === 'status') return json(backupStatus)
    if (b === 'agent-installer' && c === 'verify') return json({ ok: true })
    if (b === 'agent-installer') return json({ version: '0.9.0', fileName: `savelocker-${q.get('platform')}.bin`, uploadedAt: iso(3 * DAY), sizeBytes: 48_000_000, sha256: null, platform: q.get('platform') ?? 'win-x64', source: 'github' })
  }

  if (a === 'games' && b) {
    const list = versions[b] ?? []
    if (c === 'versions' && !d) return json(list)
    if (c === 'versions' && d === 'changes') return json(changes(b))
    if (c === 'versions' && e === 'folders') return json(folders(b, Math.max(0, list.findIndex(v => v.id === d))))
    if (c === 'versions' && e === 'stats') return json({ fileCount: folders(b, 0)[0].fileCount, newestFileWriteUtc: iso(HOUR) })
    if (c === 'paths') return json(paths(b))
    if (c === 'sources') return json(sources(b))
    if (c === 'path-candidates') return json([])
    if (c === 'content') return json({ headVersionId: list[0]?.id ?? null, headMachineName: list[0]?.machineName ?? null, headContent: [], machines: [] })
    if (c === 'transfers') return json([])
    if (c === 'art' && d === 'source') return json({ sgdbId: null, sgdbName: null, auto: true })
  }
  console.warn(`mock: no route for ${method} ${path}`)
  return json({ error: `mock: no route for ${method} ${path}` }, 404)
}

export function installFetchMock() {
  const original = window.fetch.bind(window)
  window.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, location.href)
    if (!url.pathname.startsWith('/api/')) return original(input as RequestInfo, init)
    return route((init?.method ?? 'GET').toUpperCase(), url.pathname, url.searchParams)
  }) as typeof window.fetch
}

export { games, MACHINE_NAMES, DAY }
