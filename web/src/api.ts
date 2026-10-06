import type { ArtKind, ArtOptionsPage, Game, GameSummary, Machine, Command, Conflict, Settings, AppearanceSettings, SetAppearanceRequest, Version, VersionStats, ExcludesPreview, BulkEnqueueResponse, CancelCommandsResponse, MachineSavePath, MachineScanCandidate, SavePath, VersionFolder, AuditEntry, AgentInstallerStatus, InstallerHashVerification, AgentPlatform, Enrollment, CreateEnrollmentResponse, EffectiveServerUrl, AgentHealth, AdminStatus, AutoFetchSchedule, BackupStatus, BackupResult, BackupRestoreResult, SetBackupSettingsRequest, BackupDownloadTicket } from './types';

// The console holds a revocable SESSION TOKEN, never the admin password. It used to keep the password
// itself in localStorage and send it on every request, so anything able to read that storage — an XSS,
// a hostile extension — took the real credential, which never expires and may be reused elsewhere. A
// session is a random token the server can end (Lock, "Sign out everywhere", a password change) and
// that expires on its own; only its hash is stored server-side. localStorage still holds it, so it
// survives a reload — that is the same exposure window, but now for something that can be killed.
const SESSION_KEY = 'sl_session';
const LEGACY_PASSWORD_KEY = 'sl_password';

// Web storage can throw (private windows, blocked site data). A console that cannot persist a session
// still works — it just asks again after a reload.
function readStore(key: string, store: 'local' | 'session' = 'local'): string {
  try { return (store === 'local' ? localStorage : sessionStorage).getItem(key) ?? ''; } catch { return ''; }
}
function writeStore(key: string, value: string | null, store: 'local' | 'session' = 'local') {
  try {
    const s = store === 'local' ? localStorage : sessionStorage;
    if (value) s.setItem(key, value); else s.removeItem(key);
  } catch { /* see above */ }
}

// plan.md 12.4 "Remember this browser": remembered → localStorage (outlives the browser, until the
// server's own 7-day idle / 30-day limit); not → sessionStorage, which is per TAB: gone when the tab closes,
// and a new tab asks again.
let sessionToken = readStore(SESSION_KEY, 'session') || readStore(SESSION_KEY);
let remembered = sessionToken !== '' && readStore(SESSION_KEY, 'session') === '';

function keepSession(token: string, remember: boolean) {
  sessionToken = token;
  remembered = remember;
  writeStore(SESSION_KEY, remember ? token : null);
  writeStore(SESSION_KEY, remember ? null : token, 'session');
}

export function hasSession() { return sessionToken !== ''; }
export function clearSession() { sessionToken = ''; writeStore(SESSION_KEY, null); writeStore(SESSION_KEY, null, 'session'); }

/** A failed request. `status` lets a caller react to a refusal by kind instead of matching message text. */
export class ApiError extends Error {
  readonly status: number;
  /** The server's own explanation, unwrapped from whatever shape it sent — for showing to a person. */
  readonly detail: string;
  constructor(status: number, message: string, detail = '') {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.detail = detail;
  }
}

/** The text worth showing for a caught failure: the server's explanation when there is one. */
export function errorText(e: unknown): string {
  if (e instanceof ApiError) return e.detail || e.message;
  return e instanceof Error ? e.message : String(e);
}

/** A server refusal body as plain text: a JSON string, `{error}`, or problem-details, else the raw text. */
function plainDetail(body: string): string {
  if (!body) return '';
  try {
    const parsed = JSON.parse(body);
    if (typeof parsed === 'string') return parsed;
    if (typeof parsed?.error === 'string') return parsed.error;
    if (typeof parsed?.message === 'string') return parsed.message;
    if (typeof parsed?.detail === 'string') return parsed.detail;
    if (typeof parsed?.title === 'string') return parsed.title;
  } catch { /* not JSON — fall through */ }
  return body;
}

export type SignInResult =
  | { ok: true }
  | { ok: false; reason: 'wrong' | 'throttled' | 'error'; message: string };

/**
 * Exchange the admin password for a session. The password is sent once, here, and never stored.
 * `remember` picks where the token lives; it defaults to where the current one does, so re-signing in
 * after a password change keeps the choice made at sign-in. On a
 * server that has no admin password the answer carries no token and this simply reports success —
 * there is nothing to sign in to.
 */
