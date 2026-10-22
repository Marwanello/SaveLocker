import { useState, useEffect, useCallback } from 'react';
import type { ReactNode } from 'react';
import { renderSVG } from 'uqr';
import { api, signIn, clearSession, errorText } from '../../src/api';
import type { GameSummary, Machine, Settings, Enrollment, EffectiveServerUrl, AgentHealth, AdminSession, ServerBuildInfo, BackupStatus } from '../../src/types';
import { fleetSkew, isNewerThanConsole, isTestBuild, normalizeVersion } from '../../src/versionSkew';
import { ago, fmtSize, plural, toMs, when } from '../../src/format';
import { machineOs, osLine } from '../../src/machineOs';
import { toast, toastError } from '../../src/toast';
import { AgentUpdatesCard } from '../../src/components/AgentUpdatesCard';
import { AppearanceCard } from '../../src/components/AppearanceCard';
import { HandoverCard } from '../../src/components/HandoverCard';
import { Page } from '../../src/components/ui/Page';
import { PageHead } from '../../src/components/ui/PageHead';
import { Card } from '../../src/components/ui/Card';
import { Chip } from '../../src/components/ui/Chip';
import { Button } from '../../src/components/ui/Button';
import { Banner } from '../../src/components/ui/Banner';
import { KV } from '../../src/components/ui/KV';
import { Meter } from '../../src/components/ui/Meter';
import { Switch } from '../../src/components/ui/Switch';
import { InlineConfirm } from '../../src/components/ui/InlineConfirm';
import { GlobChips } from '../../src/components/ui/GlobChips';
import { DataTable } from '../../src/components/ui/DataTable';
import type { Column } from '../../src/components/ui/DataTable';
import { EmptyState } from '../../src/components/ui/EmptyState';
import { OsBadge } from '../../src/components/ui/OsLogo';
import { useMock } from '../store';
import { OffsiteSetup, OffsiteStatus, MakeRoom } from '../features/offsite';
import { ConvertCard } from '../features/convert';
import { RecentlyDeleted } from '../features/history';

interface Props {
  games: GameSummary[];
  machines: Machine[];
  settings: Settings;
  health: AgentHealth[];
  build?: ServerBuildInfo;
  onRefresh: () => void;
}

const field = `px-3 py-2 bg-panel text-fg border border-line rounded-lg text-[12.5px] min-w-0
  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1`;

/**
 * Copy to the clipboard on every deployment, not only the secure ones: `navigator.clipboard` is
 * undefined on a plain-http LAN console (not a secure context), which is exactly the deployment
 * this console usually is — so the old optional-chained call silently copied nothing.
 */
function copyText(text: string) {
  const fallback = () => {
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); } catch { /* nothing left to try */ }
    document.body.removeChild(ta);
  };
  if (navigator.clipboard?.writeText) void navigator.clipboard.writeText(text).catch(fallback);
  else fallback();
}

function Toggle({ label, hint, checked, onChange, disabled }: {
  label: string; hint: ReactNode; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean;
}) {
  return (
    <div className="flex items-center justify-between gap-4 py-2.5 border-b border-row last:border-b-0">
      <div className="min-w-0">
        <div className="text-[13px] font-semibold text-fg">{label}</div>
        <div className="text-xs text-dim mt-0.5">{hint}</div>
      </div>
      <Switch checked={checked} onChange={onChange} disabled={disabled} aria-label={label} />
    </div>
  );
}

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** The inline dropdown the Defaults & maintenance hints use for a value stored on the server. */
function InlineSelect({ label, value, onChange, children }: {
  label: string; value: string | number; onChange: (value: string) => void; children: ReactNode;
}) {
  return (
    <select aria-label={label} value={value} onChange={e => onChange(e.target.value)}
      className="bg-panel border border-line rounded-md px-1 py-0 text-xs text-fg focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent">
      {children}
    </select>
  );
}

const left = (t: string) => {
  const m = Math.max(0, Math.round((toMs(t) - Date.now()) / 60000));
  return m < 60 ? `${m}m` : `${Math.floor(m / 60)}h ${m % 60}m`;
};

const hours = (s: number) => {
  const h = s / 3600;
  return h >= 1 ? plural(Math.round(h * 10) / 10, 'hour') : plural(Math.round(s / 60), 'minute');
};

/**
 * plan.md Phase 11.6–11.7: the prototype's two `grid2` rows — Server beside Appearance, Enroll a machine
 * beside Defaults & maintenance — then Machines, with Agent updates and Admin password underneath.
 * Every `alert()`/`confirm()` that was here is a toast or an inline confirmation now.
 */
