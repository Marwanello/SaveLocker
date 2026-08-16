import { useState, useEffect, useRef, useCallback, type CSSProperties } from 'react';
import { api } from '../api';
import type { AgentInstallerStatus, AgentPlatform, InstallerHashVerification } from '../types';

const asUtc = (t: string) => /[Z+]/.test(t.slice(-6)) ? t : t + 'Z';

/**
 * One row per hosted package (`AgentInstallerService`'s slots — win-x64, linux-x64, decky-plugin).
 * `parseVersion` reads the version out of a release asset's filename, so the admin rarely types it;
 * `decky-plugin` is the one exception (its zip is always literally `SaveLocker.zip`, so the plugin
 * repo's own release has to be typed or read from a GitHub fetch instead).
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
export function AgentUpdatesCard() {
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
    <div style={card}>
      <div style={cardHeader}>
        <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Agent updates</span>
        <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>hosted packages</span>
      </div>

      <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 10 }}>
        {INSTALLER_SLOTS.map(slot => {
          const status = statuses[slot.platform];
          return (
            <div key={slot.platform} style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span style={{ fontSize: 13, color: '#ECEFF1', fontWeight: 600, minWidth: 122 }}>{slot.label}</span>
              {loading ? (
                <span style={{ fontSize: 12, color: '#556070' }}>Loading…</span>
              ) : status ? (
                <>
                  <span style={{ padding: '2px 7px', background: '#129271', color: '#fff', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>
                    v{status.version}
                  </span>
                  <span style={{ fontSize: 11, color: '#9CA3AF' }}>{sourceLabel(status.source)}</span>
                  <span style={{ fontSize: 11, color: '#556070' }}>· {new Date(asUtc(status.uploadedAt)).toLocaleDateString()}</span>
                </>
              ) : (
                <span style={{ padding: '2px 7px', border: '1px solid #556070', color: '#556070', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>
                  none — these agents won't be offered updates
                </span>
              )}
            </div>
          );
        })}

        <div style={{ marginTop: 4 }}>
          <button
            onClick={() => setShowEdit(true)}
            style={{ padding: '6px 16px', background: 'transparent', color: '#129271', border: '1px solid #129271', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: 'pointer' }}
          >
            Edit
          </button>
        </div>
      </div>

      {showEdit && (
        <AgentUpdatesModal
          statuses={statuses}
          onClose={() => setShowEdit(false)}
          onChanged={reload}
        />
      )}
    </div>
  );
}

function AgentUpdatesModal({
  statuses, onClose, onChanged,
}: {
  statuses: StatusMap;
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const [checked, setChecked] = useState<Record<AgentPlatform, boolean>>(
    () => Object.fromEntries(INSTALLER_SLOTS.map(s => [s.platform, true])) as Record<AgentPlatform, boolean>
  );
  const [bulkFetching, setBulkFetching] = useState(false);
  const [bulkResults, setBulkResults] = useState<Partial<Record<AgentPlatform, string>>>({});

  async function handleBulkFetch() {
    const targets = INSTALLER_SLOTS.filter(s => checked[s.platform]);
    if (targets.length === 0) { alert('Check at least one package first.'); return; }
    setBulkFetching(true);
    setBulkResults({});
    // Sequential, not Promise.all: these share one server-side gate per platform anyway, and
    // reporting "package 2 of 3 failed" is only legible if the others have already finished.
    for (const slot of targets) {
      try {
        const info = await api.fetchInstallerFromGitHub(slot.platform);
        setBulkResults(prev => ({ ...prev, [slot.platform]: `✓ v${info.version}` }));
      } catch (e) {
        setBulkResults(prev => ({ ...prev, [slot.platform]: `✗ ${(e as Error).message}` }));
      }
    }
    setBulkFetching(false);
    await onChanged();
  }

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.55)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 50,
      }}
    >
      <div
        onClick={e => e.stopPropagation()}
        style={{
          width: 'min(720px, 92vw)', maxHeight: '86vh', overflowY: 'auto',
          background: '#1E252A', border: '1px solid #494949', borderRadius: 8,
          boxShadow: '0 20px 60px rgba(0,0,0,0.5)',
        }}
      >
        <div style={{ ...cardHeader, position: 'sticky', top: 0, background: '#1E252A', zIndex: 1 }}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Edit agent updates</span>
          <button
            onClick={onClose}
            style={{ padding: '3px 9px', background: 'transparent', border: '1px solid #494949', borderRadius: 4, color: '#9CA3AF', fontSize: 12, cursor: 'pointer' }}
          >
            Close
          </button>
        </div>

        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 8, borderBottom: '1px solid #2A3238' }}>
          <span style={{ fontSize: 13, color: '#ECEFF1', fontWeight: 600 }}>Fetch latest for all packages</span>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {INSTALLER_SLOTS.map(slot => (
              <label key={slot.platform} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12.5, color: '#ECEFF1', cursor: 'pointer' }}>
                <input
                  type="checkbox"
                  checked={checked[slot.platform]}
                  onChange={e => setChecked(prev => ({ ...prev, [slot.platform]: e.target.checked }))}
                />
                {slot.label}
                {bulkResults[slot.platform] && (
                  <span style={{ fontSize: 11, color: bulkResults[slot.platform]?.startsWith('✓') ? '#129271' : '#e05252', fontFamily: "'JetBrains Mono', monospace" }}>
                    {bulkResults[slot.platform]}
                  </span>
                )}
              </label>
            ))}
          </div>
          <div>
            <button
              onClick={handleBulkFetch}
              disabled={bulkFetching}
              style={{ padding: '7px 16px', background: bulkFetching ? '#2A3238' : '#129271', color: bulkFetching ? '#556070' : '#fff', border: 'none', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: bulkFetching ? 'default' : 'pointer' }}
            >
              {bulkFetching ? 'Fetching…' : 'Fetch selected from GitHub'}
            </button>
          </div>
        </div>

        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 18 }}>
          <span style={{ fontSize: 13, color: '#ECEFF1', fontWeight: 600 }}>Per-package</span>
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
    if (!file) { alert(`Choose a ${slot.label} package first.`); return; }
    const ver = versionOverride.trim() || slot.parseVersion(file.name);
    if (!ver) { alert('Could not parse version from filename. Enter it in the Version field.'); return; }
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      await api.uploadInstaller(fd, ver, slot.platform);
      setVersionOverride('');
      if (fileInputRef.current) fileInputRef.current.value = '';
      setVerification(null);
      await refreshStatus();
    } catch (e) { alert('Upload failed: ' + (e as Error).message); }
    finally { setUploading(false); }
  }

  async function handleDelete() {
    if (!confirm(`Remove the hosted ${slot.label} package? Those agents will no longer be offered an update until a new one is uploaded.`)) return;
    try { await api.deleteInstaller(slot.platform); setVerification(null); await refreshStatus(); }
    catch (e) { alert('Delete failed: ' + (e as Error).message); }
  }

  async function handleFetchGitHub() {
    setFetching(true);
    try {
      const info = await api.fetchInstallerFromGitHub(slot.platform);
      setVerification(null);
      await refreshStatus();
      alert(`Fetched v${info.version} (${info.fileName}) from GitHub.`);
    } catch (e) { alert('Fetch from GitHub failed: ' + (e as Error).message); }
    finally { setFetching(false); }
  }

  async function handleVerify() {
    setVerifying(true);
    try { setVerification(await api.verifyInstallerHash(slot.platform)); }
    catch (e) { alert('Hash check failed: ' + (e as Error).message); }
    finally { setVerifying(false); }
  }

  return (
    <div style={{
      display: 'flex', flexDirection: 'column', gap: 10,
      borderTop: first ? undefined : '1px solid #2A3238',
      paddingTop: first ? 0 : 14,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 13, color: '#ECEFF1', fontWeight: 600, minWidth: 122 }}>{slot.label}</span>
        {status ? (
          <>
            <span style={{ padding: '2px 7px', background: '#129271', color: '#fff', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>v{status.version}</span>
            <span style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 11, color: '#9CA3AF' }}>{status.fileName}</span>
            <span style={{ fontSize: 11, color: '#556070' }}>·</span>
            <span style={{ fontSize: 11, color: '#556070' }}>{(status.sizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
            <span style={{ fontSize: 11, color: '#556070' }}>· {sourceLabel(status.source)} · uploaded {new Date(asUtc(status.uploadedAt)).toLocaleDateString()}</span>
            <a
              href={`/api/agent/installer/download?platform=${slot.platform}`}
              style={{ fontSize: 11, color: '#129271', textDecoration: 'none' }}
              target="_blank" rel="noreferrer"
            >
              Download ↓
            </a>
            <button
              onClick={handleDelete}
              style={{ padding: '2px 10px', border: '1px solid #f4a60d', color: '#f4a60d', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }}
            >
              Delete
            </button>
          </>
        ) : (
          <span style={{ padding: '2px 7px', border: '1px solid #556070', color: '#556070', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>none — these agents won't be offered updates</span>
        )}
      </div>

      {/* Hash verification: on-demand, not automatic — every page load hitting GitHub for three
          packages just to render a status card would be a needless round-trip most sessions never
          look at. */}
      {status && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
          <button
            onClick={handleVerify}
            disabled={verifying}
            style={{ padding: '3px 10px', background: 'transparent', color: verifying ? '#556070' : '#8b9aaa', border: '1px solid #494949', borderRadius: 4, fontSize: 11, cursor: verifying ? 'default' : 'pointer' }}
          >
            {verifying ? 'Checking…' : 'Verify hash against GitHub'}
          </button>
          {verification && (
            <span
              title={verification.note ?? (verification.publishedSha256 ? `Published: ${verification.publishedSha256}\nHosted: ${status.sha256}` : undefined)}
              style={{
                fontSize: 11, fontFamily: "'JetBrains Mono', monospace",
                color: verification.status === 'match' ? '#129271' : verification.status === 'mismatch' ? '#e05252' : '#9CA3AF',
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
          style={{ flex: 1, minWidth: 200, padding: '5px 0', color: '#9CA3AF', fontSize: 12, background: 'transparent', border: 'none' }}
        />
        <input
          type="text"
          value={versionOverride}
          onChange={e => setVersionOverride(e.target.value)}
          placeholder="Version (e.g. 0.2.0)"
          aria-label={`${slot.label} package version`}
          style={{ width: 140, padding: '7px 10px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, fontFamily: "'JetBrains Mono', monospace" }}
        />
        <button
          onClick={handleUpload}
          disabled={uploading}
          style={{ padding: '6px 14px', background: uploading ? '#2A3238' : '#129271', color: uploading ? '#556070' : '#fff', border: 'none', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: uploading ? 'default' : 'pointer', whiteSpace: 'nowrap' }}
        >
          {uploading ? 'Uploading…' : 'Upload'}
        </button>
        <button
          onClick={handleFetchGitHub}
          disabled={fetching}
          style={{ padding: '6px 14px', background: 'transparent', color: fetching ? '#556070' : '#129271', border: `1px solid ${fetching ? '#2A3238' : '#129271'}`, borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: fetching ? 'default' : 'pointer', whiteSpace: 'nowrap' }}
        >
          {fetching ? 'Fetching…' : 'Fetch from GitHub'}
        </button>
      </div>
      <p style={{ fontSize: 11, color: '#9CA3AF', marginTop: -4 }}>
        <code style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{slot.fileHint}</code>
        {' '}from the release workflow — upload it, or pull it straight from the latest GitHub Release.
        The version is read from the filename. Connected {slot.label} agents are offered it at their next check-in.
      </p>
    </div>
  );
}

const card: CSSProperties = {
  background: '#1E252A', border: '1px solid #494949', borderRadius: 8, overflow: 'hidden',
};
const cardHeader: CSSProperties = {
  padding: '11px 18px', borderBottom: '1px solid #494949',
  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
};
