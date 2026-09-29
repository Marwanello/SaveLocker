import { useState, useEffect, useRef, useCallback, type CSSProperties } from 'react';
import { api, errorText } from '../api';
import { toast, toastError } from '../toast';
import { Card } from './ui/Card';
import { Chip } from './ui/Chip';
import { Button } from './ui/Button';
import { InlineConfirm } from './ui/InlineConfirm';
import type { AgentInstallerStatus, AgentPlatform, AutoFetchSchedule, InstallerHashVerification, Settings } from '../types';
import { asUtc } from '../format';

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

function ordinal(n: number) {
  if (n % 10 === 1 && n % 100 !== 11) return `${n}st`;
  if (n % 10 === 2 && n % 100 !== 12) return `${n}nd`;
  if (n % 10 === 3 && n % 100 !== 13) return `${n}rd`;
  return `${n}th`;
}

/** Plain-sentence summary for the card — the whole point of moving this behind Edit was that the
 *  default view should read like a sentence, not a form. */
function describeSchedule(schedule: AutoFetchSchedule | undefined, nextRunAt: string | null | undefined): string {
  if (!schedule || schedule.mode === 'disabled') return 'Automatic fetch is off.';
  const next = nextRunAt ? `. Next check ${new Date(asUtc(nextRunAt)).toLocaleString()}` : '';
  switch (schedule.mode) {
    case 'hours':
      return schedule.hours > 0
        ? `Checks GitHub every ${schedule.hours} hour${schedule.hours === 1 ? '' : 's'}${next}.`
        : 'Automatic fetch is off.';
    case 'weekly':
      return `Checks GitHub every ${DAY_NAMES[schedule.dayOfWeek] ?? '?'} at ${schedule.timeOfDay} (server time)${next}.`;
    case 'monthly':
      return `Checks GitHub on the ${ordinal(schedule.dayOfMonth)} of each month at ${schedule.timeOfDay} (server time)${next}.`;
    default:
      return 'Automatic fetch is off.';
  }
}

/**
 * One row per hosted package (`AgentInstallerService`'s slots — win-x64, linux-x64, decky-plugin,
 * playnite-plugin). `parseVersion` reads the version out of a release asset's filename, so the admin
 * rarely types it; `decky-plugin` and `playnite-plugin` are the exceptions (their zips are always
 * literally `SaveLocker.zip`, so their own repo's release has to be typed or read from a GitHub fetch
 * instead).
 */
interface InstallerSlot {
  platform: AgentPlatform;
  label: string;
  accept: string;
  fileHint: string;
  parseVersion: (name: string) => string;
}

const INSTALLER_SLOTS: InstallerSlot[] = [
  {
    platform: 'win-x64',
    label: 'Windows',
    accept: '.exe',
    fileHint: 'SaveLocker-Agent-Setup-x.y.z.exe',
    parseVersion: name => name.match(/Setup-(.+?)\.exe$/i)?.[1] ?? '',
  },
  {
    platform: 'linux-x64',
    label: 'Linux / Steam Deck',
    accept: '.gz,.tar.gz',
    fileHint: 'savelocker-x.y.z-linux-x64.tar.gz',
    parseVersion: name => name.match(/^savelocker-(.+?)-linux-x64\.tar\.gz$/i)?.[1] ?? '',
  },
  {
    platform: 'decky-plugin',
    label: 'Decky plugin',
    accept: '.zip',
    fileHint: 'SaveLocker.zip — type the version, it is not in the filename',
    parseVersion: name => name.match(/^SaveLocker-?(\d[\d.]*)\.zip$/i)?.[1] ?? '',
  },
  {
    platform: 'playnite-plugin',
    label: 'Playnite plugin',
    accept: '.zip',
    fileHint: 'SaveLocker.zip — type the version, it is not in the filename',
    parseVersion: name => name.match(/^SaveLocker-?(\d[\d.]*)\.zip$/i)?.[1] ?? '',
  },
];

type StatusMap = Partial<Record<AgentPlatform, AgentInstallerStatus | null>>;

function sourceLabel(source: string | undefined) {
  return source === 'github' ? 'GitHub fetch' : 'manual upload';
}

/**
 * Read-only summary of the three hosted packages, with a single Edit button behind which every
 * upload/fetch/delete/hash-check control lives. Used to sprawl across the card directly — three
 * packages' worth of file pickers, version fields and buttons made it the busiest thing on the
 * Config page even when nothing needed doing.
 */
