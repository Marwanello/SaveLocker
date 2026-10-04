import type { Activity, AgentAppearance, AgentState, AgentVersion, BrowseListing, Candidate, Conflict, DeckyStatus, EnrollProgress, GameState, GameSyncMode, OfflineQueueEntry, OpenPathResult, PlaynitePluginCardStatus, PlaynitePluginStatus, SaveVersion, SyncStatus, TestConnection, TrackedGame, VersionStats } from './types'

// The agent injects the local API token into index.html when it serves the page; the same-origin
// policy is what keeps any other page from reading it. Left as the literal placeholder under
// `vite dev`, where the proxy supplies the header instead.
const TOKEN = document
  .querySelector<HTMLMetaElement>('meta[name="savelocker-token"]')
  ?.content ?? ''

function authHeaders(extra?: HeadersInit): HeadersInit | undefined {
  if (!TOKEN || TOKEN.startsWith('__')) return extra
  return { ...(extra as Record<string, string> | undefined), 'X-SaveLocker-Token': TOKEN }
}

/** A refused request. `needsConfirm` is the agent saying "a heuristic flagged this, and the same request
 *  with `confirm` will be accepted" — read from its own field, never inferred from the message, so a hard
 *  refusal can never be offered as something to click past. */
export class ApiError extends Error {
  readonly needsConfirm: boolean
  constructor(message: string, needsConfirm = false) {
    super(message)
    this.name = 'ApiError'
    this.needsConfirm = needsConfirm
  }
}

/** The sentence the agent ends a confirmable refusal with (for clients older than `needsConfirm`). The
 *  page asks the question in its own words, so it is dropped from what is shown. */
export const CONFIRM_HINT = ' Re-send with confirm to use it anyway.'

async function req<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(path, { ...options, headers: authHeaders(options?.headers) })
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText })) as { error?: string; needsConfirm?: boolean }
    throw new ApiError(err.error ?? res.statusText, err.needsConfirm === true)
  }
  return res.json() as Promise<T>
}

