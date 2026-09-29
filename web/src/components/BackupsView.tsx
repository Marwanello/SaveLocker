import { useCallback, useEffect, useState } from 'react';
import { api, errorText } from '../api';
import type { BackupInfo, BackupStatus } from '../types';
import { ago, fmtSize, plural, toMs, when } from '../format';
import { toast, toastError } from '../toast';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Stat } from './ui/Stat';
import { Card } from './ui/Card';
import { Chip } from './ui/Chip';
import { Button } from './ui/Button';
import { Banner } from './ui/Banner';
import { DataTable } from './ui/DataTable';
import type { Column } from './ui/DataTable';
import { EmptyState } from './ui/EmptyState';

/** A nightly snapshot lands every 24 h; two hours of slack before "last night" stops being true. */
const FRESH_MS = 26 * 3600_000;

const REASON: Record<BackupInfo['reason'], string> = {
  Nightly: 'Nightly',
  Manual: 'Manual',
  BeforeUpgrade: 'Before upgrade',
};

const clock = (t: string) => new Date(toMs(t)).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

function countdown(t: string): string {
  const mins = Math.max(0, Math.round((toMs(t) - Date.now()) / 60000));
  const h = Math.floor(mins / 60), m = mins % 60;
  return h > 0 ? `in ${h}h ${m}m` : `in ${m}m`;
}

/**
 * plan.md Phase 11.1: the nightly `VACUUM INTO` snapshots of the database, which nobody could see without
 * curl. A snapshot is the version graph — not the save archives — and the page says so, because that is
 * the one thing a person restoring from it must know first.
 */
export function BackupsView() {
  const [status, setStatus] = useState<BackupStatus | null>(null);
  const [loadError, setLoadError] = useState('');
  const [busy, setBusy] = useState(false);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [, setTick] = useState(0);

  const load = useCallback(async () => {
    try {
      setStatus(await api.backupStatus());
      setLoadError('');
    } catch (e) {
      setLoadError(errorText(e));
    }
  }, []);

  useEffect(() => { void load(); }, [load]);
  // The countdown and the "last run" chip read the clock; a minute is their resolution.
  useEffect(() => {
    const id = setInterval(() => setTick(t => t + 1), 60_000);
    return () => clearInterval(id);
  }, []);

  async function backUpNow() {
    setBusy(true);
    try {
      const r = await api.backupNow();
      if (r.ok && r.backup) toast(`Snapshot written. ${fmtSize(r.backup.sizeBytes)}.`);
      else toastError('The snapshot failed: ' + (r.message ?? 'no reason given'));
    } catch (e) {
      toastError('The snapshot failed: ' + errorText(e));
    } finally {
      setBusy(false);
      await load();
    }
  }

  async function download(b: BackupInfo) {
    setDownloading(b.fileName);
    try {
      await api.downloadBackup(b.fileName);
      toast(`Downloaded ${b.fileName}.`);
    } catch (e) {
      toastError('Could not download: ' + errorText(e));
    } finally {
      setDownloading(null);
    }
  }

  if (!status) {
    return (
      <Page>
        <PageHead title="Backups" sub="nightly snapshots of the database" />
        {loadError
          ? <Banner tone="watch" title="Could not read the backups" action={<Button size="sm" onClick={() => void load()}>Retry</Button>}>{loadError}</Banner>
          : <div className="text-dim text-[13px]">Loading…</div>}
      </Page>
    );
  }

  const backups = status.backups;
  const newest = backups[0];
  const oldest = backups[backups.length - 1];
  const onDisk = backups.reduce((n, b) => n + b.sizeBytes, 0);
  const stale = !newest || Date.now() - toMs(newest.createdAt) > FRESH_MS;

  const lastRun = status.lastError
    ? <Chip tone="warn">Last run failed</Chip>
    : !newest
      ? <Chip tone="warn">No snapshots yet</Chip>
      : <Chip tone={stale ? 'warn' : 'ok'}>Last {ago(newest.createdAt)}</Chip>;

  const columns: Column<BackupInfo>[] = [
    { head: 'File', cell: b => b.fileName, kind: ['k', 'm'] },
    { head: 'Taken', cell: b => <span title={when(b.createdAt)}>{ago(b.createdAt)} · {clock(b.createdAt)}</span> },
    { head: 'Size', cell: b => fmtSize(b.sizeBytes), kind: 'n' },
    { head: 'Reason', cell: b => <Chip tone={b.reason === 'Nightly' ? 'default' : 'warn'}>{REASON[b.reason]}</Chip> },
    {
      head: '', label: 'Actions', end: true,
      cell: b => (
        <Button size="sm" disabled={downloading !== null} onClick={() => void download(b)}
          aria-label={`Download ${b.fileName}`}>
          {downloading === b.fileName ? 'Downloading…' : 'Download'}
        </Button>
      ),
    },
  ];

  return (
    <Page>
      <PageHead
        title="Backups"
        sub={<>nightly snapshots of the database · keeps the newest {status.retentionCount}</>}
        actions={<>
          {lastRun}
          <Button variant="primary" disabled={busy} onClick={() => void backUpNow()}>
            {busy ? 'Backing up…' : 'Back up now'}
          </Button>
        </>}
      />

      {status.lastError && (
        <Banner tone="watch" title="The last backup failed">
          {status.lastError}{status.lastErrorAt ? ` · ${ago(status.lastErrorAt)}` : ''}. The next run tries again.
        </Banner>
      )}

      <div className="grid grid-cols-[repeat(auto-fit,minmax(180px,1fr))] gap-3">
        <Stat label="Snapshots" value={backups.length}
          context={oldest ? `oldest ${new Date(toMs(oldest.createdAt)).toLocaleDateString([], { day: 'numeric', month: 'short' })}` : 'none yet'} />
        <Stat label="On disk" value={fmtSize(onDisk)} context={<span className="font-mono break-all">{status.backupRoot}</span>} />
        <Stat label="Archives" value={fmtSize(status.archivesBytes)}
          context={`${plural(status.archivesCount, 'version')} · not included in snapshots`} />
        <Stat label="Next run"
          value={status.enabled && status.nextRunAt ? clock(status.nextRunAt) : 'Off'}
          context={status.enabled
            ? status.nextRunAt ? countdown(status.nextRunAt) : 'being scheduled'
            : 'Scheduled backups are off'} />
      </div>

      <Card
        title="Recent snapshots"
        headerRight={<span className="font-mono text-[11px] text-faint">VACUUM INTO · safe while running</span>}
        flush
      >
        <p className="px-4 py-3 text-xs text-dim border-b border-line">
          A snapshot holds every machine’s API-key hash, the admin password hash and the SteamGridDB key.
          Keep a downloaded one as safe as the admin password. It holds the version history, not the save
          files themselves — <a href="#help/database-backups" className="text-fg underline underline-offset-2 rounded
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">
            restoring one</a>.
        </p>
        <DataTable
          caption="Database snapshots, newest first"
          columns={columns}
          rows={backups}
          rowKey={b => b.fileName}
          empty={<EmptyState title="No snapshots yet">
            {status.enabled
              ? `The first nightly one runs at ${String(status.hourOfDay).padStart(2, '0')}:00, or take one now with Back up now.`
              : 'Scheduled backups are off. Take one now with Back up now.'}
          </EmptyState>}
        />
      </Card>
    </Page>
  );
}