export function AgentUpdatesCard({
  settings, onScheduleChanged,
}: {
  settings: Settings;
  /** Re-fetches the App-level settings — the schedule lives there, not in this component's own
   *  per-package status state, so a schedule change has to bubble up rather than just reload(). */
  onScheduleChanged: () => void;
}) {
  const [statuses, setStatuses] = useState<StatusMap>({});
  const [loading, setLoading] = useState(true);
  const [showEdit, setShowEdit] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const pairs = await Promise.all(
        INSTALLER_SLOTS.map(async slot => {
          try { return [slot.platform, await api.installerStatus(slot.platform)] as const; }
          catch { return [slot.platform, null] as const; }
        })
      );
      setStatuses(Object.fromEntries(pairs));
    } finally { setLoading(false); }
  }, []);

  useEffect(() => { reload(); }, [reload]);

  return (
    <Card title="Agent updates" headerRight={<span className="text-[11.5px] text-dim">hosted packages</span>}>
      <div className="flex flex-col gap-2.5">
        {INSTALLER_SLOTS.map(slot => {
          const status = statuses[slot.platform];
          return (
            <div key={slot.platform} className="flex items-center gap-2.5 flex-wrap">
              <span className="text-[13px] font-semibold text-fg min-w-[122px]">{slot.label}</span>
              {loading
                ? <span className="text-xs text-dim">Loading…</span>
                : status
                  ? <>
                      <Chip tone="ok">v{status.version}</Chip>
                      <span className="text-[11px] text-dim">{sourceLabel(status.source)} · {new Date(asUtc(status.uploadedAt)).toLocaleDateString()}</span>
                    </>
                  : <Chip>none — these agents won't be offered updates</Chip>}
            </div>
          );
        })}

        <div className="pt-2.5 border-t border-line text-xs text-dim">
          {describeSchedule(settings.schedule ?? undefined, settings.nextAutoFetchRunAt)}
        </div>

        <div>
          <Button size="sm" aria-expanded={showEdit} onClick={() => setShowEdit(v => !v)}>
            {showEdit ? 'Done editing' : 'Edit'}
          </Button>
        </div>
      </div>

      {/* In place, not a modal (plan.md "No modals"): the editor opens inside the card it edits. */}
      {showEdit && (
        <AgentUpdatesEditor
          statuses={statuses}
          schedule={settings.schedule ?? undefined}
          onChanged={reload}
          onScheduleChanged={onScheduleChanged}
        />
      )}
    </Card>
  );
}

function AgentUpdatesEditor({
  statuses, schedule, onChanged, onScheduleChanged,
}: {
  statuses: StatusMap;
  schedule: AutoFetchSchedule | undefined;
  onChanged: () => Promise<void>;
  onScheduleChanged: () => void;
}) {
  const [checked, setChecked] = useState<Record<AgentPlatform, boolean>>(
    () => Object.fromEntries(INSTALLER_SLOTS.map(s => [s.platform, true])) as Record<AgentPlatform, boolean>
  );
  const [bulkFetching, setBulkFetching] = useState(false);
  const [bulkResults, setBulkResults] = useState<Partial<Record<AgentPlatform, string>>>({});

  async function handleBulkFetch() {
    const targets = INSTALLER_SLOTS.filter(s => checked[s.platform]);
    if (targets.length === 0) { toastError('Check at least one package first.'); return; }
    setBulkFetching(true);
    setBulkResults({});
    // Sequential, not Promise.all: these share one server-side gate per platform anyway, and
    // reporting "package 2 of 3 failed" is only legible if the others have already finished.
    for (const slot of targets) {
      try {
        const info = await api.fetchInstallerFromGitHub(slot.platform);
        setBulkResults(prev => ({ ...prev, [slot.platform]: `✓ v${info.version}` }));
      } catch (e) {
        setBulkResults(prev => ({ ...prev, [slot.platform]: `✗ ${errorText(e)}` }));
      }
    }
    setBulkFetching(false);
    await onChanged();
  }

  return (
    <div className="animate-drop mt-4 -mx-4 -mb-[15px] border-t border-line">
        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 8, borderBottom: '1px solid var(--color-line)' }}>
          <span style={{ fontSize: 13, color: 'var(--color-fg)', fontWeight: 600 }}>Fetch latest for all packages</span>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {INSTALLER_SLOTS.map(slot => (
              <label key={slot.platform} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12.5, color: 'var(--color-fg)', cursor: 'pointer' }}>
                <input
                  type="checkbox"
                  checked={checked[slot.platform]}
                  onChange={e => setChecked(prev => ({ ...prev, [slot.platform]: e.target.checked }))}
                  style={{ accentColor: 'var(--color-accent)' }}
                />
                {slot.label}
                {bulkResults[slot.platform] && (
                  <span style={{ fontSize: 11, color: bulkResults[slot.platform]?.startsWith('✓') ? 'var(--color-safe-ink)' : 'var(--color-accent-ink)', fontFamily: "'JetBrains Mono', monospace" }}>
                    {bulkResults[slot.platform]}
                  </span>
                )}
              </label>
            ))}
          </div>
          <div>
            <Button size="sm" onClick={() => void handleBulkFetch()} disabled={bulkFetching}>
              {bulkFetching ? 'Fetching…' : 'Fetch selected from GitHub'}
            </Button>
          </div>
        </div>

        <div style={{ padding: '16px 18px', borderBottom: '1px solid var(--color-line)' }}>
          <ScheduleEditor schedule={schedule} onChanged={onScheduleChanged} />
        </div>

        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 18 }}>
          <span style={{ fontSize: 13, color: 'var(--color-fg)', fontWeight: 600 }}>Per-package</span>
          {INSTALLER_SLOTS.map((slot, i) => (
            <InstallerSlotEditor
              key={slot.platform}
              slot={slot}
              first={i === 0}
              status={statuses[slot.platform] ?? null}
              onChanged={onChanged}
            />
          ))}
        </div>
    </div>
  );
}