function post<T = unknown>(path: string, body?: object): Promise<T> {
  return req<T>(path, {
    method: 'POST',
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
}

/** A POST whose 409 is an answer, not a failure: "there is no desktop to show that on" carries the path
 *  the page should show instead, in the same shape as the 200. Anything else still throws. */
async function postShow(path: string): Promise<OpenPathResult> {
  const res = await fetch(path, { method: 'POST', headers: authHeaders() })
  if (res.ok || res.status === 409) return res.json() as Promise<OpenPathResult>
  const err = await res.json().catch(() => ({ error: res.statusText })) as { error?: string }
  throw new Error(err.error ?? res.statusText)
}

export const api = {
  state: () => req<AgentState>('/api/state'),
  candidates: () => req<Candidate[]>('/api/candidates'),
  rescan: () => post<Candidate[]>('/api/candidates/rescan'),
  enroll: (ids: number[]) => post<{ enrolled: number; skipped: number }>('/api/enroll', { ids }),
  // Asked while enroll() is still open: which game and which step the agent is on.
  enrollProgress: () => req<EnrollProgress>('/api/enroll/progress'),
  // identityCleared is true when the server URL moved to a different origin: the machine key, id
  // and TLS pin were issued by the old server and have been dropped, so this agent must register
  // or enroll again before it can sync.
  saveConfig: (body: {
    serverUrl?: string
    machineName?: string
    startWithWindows?: boolean
    settleQuietSeconds?: number
    // Whether the Linux agent may stage a newer version by itself.
    autoUpdate?: boolean
    // startWithWindows is the EFFECTIVE state read back from the platform, not what was asked for.
    // A refusal comes back as a failed request; this covers the quieter case where the entry was
    // written and then reverted underneath us.
  }) => post<{ identityCleared: boolean; startWithWindows: boolean }>('/api/config', body),
  register: (adminPassword?: string) =>
    post<{ machineName: string }>('/api/register', { adminPassword }),
  games: () => req<TrackedGame[]>('/api/games'),
  removeGame: (id: string) => post(`/api/games/${id}/remove`),
  // `confirm` accepts a folder the sanity heuristics flagged (a suspected Wine prefix, an oversized
  // folder). It never overrides the hard refusals — a drive root or a user profile is refused with
  // or without it.
  setGameFolder: (id: string, path: string, confirm = false) =>
    post(`/api/games/${id}/folder`, { path, confirm }),
  // Process names that mean the game is running. Empty means the Windows agent cannot detect it at
  // all — no lease, no exit push, no refusal to pull under a live game.
  setGameProcesses: (id: string, processNames: string[]) =>
    post(`/api/games/${id}/processes`, { processNames }),
  browse: (path?: string) =>
    req<BrowseListing>('/api/browse' + (path ? `?path=${encodeURIComponent(path)}` : '')),
  suggestedPath: (id: string) => req<{ path: string | null }>(`/api/games/${id}/suggested-path`),
  folderPick: () => post<{ path: string | null }>('/api/folder-pick'),
  candidateFolderPick: (id: number) => post<{ path: string | null }>(`/api/candidates/${id}/folder-pick`),
  candidateFolder: (id: number, path: string) => post(`/api/candidates/${id}/folder`, { path }),
  dismissLeaseWarning: (gameName: string) => post('/api/lease-warnings/dismiss', { gameName }),
  launchCommand: () => req<{ command: string | null; note: string | null }>('/api/launch-command'),
  decky: () => req<DeckyStatus>('/api/decky'),
  playnitePluginCardStatus: () => req<PlaynitePluginCardStatus>('/api/playnite-plugin/status'),
  installPlaynitePlugin: () => post<PlaynitePluginStatus>('/api/playnite-plugin/install'),
  // What the agent's last check found. The agent decides this, not the UI: it is the host that
  // knows which platform's package the server offered and whether the version is actually newer.
  agentVersion: () => req<AgentVersion>('/api/agent-version'),
  // The look this window draws itself in: the console's, pushed on a heartbeat, or this machine's own.
  appearance: () => req<AgentAppearance>('/api/appearance'),
  setAppearance: (body: { follow: boolean; look?: { theme: string; accent: string; mark: string } }) =>
    post<AgentAppearance>('/api/appearance', body),
  // What is syncing right now (with byte progress for a push) plus a short rolling history.
  // Cheap — an in-memory read on the agent's side — so this can be polled far more often than state.
  activity: () => req<Activity>('/api/activity'),
  // Pull then push every tracked game, same as the tray menu's "Sync All". The response is a
  // one-line summary; progress for whichever game is mid-sync shows up on the next activity() poll.
  syncNow: () => post<{ message: string }>('/api/sync'),
  // Stops a running Sync all AFTER the game it is on — that game always finishes, so a cancel can
  // never leave a save folder half-replaced. `requested` is false when nothing was running.
  cancelSync: () => post<{ requested: boolean }>('/api/sync/cancel'),
  // What is waiting for the server to come back (pushes that could not be sent).
  offlineQueue: () => req<OfflineQueueEntry[]>('/api/offline-queue'),
  // Show agent.log / a game's save folder on this machine's desktop. `opened: false` means there was
  // none (a headless box) — show `path` with a Copy button instead.
  openLog: () => postShow('/api/open-log'),
  openFolder: (id: string) => postShow(`/api/games/${id}/open-folder`),
  testConnection: () => post<TestConnection>('/api/test-connection'),
  // How many games the LAST scan suggested; null before any scan. Never scans by itself.
  cachedCandidates: () => req<{ suggested: number | null }>('/api/candidates/cached'),
  // Every version the server keeps of one game, newest first.
  gameVersions: (id: string) => req<SaveVersion[]>(`/api/games/${id}/versions`),
  resolvedConflicts: () => req<Conflict[]>('/api/conflicts/resolved'),
  // How big a game's save folder is now — a plain directory walk, so fine to ask on opening the page.
  localSize: (id: string) => req<{ bytes: number }>(`/api/games/${id}/local-size`),
  // Conflict resolution (tasks/conflict-resolution-ui/plan.md, Phase 6) — every open conflict on the
  // server this machine's key can see, not only this machine's own. Resolution itself already lives
  // in Agent.Core (Phase 0/1); this just gives it a UI both hosts can reach.
  conflicts: () => req<Conflict[]>('/api/conflicts'),
  resolveConflict: (id: string, winningVersionId: string, keepBoth: boolean) =>
    post(`/api/conflicts/${id}/resolve`, { winningVersionId, keepBoth }),
  // Machine name, timestamp and size for one side of a conflict — a conflict only carries version
  // ids. Cached by the caller: an archive's stats never change once uploaded.
  version: (id: string) => req<SaveVersion>(`/api/versions/${id}`),
  versionStats: (id: string) => req<VersionStats>(`/api/versions/${id}/stats`),
  // One game's manual sync (never forced): the agent answers with a one-line result, or a 409 when
  // that same game is already mid-sync. It keeps going if this page goes away, so the result also
  // shows on /api/activity.
  syncGame: (id: string, mode: GameSyncMode) => post<{ message: string }>(`/api/games/${id}/sync`, { mode }),
  // What the server holds for one game: head, lease, open conflict. Cheap; no disk work.
  gameState: (id: string) => req<GameState>(`/api/games/${id}/state`),
  // Hashes the whole save folder. Only ever on an explicit "Check now", never on a timer or a list.
  syncStatus: (id: string) => req<SyncStatus>(`/api/games/${id}/sync-status`),
  // Cover or icon as a Blob: an <img src> cannot carry the local token, so the UI fetches it here.
  // Aborting `signal` reaches the agent, which stops asking the server for it.
  art: async (id: string, kind: 'grid' | 'icon', w: number, signal?: AbortSignal): Promise<Blob | null> => {
    const res = await fetch(`/api/games/${id}/art?kind=${kind}&w=${w}`, { headers: authHeaders(), signal })
    return res.ok ? res.blob() : null
  },
}