export function ConfigView({ games, machines, settings, health, build, onRefresh }: Props) {
  // Mockup: the planned storage cards, for the feature on show.
  const feature = useMock(s => s.feature);
  const mockVariant = useMock(s => s.variant);
  const healthByMachine = new Map(health.map(h => [h.machineId, h]));
  const skew = fleetSkew(build?.version, health);
  const storage = settings.storage ?? null;

  // ── SteamGridDB ──
  const [sgdbInput, setSgdbInput] = useState('');
  const [savingKey, setSavingKey] = useState(false);

  /** The input is cleared and the view refreshed only after the server confirms the key was stored: a
   *  rejected key keeps what was pasted, and the previously working key is untouched (the server
   *  verifies before storing). */
  async function handleSaveKey() {
    const v = sgdbInput.trim();
    if (!v) { toastError('Paste a SteamGridDB API key first.'); return; }
    setSavingKey(true);
    try {
      const res = await api.saveSgdbKey(v);
      setSgdbInput('');
      toast(res.message || 'Saved the SteamGridDB key.', 4000);
      onRefresh();
    } catch (e) {
      toastError('Key not saved: ' + errorText(e));
    } finally { setSavingKey(false); }
  }

  async function handleClearKey() {
    try { await api.saveSgdbKey(null); toast('Cleared the SteamGridDB key.'); onRefresh(); }
    catch (e) { toastError('Could not clear the key: ' + errorText(e)); }
  }

  // ── Admin password ──
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  async function handleSetPassword() {
    if (!newPassword) { toastError('Enter a new password.'); return; }
    if (newPassword !== confirmPassword) { toastError('The two passwords do not match.'); return; }
    try {
      const res = await api.setAdminPassword(newPassword);
      // A password change ends every session on the server — this browser's included — so sign in
      // again with the new one. The password is used for that one request and not kept.
      const again = await signIn(newPassword);
      setNewPassword('');
      setConfirmPassword('');
      if (again.ok) toast(res.message || 'Changed the admin password.', 4000);
      else toastError(`${res.message} You were signed out: ${again.message}`);
      onRefresh();
    } catch (e) { toastError('Could not set the password: ' + errorText(e)); }
  }

  async function handleClearPassword() {
    try { await api.setAdminPassword(null); clearSession(); toast('Removed the admin password.'); onRefresh(); }
    catch (e) { toastError('Could not remove the password: ' + errorText(e)); }
  }

  async function handleSignOutEverywhere() {
    try {
      await api.signOutEverywhere();
      clearSession();
      onRefresh(); // the next request is refused, which sends this browser back to the sign-in screen
    } catch (e) { toastError('Could not sign out: ' + errorText(e)); }
  }

  // ── Signed-in browsers ──
  // Sessions already existed; listing them is what turns "sign out everywhere" into "end the one
  // that is not mine" when a token goes missing with a browser.
  const [sessions, setSessions] = useState<AdminSession[] | null>(null);

  const loadSessions = useCallback(async () => {
    try { setSessions(await api.sessions()); } catch { /* the password controls still work without the table */ }
  }, []);

  useEffect(() => {
    if (settings.adminPasswordSet) void loadSessions();
  }, [settings.adminPasswordSet, loadSessions]);

  async function handleRevokeSession(id: string) {
    try {
      await api.revokeSession(id);
      toast('Ended that session.');
      await loadSessions();
    } catch (e) { toastError('Could not end the session: ' + errorText(e)); }
  }

  // ── Machines ──
  async function handleDeleteMachine(machineId: string, name: string) {
    try { await api.deleteMachine(machineId); toast(`Deleted ${name}.`); onRefresh(); }
    catch (e) { toastError('Could not delete the machine: ' + errorText(e)); }
  }

  async function handleReleaseLeases(name: string, gameIds: string[]) {
    const failed: string[] = [];
    for (const id of gameIds) {
      try { await api.forceRelease(id); } catch (e) { failed.push(errorText(e)); }
    }
    onRefresh();
    if (failed.length) toastError(`Released ${gameIds.length - failed.length} of ${gameIds.length}: ${failed[0]}`);
    else toast(`Released ${plural(gameIds.length, 'lease')} held by ${name}.`);
  }

  // ── Enrollment (join links — tasks/enrollment-options, option B) ──
  const [enrollments, setEnrollments] = useState<Enrollment[]>([]);
  const [enrollName, setEnrollName] = useState('');
  const [enrollTtl, setEnrollTtl] = useState('15');
  const [enrollServerUrl, setEnrollServerUrl] = useState('');
  const [minting, setMinting] = useState(false);
  // The code and link come back exactly once (the server keeps only the hash), so the card holds
  // them until the admin hides the panel or navigates away.
  const [joinLink, setJoinLink] = useState<{ code: string; url: string; expiresAt: string } | null>(null);

  const loadEnrollments = useCallback(async () => {
    try { setEnrollments(await api.enrollments()); } catch { /* non-fatal */ }
  }, []);
  useEffect(() => { void loadEnrollments(); }, [loadEnrollments]);

  // The URL the join link will carry. Worth showing unprompted: the failure it prevents is silent —
  // a link that works for the admin at the server and sends the Deck looking for itself.
  const [effectiveUrl, setEffectiveUrl] = useState<EffectiveServerUrl | null>(null);
  useEffect(() => { api.effectiveServerUrl().then(setEffectiveUrl).catch(() => setEffectiveUrl(null)); }, []);
  // Only an INFERRED loopback address blocks minting. A configured or typed one is a deliberate
  // same-box setup (agent and server on one machine).
  const blockedByLoopback = effectiveUrl?.isLoopback === true && !effectiveUrl.fromConfig;

  async function handleCreateJoinLink() {
    const ttl = parseInt(enrollTtl, 10);
    if (!Number.isFinite(ttl) || ttl < 1) { toastError('Enter an expiry in minutes (at least 1).'); return; }
    setMinting(true);
    try {
      const res = await api.createEnrollment({
        machineName: enrollName.trim() || null,
        ttlMinutes: ttl,
        serverUrl: enrollServerUrl.trim() || null,
      });
      setJoinLink({ code: res.code, url: res.joinUrl, expiresAt: res.expiresAt });
      setEnrollName('');
      toast('Join link created — open it on the new machine.', 4000);
      await loadEnrollments();
    } catch (e) { toastError('Could not create the join link: ' + errorText(e)); }
    finally { setMinting(false); }
  }

  async function handleRevokeEnrollment(e: Enrollment) {
    try { await api.revokeEnrollment(e.id); await loadEnrollments(); toast(e.redeemedAt ? 'Removed it from the list.' : 'Revoked the join link — an agent still holding it cannot use it.'); }
    catch (err) { toastError('Could not revoke it: ' + errorText(err)); }
  }

  // ── Defaults & maintenance ──
  const savedDefaults = settings.defaultExcludeGlobs ?? [];
  const [defaultsDraft, setDefaultsDraft] = useState<string[]>(savedDefaults);
  const [seenDefaults, setSeenDefaults] = useState(JSON.stringify(savedDefaults));
  // Follow a save made elsewhere, but never throw away a draft in progress (same rule as a game's card).
  if (seenDefaults !== JSON.stringify(savedDefaults)) {
    if (JSON.stringify(defaultsDraft) === seenDefaults) setDefaultsDraft(savedDefaults);
    setSeenDefaults(JSON.stringify(savedDefaults));
  }
  const defaultsDirty = JSON.stringify(defaultsDraft) !== JSON.stringify(savedDefaults);
  const [savingDefaults, setSavingDefaults] = useState(false);
  const [defaultsError, setDefaultsError] = useState<string | null>(null);

  async function saveDefaults() {
    setSavingDefaults(true);
    setDefaultsError(null);
    try {
      await api.setDefaultExcludes(defaultsDraft);
      toast('Saved the default exclude patterns. Agents apply them from their next game-list poll.', 4000);
      onRefresh();
    } catch (e) { setDefaultsError(errorText(e)); }
    finally { setSavingDefaults(false); }
  }

  const [backup, setBackup] = useState<BackupStatus | null>(null);
  const loadBackup = useCallback(async () => {
    try { setBackup(await api.backupStatus()); } catch { setBackup(null); }
  }, []);
  useEffect(() => { void loadBackup(); }, [loadBackup]);

  async function setBackupSchedule(patch: Partial<{ enabled: boolean; retentionCount: number; hourOfDay: number; frequency: string; dayOfWeek: number }>) {
    if (!backup) return;
    const next = {
      enabled: backup.enabled, retentionCount: backup.retentionCount, hourOfDay: backup.hourOfDay,
      frequency: backup.frequency ?? 'weekly', dayOfWeek: backup.dayOfWeek ?? 0, ...patch,
    };
    const at = `${String(next.hourOfDay).padStart(2, '0')}:00 UTC`;
    try {
      await api.setBackupSettings(next);
      toast(next.enabled
        ? `Backups on: ${next.frequency === 'daily' ? `daily at ${at}` : `every ${WEEKDAYS[next.dayOfWeek]} at ${at}`}, keeping ${next.retentionCount}.`
        : 'Scheduled backups are off. Back up now still works.');
      await loadBackup();
    } catch (e) { toastError('Could not change the backup schedule: ' + errorText(e)); }
  }

  const schedule = settings.schedule ?? null;
  const autoUpdateOn = !!schedule && schedule.mode !== 'disabled' && !(schedule.mode === 'hours' && schedule.hours <= 0);
  async function setAutoUpdate(on: boolean) {
    const base = schedule ?? { mode: 'hours', hours: 24, dayOfWeek: 0, dayOfMonth: 1, timeOfDay: '03:00' };
    try {
      await api.setAutoFetchSchedule(on
        ? { ...base, mode: 'hours', hours: base.mode === 'hours' && base.hours > 0 ? base.hours : 24 }
        : { ...base, mode: 'disabled' });
      toast(on ? 'The server checks GitHub for new agents again.' : 'The server no longer checks GitHub for new agents.');
      onRefresh();
    } catch (e) { toastError('Could not change auto-update: ' + errorText(e)); }
  }

  // ── Manifest refresh (save-detection data) ──
  const manifestDays = settings.manifestRefreshDays;
  const [savingManifest, setSavingManifest] = useState(false);
  async function setManifestDays(days: number) {
    setSavingManifest(true);
    try {
      const stored = await api.setManifestRefreshDays(days);
      toast(stored.refreshDays === 0
        ? 'Automatic manifest refresh is off. Refresh now still works.'
        : `Each machine refreshes its save-detection data every ${plural(stored.refreshDays, 'day')}.`);
      onRefresh();
    } catch (e) { toastError('Could not change the manifest refresh window: ' + errorText(e)); }
    finally { setSavingManifest(false); }
  }
  const [refreshingManifest, setRefreshingManifest] = useState(false);
  async function refreshManifestNow() {
    setRefreshingManifest(true);
    try {
      await api.refreshManifestNow();
      toast('Refresh requested — every machine picks it up at its next heartbeat.');
      onRefresh();
    } catch (e) { toastError('Could not request a manifest refresh: ' + errorText(e)); }
    finally { setRefreshingManifest(false); }
  }

  // ── Server card ──
  const [copiedBuild, setCopiedBuild] = useState(false);
  const volumeUsed = storage?.volumeTotalBytes && storage.volumeFreeBytes != null
    ? (storage.volumeTotalBytes - storage.volumeFreeBytes) / storage.volumeTotalBytes : null;

  const buildText = build ? (build.version === 'dev' ? 'dev' : `v${build.version}`) : '…';

  // ── Machines table ──
  const leasesBy = new Map<string, string[]>();
  for (const g of games) {
    const holder = g.lease?.holderMachineId;
    if (holder && g.lease?.expiresAt && toMs(g.lease.expiresAt) > Date.now()) leasesBy.set(holder, [...(leasesBy.get(holder) ?? []), g.game.id]);
  }

  const machineColumns: Column<Machine>[] = [
    {
      head: 'Machine', kind: 'k',
      cell: m => {
        const h = healthByMachine.get(m.id);
        const problems = h?.openEvents.filter(e => e.severity !== 'Info') ?? [];
        const infos = h?.openEvents.filter(e => e.severity === 'Info') ?? [];
        return (
          <div className="flex items-center gap-2 flex-wrap">
            <OsBadge os={machineOs(h)} size="sm" />
            <span className="font-mono">{m.name}</span>
            {problems.length > 0 && <span title={problems.map(e => e.message).join('\n')}><Chip tone="crit">{plural(problems.length, 'problem')}</Chip></span>}
            {infos.length > 0 && <span title={infos.map(e => e.message).join('\n')}><Chip>{plural(infos.length, 'update')}</Chip></span>}
            {(h?.offlineQueueDepth ?? 0) > 0 && <Chip tone="warn">{h!.offlineQueueDepth} queued</Chip>}
          </div>
        );
      },
    },
    { head: 'Platform', cell: m => osLine(machineOs(healthByMachine.get(m.id))) ?? healthByMachine.get(m.id)?.platform ?? '—', kind: 'wrap' },
    {
      head: 'Agent', kind: 'm',
      cell: m => {
        const h = healthByMachine.get(m.id);
        return (
          <span className="inline-flex items-center gap-1.5 flex-wrap">
            {h?.agentVersion ? `v${normalizeVersion(h.agentVersion)}` : '—'}
            {isTestBuild(h?.agentVersion) && <span title="A throwaway build from CI, not a release."><Chip>Test build</Chip></span>}
            {isNewerThanConsole(h?.agentVersion, build?.version) &&
              <span title="This agent is newer than the console and may expect routes this server lacks — upgrade the server container."><Chip tone="warn">Newer than console</Chip></span>}
          </span>
        );
      },
    },
    {
      head: 'Last seen',
      cell: m => {
        const h = healthByMachine.get(m.id);
        // The health API returns a row for EVERY machine, so "never reported" is the missing heartbeat,
        // not a missing row — an enrolled agent that has not started is not one that went offline.
        if (!h?.lastHeartbeat) return <Chip>Never reported</Chip>;
        return <span title={when(h.lastHeartbeat)}><Chip tone={h.online ? 'ok' : 'warn'}>{h.online ? 'Online' : `Offline · ${ago(h.lastHeartbeat)}`}</Chip></span>;
      },
    },
    {
      head: 'Games', kind: 'n',
      cell: m => {
        const h = healthByMachine.get(m.id);
        return <>{h ? h.trackedGames : '—'}{(h?.unmappedGames ?? 0) > 0 && <span className="text-watch-ink"> ({h!.unmappedGames} unmapped)</span>}</>;
      },
    },
    {
      head: '', label: 'Actions', end: true,
      cell: m => {
        const held = leasesBy.get(m.id) ?? [];
        return (
          <div className="flex gap-1.5 justify-end flex-wrap">
            {held.length > 0 && (
              <InlineConfirm
                label={`Force-release ${plural(held.length, 'lease')}`}
                consequence={`${m.name} is marked as playing ${plural(held.length, 'game')}. Releasing lets another machine push or pull now — only do this if ${m.name} is really not running it.`}
                confirmLabel={`Release ${m.name}'s ${held.length === 1 ? 'lease' : 'leases'}`}
                onConfirm={() => handleReleaseLeases(m.name, held)}
              />
            )}
            <InlineConfirm
              label="Delete"
              consequence={`${m.name}'s API key stops working at once. The versions it uploaded are kept as history.`}
              confirmLabel={`Delete ${m.name}`}
              onConfirm={() => handleDeleteMachine(m.id, m.name)}
            />
          </div>
        );
      },
    },
  ];

  const enrollmentColumns: Column<Enrollment>[] = [
    { head: 'Created', cell: e => <span title={when(e.createdAt)}>{ago(e.createdAt)}</span>, kind: 'm' },
    { head: 'For machine', cell: e => e.machineName ?? <span className="text-faint">any machine</span>, kind: 'k' },
    {
      head: 'State',
      cell: e => e.redeemedAt
        ? <span title={`Used by ${e.redeemedByMachineName ?? 'a machine'}`}><Chip tone="ok">Used · {e.redeemedByMachineName ?? 'a machine'}</Chip></span>
        : toMs(e.expiresAt) <= Date.now()
          ? <Chip>Expired</Chip>
          : <span title={`Valid until ${when(e.expiresAt)}`}><Chip tone="warn">Valid · {left(e.expiresAt)} left</Chip></span>,
    },
    {
      head: '', label: 'Actions', end: true,
      cell: e => e.redeemedAt || toMs(e.expiresAt) <= Date.now()
        ? <Button size="sm" variant="quiet" onClick={() => void handleRevokeEnrollment(e)}>Remove</Button>
        : <InlineConfirm label="Revoke" consequence="An agent still holding this link will not be able to use it."
            confirmLabel="Revoke the link" onConfirm={() => handleRevokeEnrollment(e)} />,
    },
  ];

  return (
    <Page>
      <PageHead
        title="Configuration"
        sub="server settings · appearance · enrollment · storage"
        actions={settings.adminPasswordSet
          ? <Chip tone="ok">Connected as admin</Chip>
          : <Chip tone="warn">Open — no admin password</Chip>}
      />

      {feature === 'st-setup' && <OffsiteSetup />}
      {(feature === 'st-status' || (feature === 'st-full' && mockVariant === 'B')) && <OffsiteStatus />}
      {feature === 'st-full' && mockVariant === 'C' && <MakeRoom />}
      {feature === 'st-convert' && <ConvertCard />}
      {feature === 'vh-trash' && mockVariant === 'C' && <RecentlyDeleted />}

      <div className="grid gap-4 lg:grid-cols-2 items-start">
        <Card title="Server">
          <KV items={[
            { label: 'Public URL', value: <span className="font-mono text-[11.5px] break-all">{effectiveUrl?.url ?? '…'}{effectiveUrl?.fromConfig && <span className="text-faint font-sans"> (Server:PublicBaseUrl)</span>}</span> },
            { label: 'Storage', value: <span className="font-mono text-[11.5px] break-all">{storage?.archiveRoot ?? '—'}</span> },
            {
              label: 'Build', value: (
                <span className="inline-flex items-center gap-2 flex-wrap">
                  <span>{buildText}{build?.commit && ` · commit ${build.commit.slice(0, 7)}`}{build?.builtAt && ` · built ${new Date(toMs(build.builtAt)).toLocaleDateString()}`}</span>
                  {build && !build.isRelease && <span title="This build is not a tagged release."><Chip tone="warn">Dev build</Chip></span>}
                  <Button size="sm" variant="quiet" title="Copy the build identity — paste it into a bug report" onClick={() => {
                    const text = [`SaveLocker console ${build?.version ?? 'unknown'}`, build?.commit ? `commit ${build.commit}` : null, build?.builtAt ? `built ${build.builtAt}` : null].filter(Boolean).join('\n');
                    void navigator.clipboard?.writeText(text);
                    setCopiedBuild(true);
                    setTimeout(() => setCopiedBuild(false), 1500);
                  }}>{copiedBuild ? 'Copied' : 'Copy'}</Button>
                  <a href="#whats-new" className="text-xs text-dim underline underline-offset-2 rounded focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent">Release notes</a>
                </span>
              ),
            },
            { label: 'Escalate', value: storage ? `Conflicts go overdue after ${hours(storage.escalationAfterSeconds)}` : '—' },
          ]} />

          {storage && (
            <div className="mt-4">
              {volumeUsed !== null && <Meter value={volumeUsed} tone={volumeUsed > 0.9 ? 'watch' : 'safe'} aria-label="Storage volume used" />}
              <div className="flex justify-between gap-3 flex-wrap mt-2 font-mono text-[11px] text-faint">
                <span>{fmtSize(storage.archivesBytes)} of saves across {plural(storage.gamesWithArchives, 'game')}</span>
                <span>{storage.volumeTotalBytes != null && storage.volumeFreeBytes != null
                  ? `${fmtSize(storage.volumeFreeBytes)} free of ${fmtSize(storage.volumeTotalBytes)}`
                  : 'volume size unknown'}</span>
              </div>
            </div>
          )}

          {(skew.aheadOfConsole.length > 0 || skew.mixedVersions.length > 0) && (
            <div className="mt-4 flex flex-col gap-2">
              {skew.aheadOfConsole.length > 0 && (
                <Banner tone="watch" title={`${skew.aheadOfConsole.join(', ')} ${skew.aheadOfConsole.length > 1 ? 'run agents' : 'runs an agent'} newer than this console`}>
                  A newer agent can expect routes this server does not have, and fails with an opaque HTTP error. Pull the latest server image.
                </Banner>
              )}
              {skew.mixedVersions.length > 0 && (
                <Banner tone="watch" title={`The fleet runs ${skew.mixedVersions.length} agent versions (${skew.mixedVersions.map(v => `v${v}`).join(', ')})`}>
                  Agents that differ can disagree about exclude patterns and save paths, which shows up as repeated conflicts.
                </Banner>
              )}
            </div>
          )}

          <div className="mt-4 pt-4 border-t border-line">
            <div className="flex items-center gap-2 flex-wrap mb-2">
              <span className="text-[13px] font-semibold text-fg">SteamGridDB artwork</span>
              {settings.steamGridDbConfigured
                ? <><Chip tone="ok">Key set</Chip><span className="font-mono text-xs text-dim">{settings.steamGridDbKeyMasked}</span>
                    {settings.steamGridDbFromConfig && <span className="text-[11.5px] text-dim">(from config — saving here overrides it)</span>}</>
                : <Chip tone="warn">No key</Chip>}
            </div>
            <div className="flex items-center gap-2 flex-wrap">
              <input type="text" value={sgdbInput} onChange={e => setSgdbInput(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter') void handleSaveKey(); }}
                placeholder="Paste a SteamGridDB API key" aria-label="SteamGridDB API key" className={`${field} flex-1 min-w-[200px]`} />
              <Button size="sm" onClick={() => void handleSaveKey()} disabled={savingKey}>{savingKey ? 'Verifying…' : 'Save key'}</Button>
              {settings.steamGridDbConfigured && (
                <InlineConfirm label="Clear" consequence="Cover art stops refreshing until a key is set again. Art already fetched stays."
                  confirmLabel="Clear the key" onConfirm={handleClearKey} />
              )}
            </div>
            <p className="text-[11px] text-dim mt-2">
              Free key: <a href="https://www.steamgriddb.com" target="_blank" rel="noreferrer" className="underline underline-offset-2 text-fg rounded focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent">steamgriddb.com</a> → user menu → Preferences → API.
            </p>
          </div>
        </Card>

        <AppearanceCard settings={settings} health={health} onSaved={onRefresh} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2 items-start">
        <Card title="Enroll a machine" flush>
          <div className="px-4 py-[15px]">
            <p className="text-[13px] text-dim mb-3">
              Creates a one-time join link. Open it on the new machine — the page installs the agent and enrolls it
              in one pass, or run <code className="font-mono text-[11.5px] text-fg">savelocker enroll --url &lt;link&gt;</code>.
              The code inside is shown once and expires on its own.
            </p>
            {effectiveUrl && (
              <p className={`text-xs mb-3 ${effectiveUrl.isLoopback ? 'text-watch-ink' : 'text-dim'}`}>
                {blockedByLoopback
                  ? <><strong>This console was reached at {effectiveUrl.url}</strong>, which no other machine can use. Reopen it at the
                      server's LAN address, set <code className="font-mono">Server:PublicBaseUrl</code>, or type the address below.</>
                  : <>The link will tell the agent to sync with <code className="font-mono text-fg">{effectiveUrl.url}</code>
                      {effectiveUrl.fromConfig ? ' (from Server:PublicBaseUrl).' : '.'} Override it below if the machine reaches the server differently.</>}
              </p>
            )}
            <div className="grid gap-2.5 sm:grid-cols-[minmax(0,1fr)_110px]">
              <label className="flex flex-col gap-1">
                <span className="text-[10px] tracking-[0.1em] uppercase text-faint">For (optional, binds the link)</span>
                <input value={enrollName} onChange={e => setEnrollName(e.target.value)} placeholder="steamdeck" className={field} />
              </label>
              <label className="flex flex-col gap-1">
                <span className="text-[10px] tracking-[0.1em] uppercase text-faint">Expires (min)</span>
                <input type="number" min={1} value={enrollTtl} onChange={e => setEnrollTtl(e.target.value)} className={`${field} font-mono`} />
              </label>
              <label className="flex flex-col gap-1 sm:col-span-2">
                <span className="text-[10px] tracking-[0.1em] uppercase text-faint">Server URL the agent should use (optional)</span>
                <input value={enrollServerUrl} onChange={e => setEnrollServerUrl(e.target.value)} placeholder={effectiveUrl?.url ?? window.location.origin} className={`${field} font-mono`} />
              </label>
            </div>
            {(() => {
              const blocked = minting || (blockedByLoopback && !enrollServerUrl.trim());
              return (
                <div className="flex items-center gap-2 flex-wrap mt-3.5">
                  <Button variant="primary" onClick={() => void handleCreateJoinLink()} disabled={blocked}
                    title={blockedByLoopback && !enrollServerUrl.trim() ? "Enter the address agents should use, or reopen the console at this server's LAN address." : undefined}>
                    {minting ? 'Creating…' : 'Create join link'}
                  </Button>
                  {joinLink && <Button size="sm" variant="quiet" onClick={() => setJoinLink(null)}>Hide the link</Button>}
                </div>
              );
            })()}

            {joinLink && (
              <div className="mt-4 rounded-xl border border-accent-line bg-accent-soft p-4">
                <div className="grid gap-4 sm:grid-cols-[132px_minmax(0,1fr)] items-start">
                  <div>
                    {/* A QR must be dark-on-light to scan, whatever theme the console is in — hence
                        the forced white tile and black modules rather than theme tokens. */}
                    <div className="rounded-lg bg-white p-1.5 w-[132px] h-[132px]" style={{ color: 'black' }}
                      dangerouslySetInnerHTML={{ __html: renderSVG(joinLink.url, { pixelSize: 4, border: 2, ecc: 'M', blackColor: 'currentColor', whiteColor: 'transparent' }) }} />
                    <div className="text-[10.5px] text-dim text-center mt-1.5">scan to open on a phone</div>
                  </div>
                  <div className="min-w-0 flex flex-col gap-2.5">
                    <div className="font-mono text-[25px] font-semibold tracking-[0.16em] text-center text-fg bg-tile border border-line rounded-[10px] px-3 py-2.5 select-all">
                      {joinLink.code.slice(0, 4)}-{joinLink.code.slice(4)}
                    </div>
                    <div className="flex items-center gap-2 flex-wrap">
                      <span title={`Valid until ${when(joinLink.expiresAt)} UTC`}>
                        <Chip tone="warn">Valid · {left(joinLink.expiresAt)} left</Chip>
                      </span>
                      <Button size="sm" onClick={() => { copyText(joinLink.url); toast('Copied the join link.'); }}>Copy link</Button>
                      <Button size="sm" variant="quiet" onClick={() => { copyText(joinLink.code); toast('Copied the code.'); }}>Copy code</Button>
                    </div>
                    <p className="text-xs text-dim">
                      Open the link on the new machine; the page installs the agent and enrolls it in one pass —
                      on Windows the installer asks for a paste of the same link. On a Steam Deck the pairing
                      page gives a one-liner that does both over SSH.
                    </p>
                    <p className="text-[11px] text-faint">Shown once — create another any time.</p>
                  </div>
                </div>
              </div>
            )}
          </div>
          <div className="border-t border-line">
            <DataTable caption="Join links" columns={enrollmentColumns} rows={enrollments} rowKey={e => e.id}
              empty={<div className="px-4 py-5 text-[12.5px] text-dim">No join links created yet.</div>} />
          </div>
        </Card>

        <Card title="Defaults & maintenance">
          <Toggle label="Agent auto-update" checked={autoUpdateOn} onChange={v => void setAutoUpdate(v)}
            hint={autoUpdateOn ? 'Checks GitHub for a newer installer and hosts it for agents. Schedule: Agent updates, below.' : 'Off — upload installers by hand under Agent updates, below.'} />

          <div className="flex items-center justify-between gap-4 py-2.5 border-b border-row">
            <div className="min-w-0">
              <div className="text-[13px] font-semibold text-fg">Manifest refresh</div>
              <div className="text-xs text-dim mt-0.5">
                The save-detection data used to find new games.
                {savingManifest ? ' Saving…' : <> Each machine refreshes{' '}
                  <InlineSelect label="Manifest refresh window" value={manifestDays}
                    onChange={v => void setManifestDays(Number(v))}>
                    {/* The stored value is anywhere from 0-365 (a config default included), so it is
                        unioned in — a controlled select with no matching option renders blank. */}
                    {[...new Set([1, 3, 7, 14, 30, 0, manifestDays])].map(d =>
                      <option key={d} value={d}>
                        {{ 0: 'never automatically', 1: 'every day', 3: 'every 3 days', 7: 'weekly', 14: 'every 14 days', 30: 'monthly' }[d] ?? `every ${d} days`}
                      </option>)}
                  </InlineSelect>.</>}
                {settings.manifestRefreshRequestedAt && <> Last requested {ago(settings.manifestRefreshRequestedAt)}.</>}
              </div>
            </div>
            <Button size="sm" variant="quiet" disabled={refreshingManifest} onClick={() => void refreshManifestNow()}>
              {refreshingManifest ? 'Requesting…' : 'Refresh now'}
            </Button>
          </div>

          <Toggle label="Scheduled backup" checked={!!backup?.enabled} disabled={!backup}
            onChange={v => void setBackupSchedule({ enabled: v })}
            hint={backup
              ? <span className="inline-flex flex-wrap items-center gap-x-1 gap-y-1">
                  The database and every game's latest save, zipped,
                  <InlineSelect label="Backup frequency" value={backup.frequency ?? 'weekly'}
                    onChange={v => void setBackupSchedule({ frequency: v })}>
                    <option value="daily">daily</option>
                    <option value="weekly">weekly</option>
                  </InlineSelect>
                  {(backup.frequency ?? 'weekly') === 'weekly' && <>on
                    <InlineSelect label="Backup day" value={backup.dayOfWeek ?? 0}
                      onChange={v => void setBackupSchedule({ dayOfWeek: Number(v) })}>
                      {WEEKDAYS.map((d, i) => <option key={d} value={i}>{d}</option>)}
                    </InlineSelect></>}
                  at
                  <InlineSelect label="Backup hour (UTC)" value={backup.hourOfDay}
                    onChange={v => void setBackupSchedule({ hourOfDay: Number(v) })}>
                    {Array.from({ length: 24 }, (_, h) => <option key={h} value={h}>{String(h).padStart(2, '0')}:00 UTC</option>)}
                  </InlineSelect>, keep
                  <InlineSelect label="Backups to keep" value={backup.retentionCount}
                    onChange={v => void setBackupSchedule({ retentionCount: Number(v) })}>
                    {[...new Set([2, 4, 8, 12, 26, 52, backup.retentionCount])].sort((a, b) => a - b).map(n => <option key={n} value={n}>{n}</option>)}
                  </InlineSelect>. <a href="#backups" className="underline underline-offset-2 rounded focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent">Backups</a>
                </span>
              : 'Reading the backup schedule…'} />

          <KV className="mt-3.5" items={[
            { label: 'Default keep', value: storage ? `${plural(storage.defaultRetainVersions, 'version')} per game — a game's own Keep overrides it` : '—' },
          ]} />

          <div className="mt-4">
            <div className="text-[10px] tracking-[0.1em] uppercase text-faint mb-2">
              Default exclude patterns <span className="normal-case tracking-normal">— every game inherits these</span>
            </div>
            <GlobChips patterns={defaultsDraft} onChange={d => { setDefaultsDraft(d); setDefaultsError(null); }} addLabel="Add a default exclude pattern (Enter adds it)" />
            {defaultsError && <p role="alert" className="mt-2.5 text-[11.5px] text-accent-ink">{defaultsError}</p>}
            <p className="text-xs text-faint mt-2.5">
              {settings.defaultExcludeGlobsFromConsole ? 'Saved from this console.' : 'From the server configuration until saved here.'}{' '}
              Per-game patterns are set on the game itself, under <strong className="text-dim">Games → Exclude patterns</strong>, and stack on top of these.
            </p>
            <div className="flex gap-2 mt-3 flex-wrap">
              <Button size="sm" disabled={!defaultsDirty || savingDefaults} onClick={() => void saveDefaults()}>{savingDefaults ? 'Saving…' : 'Save defaults'}</Button>
              {defaultsDirty && <Button size="sm" variant="quiet" onClick={() => { setDefaultsDraft(savedDefaults); setDefaultsError(null); }}>Discard changes</Button>}
            </div>
          </div>
        </Card>
      </div>

      <HandoverCard />

      <Card title="Machines" headerRight={<span className="text-[11.5px] text-dim">agent health, versions and keys</span>} flush>
        <DataTable caption="Registered machines" columns={machineColumns} rows={machines} rowKey={m => m.id}
          empty={<EmptyState title="No machines yet">Create an enrollment file above and run it on the machine.</EmptyState>} />
      </Card>

      <div className="grid gap-4 lg:grid-cols-2 items-start">
        <AgentUpdatesCard settings={settings} onScheduleChanged={onRefresh} />

        <Card id="admin-password" title="Admin password" headerRight={settings.adminPasswordSet ? <Chip tone="ok">Protected</Chip> : <Chip tone="warn">Open — no password</Chip>}>
          <div className="flex flex-col gap-2">
            <input type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)} autoComplete="new-password"
              placeholder={settings.adminPasswordSet ? 'New password' : 'Set a password'} aria-label="New admin password" className={field} />
            <input type="password" value={confirmPassword} onChange={e => setConfirmPassword(e.target.value)} autoComplete="new-password"
              onKeyDown={e => { if (e.key === 'Enter') void handleSetPassword(); }}
              placeholder="Confirm the password" aria-label="Confirm the admin password" className={field} />
          </div>
          <div className="flex gap-2 flex-wrap mt-3">
            <Button size="sm" onClick={() => void handleSetPassword()}>{settings.adminPasswordSet ? 'Change password' : 'Set password'}</Button>
            {settings.adminPasswordSet && (
              <InlineConfirm label="Sign out everywhere" consequence="Ends every signed-in session on every browser, this one included."
                confirmLabel="Sign out every browser" onConfirm={handleSignOutEverywhere} />
            )}
            {settings.adminPasswordSet && (
              <InlineConfirm label="Remove" consequence="Anyone who can reach this console on your network can use it."
                confirmLabel="Remove the password" onConfirm={handleClearPassword} />
            )}
          </div>
          <p className="text-[11.5px] text-dim mt-3">
            Signing in gives a browser a session, not a copy of the password. Lock (in the header) or Sign out
            everywhere ends it, and changing the password ends every session.
          </p>

          {settings.adminPasswordSet && sessions !== null && sessions.length > 0 && (
            <div className="mt-4 pt-3 border-t border-line">
              <div className="flex items-center justify-between mb-2 gap-2">
                <span className="text-[10.5px] font-semibold text-dim uppercase tracking-[0.08em]">Signed-in browsers</span>
                <span className="text-[11px] text-faint">{plural(sessions.length, 'session')}</span>
              </div>
              <ul className="flex flex-col gap-2">
                {sessions.map(s => (
                  <li key={s.id} className="flex items-center justify-between gap-2 text-[11.5px] min-w-0">
                    <span className="min-w-0 truncate text-dim">
                      <span className="font-mono text-faint">{s.clientAddress || 'unknown'}</span>
                      {' · last used '}{ago(s.lastUsedAt)}
                      {s.isCurrent && <span className="ml-2 align-middle"><Chip tone="ok">This browser</Chip></span>}
                    </span>
                    {!s.isCurrent && (
                      <InlineConfirm label="Revoke"
                        consequence={`Ends the session from ${s.clientAddress || 'an unknown address'}. That browser has to sign in again.`}
                        confirmLabel="End that session" onConfirm={() => handleRevokeSession(s.id)} />
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Card>
      </div>
    </Page>
  );
}
