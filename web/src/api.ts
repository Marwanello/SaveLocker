import type { GameSummary, Machine, Command, Conflict, Settings, Version, VersionStats, ExcludesPreview, BulkEnqueueResponse, MachineSavePath, MachineScanCandidate, AuditEntry, AgentInstallerStatus, InstallerHashVerification, AgentPlatform, Enrollment, CreateEnrollmentResponse, EffectiveServerUrl, AgentHealth, AdminStatus, AutoFetchSchedule } from './types';

// The console holds a revocable SESSION TOKEN, never the admin password. It used to keep the password
// itself in localStorage and send it on every request, so anything able to read that storage — an XSS,
// a hostile extension — took the real credential, which never expires and may be reused elsewhere. A
// session is a random token the server can end (Lock, "Sign out everywhere", a password change) and
// that expires on its own; only its hash is stored server-side. localStorage still holds it, so it
// survives a reload — that is the same exposure window, but now for something that can be killed.
const SESSION_KEY = 'sl_session';
const LEGACY_PASSWORD_KEY = 'sl_password';

// localStorage can throw (private windows, blocked site data). A console that cannot persist a
// session still works — it just asks again after a reload.
function readStore(key: string): string { try { return localStorage.getItem(key) ?? ''; } catch { return ''; } }
function writeStore(key: string, value: string | null) {
  try { if (value) localStorage.setItem(key, value); else localStorage.removeItem(key); } catch { /* see above */ }
}

let sessionToken = readStore(SESSION_KEY);

export function hasSession() { return sessionToken !== ''; }
export function clearSession() { sessionToken = ''; writeStore(SESSION_KEY, null); }

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
    if (typeof parsed?.detail === 'string') return parsed.detail;
    if (typeof parsed?.title === 'string') return parsed.title;
  } catch { /* not JSON — fall through */ }
  return body;
}

export type SignInResult =
  | { ok: true }
  | { ok: false; reason: 'wrong' | 'throttled' | 'error'; message: string };

/**
 * Exchange the admin password for a session. The password is sent once, here, and never stored. On a
 * server that has no admin password the answer carries no token and this simply reports success —
 * there is nothing to sign in to.
 */
export async function signIn(password: string): Promise<SignInResult> {
  let res: Response;
  try {
    res = await fetch('/api/admin/session', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password }),
    });
  } catch { return { ok: false, reason: 'error', message: "Couldn't reach the server." }; }

  if (res.ok) {
    const body = await res.json().catch(() => null) as { token?: string | null } | null;
    if (body?.token) { sessionToken = body.token; writeStore(SESSION_KEY, body.token); }
    else clearSession();
    return { ok: true };
  }
  // 429's message comes from the server and says how long to wait ("…Try again in 14 minutes.").
  const detail = await explain(res);
  if (res.status === 401) return { ok: false, reason: 'wrong', message: 'Wrong password. Try again.' };
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

  refreshArt: (gameId: string) => request<{ message?: string }>(`/games/${gameId}/art/refresh`, { method: 'POST' }),
  setEnabled: (gameId: string, value: boolean) => request<void>(`/games/${gameId}/enabled?value=${value}`, { method: 'POST' }),
  deleteGame: (gameId: string) => request<void>(`/games/${gameId}`, { method: 'DELETE' }),
  addGame: (name: string, suggestedSaveDir: string | null) =>
    request<void>('/games', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, manifestKey: null, customPathsJson: null, suggestedSaveDir }) }),
  setSaveDir: (gameId: string, value: string) => request<void>(`/games/${gameId}/save-dir?value=${encodeURIComponent(value)}`, { method: 'POST' }),
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
  setMachinePath: (gameId: string, machineId: string, path: string) =>
    request<void>(`/games/${gameId}/paths/${machineId}?value=${encodeURIComponent(path)}`, { method: 'POST' }),
  clearMachinePath: (gameId: string, machineId: string) =>
    fetch(`/api/games/${gameId}/paths/${machineId}`, { method: 'DELETE', headers: headers() })
      .then(res => { if (!res.ok) throw new Error(`${res.status}`); }),

  audit: (limit = 200) => request<AuditEntry[]>(`/audit?limit=${limit}`),

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