const DEFAULT_SCHEDULE: AutoFetchSchedule = {
  mode: 'hours', hours: 0, dayOfWeek: 0, dayOfMonth: 1, timeOfDay: '03:00',
};

const selectStyle: CSSProperties = {
  padding: '6px 9px', background: 'var(--color-raise)', color: 'var(--color-fg)', border: '1px solid var(--color-line)',
  borderRadius: 5, fontSize: 12, fontFamily: 'inherit',
};
const numberInputStyle: CSSProperties = {
  width: 70, padding: '6px 9px', background: 'transparent', color: 'var(--color-fg)',
  border: '1px solid var(--color-line)', borderRadius: 5, fontSize: 12, fontFamily: "'JetBrains Mono', monospace",
};

function ScheduleEditor({
  schedule: initial, onChanged,
}: {
  schedule: AutoFetchSchedule | undefined;
  onChanged: () => void;
}) {
  // Seeded once from whatever the modal opened with, deliberately NOT kept in sync with `initial`
  // afterward: the app polls /api/settings every 15s in the background, and re-syncing on every
  // prop change silently overwrote an admin's in-progress edit with the still-unsaved server value
  // mid-keystroke. The modal remounts fresh each time it's opened, which is the only "sync" this
  // needs.
  const [draft, setDraft] = useState<AutoFetchSchedule>(() => initial ?? DEFAULT_SCHEDULE);
  const [saving, setSaving] = useState(false);

  async function handleSave() {
    setSaving(true);
    try {
      await api.setAutoFetchSchedule(draft);
      onChanged();
      toast('Saved the fetch schedule.');
    } catch (e) { toastError('Could not save the schedule: ' + errorText(e)); }
    finally { setSaving(false); }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <span style={{ fontSize: 13, color: 'var(--color-fg)', fontWeight: 600 }}>Automatic fetch schedule</span>

      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <select
          value={draft.mode}
          onChange={e => setDraft(d => ({ ...d, mode: e.target.value }))}
          style={selectStyle}
        >
          <option value="disabled">Disabled</option>
          <option value="hours">Every N hours</option>
          <option value="weekly">Weekly</option>
          <option value="monthly">Monthly</option>
        </select>

        {draft.mode === 'hours' && (
          <>
            <input
              type="number" min={0} step={0.5}
              value={draft.hours}
              onChange={e => setDraft(d => ({ ...d, hours: Number(e.target.value) }))}
              aria-label="Hours between checks"
              style={numberInputStyle}
            />
            <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>hours</span>
          </>
        )}

        {draft.mode === 'weekly' && (
          <select
            value={draft.dayOfWeek}
            onChange={e => setDraft(d => ({ ...d, dayOfWeek: Number(e.target.value) }))}
            style={selectStyle}
          >
            {DAY_NAMES.map((name, i) => <option key={name} value={i}>{name}</option>)}
          </select>
        )}

        {draft.mode === 'monthly' && (
          <>
            <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>day</span>
            <input
              type="number" min={1} max={31}
              value={draft.dayOfMonth}
              onChange={e => setDraft(d => ({ ...d, dayOfMonth: Number(e.target.value) }))}
              aria-label="Day of month"
              style={numberInputStyle}
            />
            <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>of each month</span>
          </>
        )}

        {(draft.mode === 'weekly' || draft.mode === 'monthly') && (
          <>
            <span style={{ fontSize: 12, color: 'var(--color-dim)' }}>at</span>
            <input
              type="time"
              value={draft.timeOfDay}
              onChange={e => setDraft(d => ({ ...d, timeOfDay: e.target.value }))}
              aria-label="Time of day"
              style={{ ...numberInputStyle, width: 100 }}
            />
            <span style={{ fontSize: 11, color: 'var(--color-dim)' }}>server time</span>
          </>
        )}

        <Button size="sm" onClick={() => void handleSave()} disabled={saving}>
          {saving ? 'Saving…' : 'Save schedule'}
        </Button>
      </div>

      <p style={{ fontSize: 11, color: 'var(--color-dim)', margin: 0 }}>
        {draft.mode === 'hours'
          ? 'Set 0 to disable. Reconfiguring checks GitHub immediately, then at this interval.'
          : draft.mode === 'disabled'
          ? 'No automatic checks — use "Fetch selected from GitHub" above, or Fetch from GitHub per package below.'
          : 'Time of day is this server\'s local clock, not your browser\'s. Saving recomputes the next check without running one immediately.'}
      </p>
    </div>
  );
}