export async function signIn(password: string, remember = remembered): Promise<SignInResult> {
  let res: Response;
  try {
    res = await fetch('/api/admin/session', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password }),
    });
  } catch { return { ok: false, reason: 'error', message: "Couldn't reach the server." }; }

  if (res.ok) {
    const body = await res.json().catch(() => null) as { token?: string | null } | null;
    if (body?.token) keepSession(body.token, remember);
    else clearSession();
    return { ok: true };
  }
  // 429's message comes from the server and says how long to wait ("…Try again in 14 minutes.").
  const detail = await explain(res);
  if (res.status === 401) return { ok: false, reason: 'wrong', message: 'That password didn’t work. It is the one set on the server, not your Steam or system login.' };
  if (res.status === 429) return { ok: false, reason: 'throttled', message: detail };
  return { ok: false, reason: 'error', message: detail };
}

/** Lock: end this browser's session on the server (best effort) and forget it locally. */
export async function signOut(): Promise<void> {
  if (sessionToken) {
    try { await fetch('/api/admin/session', { method: 'DELETE', headers: headers() }); } catch { /* the token is dropped below either way */ }
  }
  clearSession();
}

/**
 * Consoles from before sessions kept the admin PASSWORD in localStorage. Swap it for a session once and
 * delete it — whatever the answer, unless the server could not be asked (a transient failure keeps it
 * for the next load, rather than costing the user their sign-in over a network blip).
 */
export async function migrateLegacyPassword(): Promise<'none' | 'migrated' | 'rejected' | 'pending'> {
  const legacy = readStore(LEGACY_PASSWORD_KEY);
  if (!legacy) return 'none';
  const r = await signIn(legacy);
  if (r.ok) { writeStore(LEGACY_PASSWORD_KEY, null); return 'migrated'; }
  if (r.reason === 'wrong') { writeStore(LEGACY_PASSWORD_KEY, null); return 'rejected'; }
  return 'pending';
}

/** Remove a leftover plaintext password without using it (the server turned out to need none). */
export function dropLegacyPassword() { writeStore(LEGACY_PASSWORD_KEY, null); }

function headers(extra: Record<string, string> = {}): Record<string, string> {
  return sessionToken ? { 'X-Admin-Session': sessionToken, ...extra } : { ...extra };
}

/**
 * The server explains a refusal in one of two shapes and the caller cannot know which: minimal-API
 * `BadRequest(string)` sends a JSON-encoded string, `Problem(...)` sends problem details with a
 * `detail` field. Reading only one of them turned half the server's reasons into a bare
 * "400 Bad Request" — which, for the installer routes, is exactly the wrong-file-for-this-platform
 * message the admin needs to see.
 */
async function explain(res: Response): Promise<string> {
  const body = await res.text().catch(() => '');
  if (body) {
    try {
      const parsed = JSON.parse(body);
      if (typeof parsed === 'string' && parsed) return parsed;
      if (typeof parsed?.error === 'string' && parsed.error) return parsed.error;
      if (parsed?.detail) return parsed.detail as string;
      if (parsed?.title) return parsed.title as string;
    } catch { return body; }
  }
  return `${res.status} ${res.statusText}`;
}

/** `&path=<key>` for one of a game's extra save folders; nothing for the main one. */
function pathParam(key: string | undefined): string {
  return key && key !== 'main' ? `&path=${encodeURIComponent(key)}` : '';
}

async function request<T>(path: string, opts: RequestInit = {}): Promise<T> {
  const res = await fetch('/api' + path, {
    ...opts,
    headers: { ...headers(), ...(opts.headers as Record<string, string> || {}) },
  });
  if (!res.ok) {
    const detail = await res.text();
    throw new ApiError(res.status, `${res.status} ${res.statusText}${detail ? `: ${detail}` : ''}`, plainDetail(detail));
  }
  const ct = res.headers.get('content-type') || '';
  return ct.includes('json') ? res.json() : res.text() as unknown as T;
}

export const api = {
  adminStatus: () => fetch('/api/admin/status').then(r => r.json()) as Promise<AdminStatus>,
  overview: () => request<GameSummary[]>('/overview'),
  conflicts: () => request<Conflict[]>('/conflicts'),
  machines: () => request<Machine[]>('/machines'),
  commands: () => request<Command[]>('/commands'),
  settings: () => request<Settings>('/settings'),
  versions: (gameId: string) => request<Version[]>(`/games/${gameId}/versions`),
  /** File count / newest-mtime for one version, read from its archive on demand. Used to help tell
   * apart the two sides of an open conflict — deliberately not part of the versions list above, so
   * listing versions never has to open a zip for ones nobody is looking at. */
  versionStats: (gameId: string, versionId: string) =>
    request<VersionStats>(`/games/${gameId}/versions/${versionId}/stats`),
  /** One version's files grouped by save folder (`main` first), read from its archive on demand. */
  versionFolders: (gameId: string, versionId: string) =>
    request<VersionFolder[]>(`/games/${gameId}/versions/${versionId}/folders`),

  refreshArt: (gameId: string) => request<{ message?: string }>(`/games/${gameId}/art/refresh`, { method: 'POST' }),
  /** Five SteamGridDB covers or icons for a game, with inline previews. `page` counts from 0. */
  artOptions: (gameId: string, kind: ArtKind, page: number) =>
    request<ArtOptionsPage>(`/games/${gameId}/art/options?kind=${kind}&page=${page}`),
  /** Use one of those options (by its `url`) as the game's cover or icon. */
  setArt: (gameId: string, kind: ArtKind, url: string) =>
    request<Game>(`/games/${gameId}/art/${kind}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ url }) }),
  setEnabled: (gameId: string, value: boolean) => request<void>(`/games/${gameId}/enabled?value=${value}`, { method: 'POST' }),
  deleteGame: (gameId: string) => request<void>(`/games/${gameId}`, { method: 'DELETE' }),
  addGame: (name: string, suggestedSaveDir: string | null) =>
    request<void>('/games', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, manifestKey: null, customPathsJson: null, suggestedSaveDir }) }),
  /** A save folder's template for every machine; `key` names an extra folder (none: the main one). */
  setSaveDir: (gameId: string, value: string, key?: string) =>
    request<void>(`/games/${gameId}/save-dir?value=${encodeURIComponent(value)}${pathParam(key)}`, { method: 'POST' }),
  /** Another save folder for the game, on every machine that syncs it (tasks/multiple-save-paths). */
  addSavePath: (gameId: string, body: { key: string; label?: string | null; template?: string | null; includeGlobs?: string[] | null }) =>
    request<SavePath>(`/games/${gameId}/save-paths`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label: null, template: null, includeGlobs: null, ...body }),
    }),
  /** Stop syncing an extra folder everywhere. Its key is retired; stored versions keep its files. */
  removeSavePath: (gameId: string, key: string) =>
    request<void>(`/games/${gameId}/save-paths/${encodeURIComponent(key)}`, { method: 'DELETE' }),
  /** One save folder's include scope. Empty: the whole folder. */
  setIncludeGlobs: (gameId: string, patterns: string[], key?: string) =>
    request<void>(`/games/${gameId}/include-globs${key ? `?path=${encodeURIComponent(key)}` : ''}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(patterns),
    }),
  setRetention: (gameId: string, value: number | null) =>
    request<void>(`/games/${gameId}/retain${value !== null ? `?value=${value}` : ''}`, { method: 'POST' }),
  setExcludes: (gameId: string, patterns: string[]) =>
    request<void>(`/games/${gameId}/excludes`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(patterns) }),
  /** Dry run against the game's head archive, for a draft pattern list that hasn't been saved yet. */
  previewExcludes: (gameId: string, patterns: string[]) =>
    request<ExcludesPreview>(`/games/${gameId}/excludes/preview`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(patterns) }),
  setConflictPolicy: (gameId: string, policy: string, preferredMachineId?: string | null) =>
    request<void>(`/games/${gameId}/conflict-policy`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ policy, preferredMachineId: preferredMachineId ?? null }) }),
  deleteVersion: (gameId: string, versionId: string) =>
    request<void>(`/games/${gameId}/versions/${versionId}`, { method: 'DELETE' }),
  setVersionProtected: (gameId: string, versionId: string, value: boolean) =>
    request<void>(`/games/${gameId}/versions/${versionId}/protected?value=${value}`, { method: 'POST' }),

  /** Apply the game's retention limit now, rather than waiting for the next upload to trigger it. */
  pruneNow: (gameId: string) =>
    request<{ removed: number }>(`/games/${gameId}/prune`, { method: 'POST' }),

  /**
   * Download one version's archive.
   *
   * Deliberately not an `<a href>`: the admin password travels as a header and a plain link cannot
   * carry one, so this fetches the blob and hands the browser a save prompt. Being able to take a
   * copy before doing something destructive is what makes the destructive actions safe to offer.
   */
  downloadVersion: async (gameId: string, versionId: string, filename: string) => {
    const res = await fetch(`/api/games/${gameId}/versions/${versionId}/download`, { headers: headers() });
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  },
  setLatest: (gameId: string, versionId: string) => request<void>(`/games/${gameId}/set-latest?version=${versionId}`, { method: 'POST' }),
  forceRelease: (gameId: string) => request<void>(`/games/${gameId}/lease/force`, { method: 'DELETE' }),
  resolveConflict: (conflictId: string, versionId: string, keepBoth = false) =>
    request<void>(
      `/conflicts/${conflictId}/resolve?version=${versionId}&keepBoth=${keepBoth}`,
      { method: 'POST' },
    ),

  queueCommand: (machineId: string, gameId: string, type: string, force: boolean) =>
    request<void>('/commands', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ machineId, gameId, type, force }) }),

  /**
   * Console "Sync all": one call, one command per machine. `gameId: null` on each — the agent's
   * own poller already syncs every game IT tracks when a command names no specific game
   * (`CommandPoller.TargetGames`), so this needs no per-game fan-out at all.
   */
  queueSyncAll: (machineIds: string[], skipUnseenForSeconds: number) =>
    request<BulkEnqueueResponse>('/commands/bulk', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        commands: machineIds.map(machineId => ({ machineId, gameId: null, type: 'Sync', force: false })),
        // Commands never expire, so one queued for a machine that is switched off would fire
        // unannounced whenever it next connects. The server leaves such machines out and names them.
        skipMachinesUnseenForSeconds: skipUnseenForSeconds,
      }),
    }),

  /**
   * Console "Cancel" beside a running Sync all. Withdraws the commands no agent has claimed yet (they
   * become `Cancelled`); a claimed one cannot be recalled and comes back under `alreadyRunning`.
   */
  cancelCommands: (ids: string[]) =>
    request<CancelCommandsResponse>('/commands/cancel', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids }),
    }),

  /** "Sign out everywhere": ends every session on the server, this browser's included. */
  signOutEverywhere: () => request<{ ended: number }>('/admin/sessions', { method: 'DELETE' }),

  deleteMachine: (machineId: string) =>
    fetch(`/api/machines/${machineId}`, { method: 'DELETE', headers: headers() })
      .then(res => { if (!res.ok) throw new Error(`${res.status}`); }),

  // Not `request()`: a rejected key answers 4xx with a structured body, and the useful part is the
  // server's explanation ("SteamGridDB rejected the key", "could not reach SteamGridDB") rather
  // than a status line with raw JSON glued to it.
  saveSgdbKey: async (key: string | null) => {
    const res = await fetch('/api/settings/steamgriddb-key', {
      method: 'POST',
      headers: headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ apiKey: key }),
    });
    const body = await res.json().catch(() => null) as { ok?: boolean; message?: string } | null;
    if (!res.ok || body?.ok === false) {
      throw new Error(body?.message || `${res.status} ${res.statusText}`);
    }
    return body ?? {};
  },

  setAppearance: (req: SetAppearanceRequest) =>
    request<AppearanceSettings>('/settings/appearance', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
    }),

  setAutoFetchSchedule: (schedule: AutoFetchSchedule) =>
    request<{ schedule: AutoFetchSchedule; nextRunAt: string | null }>('/settings/agent-update-auto-fetch', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(schedule),
    }),

  getGamePaths: (gameId: string) =>
    request<MachineSavePath[]>(`/games/${gameId}/paths`),
  getGamePathCandidates: (gameId: string) =>
    request<MachineScanCandidate[]>(`/games/${gameId}/path-candidates`),
  setMachinePath: (gameId: string, machineId: string, path: string, key?: string) =>
    request<void>(`/games/${gameId}/paths/${machineId}?value=${encodeURIComponent(path)}${pathParam(key)}`, { method: 'POST' }),
  clearMachinePath: (gameId: string, machineId: string, key?: string) =>
    fetch(`/api/games/${gameId}/paths/${machineId}${key ? `?path=${encodeURIComponent(key)}` : ''}`, { method: 'DELETE', headers: headers() })
      .then(res => { if (!res.ok) throw new Error(`${res.status}`); }),

  audit: (limit = 200) => request<AuditEntry[]>(`/audit?limit=${limit}`),

  /** The exclude patterns every game inherits; validated like a game's own list (400 with the reason). */
  setDefaultExcludes: (patterns: string[]) =>
    request<string[]>('/settings/default-excludes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(patterns) }),
  backupStatus: () => request<BackupStatus>('/admin/backups/status'),
  backupNow: () => request<BackupResult>('/admin/backup', { method: 'POST' }),
  /** Replace the server's database with a backup's (a safety backup is taken first). */
  deleteBackup: (fileName: string) =>
    request<void>(`/admin/backups/${encodeURIComponent(fileName)}`, { method: 'DELETE' }),
  restoreBackup: (fileName: string) =>
    request<BackupRestoreResult>(`/admin/backups/${encodeURIComponent(fileName)}/restore`, { method: 'POST' }),
  setBackupSettings: (body: SetBackupSettingsRequest) =>
    request<void>('/admin/backups/settings', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
  /**
   * A backup can be gigabytes, so it is never fetched into a blob in memory: the session header buys a
   * single-use ticket (about a minute, this one file, audited), and a plain link carrying it lets the browser
   * stream the file to disk. Never the session itself in the URL. `fileName` comes from the listing; the
   * server matches it against its own listing again.
   */
  downloadBackup: async (fileName: string) => {
    const t = await request<BackupDownloadTicket>(`/admin/backups/${encodeURIComponent(fileName)}/download-ticket`, { method: 'POST' });
    const a = document.createElement('a');
    a.href = t.url;
    a.download = fileName;
    a.click();
  },

  setAdminPassword: (password: string | null) =>
    request<{ ok: boolean; message: string }>('/admin/password', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password }),
    }),

  // Every machine's health, including ones that have never sent a heartbeat — an agent that was
  // enrolled and never actually ran is exactly the case worth seeing.
  health: () => request<AgentHealth[]>('/admin/health'),

  // Dismiss does not fix the condition; if it is still true the agent's next report reopens it.
  dismissEvent: (id: string) =>
    fetch(`/api/admin/health/events/${id}/dismiss`, { method: 'POST', headers: headers() })
      .then(res => { if (!res.ok) throw new Error(`${res.status}`); }),

  enrollments: () => request<Enrollment[]>('/admin/enrollments'),

  // What the policy file WOULD say, without minting. Shown before the button is pressed, because a
  // token is single-use: finding out the URL was wrong afterwards costs the token too.
  effectiveServerUrl: () => request<EffectiveServerUrl>('/admin/enrollments/effective-url'),

  // The raw token comes back exactly once, inside the policy — the server keeps only its hash.
  // Whatever the caller does with this response is the only chance to hand it to the user.
  createEnrollment: (body: {
    machineName: string | null;
    ttlMinutes: number;
    serverUrl: string | null;
    gameIds: string[] | null;
  }) =>
    request<CreateEnrollmentResponse>('/admin/enrollments', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  revokeEnrollment: (id: string) =>
    fetch(`/api/admin/enrollments/${id}`, { method: 'DELETE', headers: headers() })
      .then(res => { if (!res.ok) throw new Error(`${res.status}`); }),

  installerStatus: (platform: AgentPlatform): Promise<AgentInstallerStatus | null> =>
    fetch(`/api/admin/agent-installer?platform=${platform}`, { headers: headers() }).then(async res => {
      if (res.status === 204) return null;
      if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
      return res.json() as Promise<AgentInstallerStatus>;
    }),

  uploadInstaller: (formData: FormData, version: string, platform: AgentPlatform): Promise<AgentInstallerStatus> =>
    fetch(`/api/admin/agent-installer?version=${encodeURIComponent(version)}&platform=${platform}`, {
      method: 'POST',
      headers: headers(), // no Content-Type — let browser set multipart boundary
      body: formData,
    }).then(async res => {
      if (!res.ok) throw new Error(await explain(res));
      return res.json() as Promise<AgentInstallerStatus>;
    }),

  deleteInstaller: (platform: AgentPlatform): Promise<void> =>
    fetch(`/api/admin/agent-installer?platform=${platform}`, { method: 'DELETE', headers: headers() })
      .then(res => { if (!res.ok) throw new Error(`${res.status}`); }),

  fetchInstallerFromGitHub: (platform: AgentPlatform): Promise<AgentInstallerStatus> =>
    fetch(`/api/admin/agent-installer/fetch-github?platform=${platform}`, { method: 'POST', headers: headers() })
      .then(async res => {
        if (!res.ok) throw new Error(await explain(res));
        return res.json() as Promise<AgentInstallerStatus>;
      }),

  verifyInstallerHash: (platform: AgentPlatform): Promise<InstallerHashVerification> =>
    fetch(`/api/admin/agent-installer/verify?platform=${platform}`, { headers: headers() })
      .then(async res => {
        if (!res.ok) throw new Error(await explain(res));
        return res.json() as Promise<InstallerHashVerification>;
      }),
};