function InstallerSlotEditor({
  slot, first, status: initialStatus, onChanged,
}: {
  slot: InstallerSlot;
  first: boolean;
  status: AgentInstallerStatus | null;
  onChanged: () => Promise<void>;
}) {
  const [status, setStatus] = useState(initialStatus);
  const [uploading, setUploading] = useState(false);
  const [fetching, setFetching] = useState(false);
  const [versionOverride, setVersionOverride] = useState('');
  const [verification, setVerification] = useState<InstallerHashVerification | null>(null);
  const [verifying, setVerifying] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => { setStatus(initialStatus); }, [initialStatus]);

  async function refreshStatus() {
    try { setStatus(await api.installerStatus(slot.platform)); } catch { /* non-fatal */ }
    await onChanged();
  }

  async function handleUpload() {
    const file = fileInputRef.current?.files?.[0];
    if (!file) { toastError(`Choose a ${slot.label} package first.`); return; }
    const ver = versionOverride.trim() || slot.parseVersion(file.name);
    if (!ver) { toastError('Could not read a version from the file name. Enter it in the Version field.'); return; }
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      await api.uploadInstaller(fd, ver, slot.platform);
      setVersionOverride('');
      if (fileInputRef.current) fileInputRef.current.value = '';
      setVerification(null);
      await refreshStatus();
      toast(`Uploaded ${slot.label} v${ver}.`);
    } catch (e) { toastError('Upload failed: ' + errorText(e)); }
    finally { setUploading(false); }
  }

  async function handleDelete() {
    try {
      await api.deleteInstaller(slot.platform); setVerification(null); await refreshStatus();
      toast(`Removed the hosted ${slot.label} package.`);
    } catch (e) { toastError('Delete failed: ' + errorText(e)); }
  }

  async function handleFetchGitHub() {
    setFetching(true);
    try {
      const info = await api.fetchInstallerFromGitHub(slot.platform);
      setVerification(null);
      await refreshStatus();
      toast(`Fetched v${info.version} (${info.fileName}) from GitHub.`, 4000);
    } catch (e) { toastError('Fetch from GitHub failed: ' + errorText(e)); }
    finally { setFetching(false); }
  }

  async function handleVerify() {
    setVerifying(true);
    try { setVerification(await api.verifyInstallerHash(slot.platform)); }
    catch (e) { toastError('Hash check failed: ' + errorText(e)); }
    finally { setVerifying(false); }
  }

  return (
    <div style={{
      display: 'flex', flexDirection: 'column', gap: 10,
      borderTop: first ? undefined : '1px solid var(--color-line)',
      paddingTop: first ? 0 : 14,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 13, color: 'var(--color-fg)', fontWeight: 600, minWidth: 122 }}>{slot.label}</span>
        {status ? (
          <>
            <span style={{ padding: '2px 7px', background: 'var(--color-safe-soft)', color: 'var(--color-safe-ink)', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>v{status.version}</span>
            <span style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 11, color: 'var(--color-dim)' }}>{status.fileName}</span>
            <span style={{ fontSize: 11, color: 'var(--color-dim)' }}>·</span>
            <span style={{ fontSize: 11, color: 'var(--color-dim)' }}>{(status.sizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
            <span style={{ fontSize: 11, color: 'var(--color-dim)' }}>· {sourceLabel(status.source)} · uploaded {new Date(asUtc(status.uploadedAt)).toLocaleDateString()}</span>
            <a
              href={`/api/agent/installer/download?platform=${slot.platform}`}
              style={{ fontSize: 11, color: 'var(--color-fg)', textDecoration: 'none' }}
              target="_blank" rel="noreferrer"
            >
              Download ↓
            </a>
            <InlineConfirm
              label="Delete"
              consequence={`Removes the hosted ${slot.label} package. Those agents are not offered an update until a new one is uploaded.`}
              confirmLabel={`Delete the ${slot.label} package`}
              onConfirm={handleDelete}
            />
          </>
        ) : (
          <span style={{ padding: '2px 7px', border: '1px solid var(--color-line)', color: 'var(--color-dim)', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>none — these agents won't be offered updates</span>
        )}
      </div>

      {/* Hash verification: on-demand, not automatic — every page load hitting GitHub for three
          packages just to render a status card would be a needless round-trip most sessions never
          look at. */}
      {status && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <Button size="sm" variant="quiet" onClick={() => void handleVerify()} disabled={verifying}>
            {verifying ? 'Checking…' : 'Verify hash against GitHub'}
          </Button>
          {verification && (
            <span
              title={verification.note ?? (verification.publishedSha256 ? `Published: ${verification.publishedSha256}\nHosted: ${status.sha256}` : undefined)}
              style={{
                fontSize: 11, fontFamily: "'JetBrains Mono', monospace",
                color: verification.status === 'match' ? 'var(--color-safe-ink)' : verification.status === 'mismatch' ? 'var(--color-accent-ink)' : 'var(--color-dim)',
              }}
            >
              {verification.status === 'match' ? '✓ matches published checksum'
                : verification.status === 'mismatch' ? '✗ DOES NOT MATCH published checksum'
                : `? ${verification.note ?? 'unknown'}`}
            </span>
          )}
        </div>
      )}

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <input
          ref={fileInputRef}
          type="file"
          accept={slot.accept}
          aria-label={`${slot.label} agent package`}
          onChange={e => {
            const parsed = slot.parseVersion(e.target.files?.[0]?.name ?? '');
            if (parsed) setVersionOverride(parsed);
          }}
          style={{ flex: 1, minWidth: 200, padding: '5px 0', color: 'var(--color-dim)', fontSize: 12, background: 'transparent', border: 'none' }}
        />
        <input
          type="text"
          value={versionOverride}
          onChange={e => setVersionOverride(e.target.value)}
          placeholder="Version (e.g. 0.2.0)"
          aria-label={`${slot.label} package version`}
          style={{ width: 140, padding: '7px 10px', background: 'transparent', color: 'var(--color-fg)', border: '1px solid var(--color-line)', borderRadius: 5, fontSize: 12, fontFamily: "'JetBrains Mono', monospace" }}
        />
        <Button size="sm" onClick={() => void handleUpload()} disabled={uploading}>
          {uploading ? 'Uploading…' : 'Upload'}
        </Button>
        <Button size="sm" onClick={() => void handleFetchGitHub()} disabled={fetching}>
          {fetching ? 'Fetching…' : 'Fetch from GitHub'}
        </Button>
      </div>
      <p style={{ fontSize: 11, color: 'var(--color-dim)', marginTop: -4 }}>
        <code style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{slot.fileHint}</code>
        {' '}from the release workflow — upload it, or pull it straight from the latest GitHub Release.
        The version is read from the filename. Connected {slot.label} agents are offered it at their next check-in.
      </p>
    </div>
  );
}
