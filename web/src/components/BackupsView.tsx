import { useCallback, useEffect, useState } from 'react';
import { api, errorText } from '../api';
import type { BackupInfo, BackupStatus } from '../types';
import { ago, fmtSize, plural, toMs, localDayClock, utcClock, when } from '../format';
import { toast, toastError } from '../toast';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Stat } from './ui/Stat';
import { Card } from './ui/Card';
import { Chip } from './ui/Chip';
import { Button } from './ui/Button';
import { Banner } from './ui/Banner';
import { InlineConfirm } from './ui/InlineConfirm';
import { DataTable } from './ui/DataTable';
import type { Column } from './ui/DataTable';
import { EmptyState } from './ui/EmptyState';

const REASON: Record<BackupInfo['reason'], string> = {
  Scheduled: 'Scheduled',
  Manual: 'Manual',
  BeforeUpgrade: 'Before upgrade',
  BeforeRestore: 'Before restore',
};

const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

function countdown(t: string): string {
  const mins = Math.max(0, Math.round((toMs(t) - Date.now()) / 60000));
  const d = Math.floor(mins / 1440), h = Math.floor((mins % 1440) / 60), m = mins % 60;
  return d > 0 ? `in ${d}d ${h}h` : h > 0 ? `in ${h}h ${m}m` : `in ${m}m`;
}

interface Props {
  /** A restore replaced the whole database: everything the console holds is stale. */
  onRestored: () => void;
}

/**
 * plan.md Phase 11.1, extended at the maintainer's request (2026-09-29): each backup is one zip — the database
 * (compressed) plus every game's latest save — on a daily or weekly UTC schedule, and any of them can be restored
 * from here. A restore takes a safety backup first, so it can itself be undone.
 */
export function BackupsView({ onRestored }: Props) {
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
      if (r.ok && r.backup) toast(`Backup written. ${fmtSize(r.backup.sizeBytes)}.`);
      else toastError('The backup failed: ' + (r.message ?? 'no reason given'));
    } catch (e) {
      toastError('The backup failed: ' + errorText(e));
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

  async function restore(b: BackupInfo) {
    try {
      const r = await api.restoreBackup(b.fileName);
      toast(`Restored ${r.restoredFrom}. ${plural(r.savesRestored, 'save')} put back; the state before it is in ${r.safetyBackup}.`, 8000);
    } catch (e) {
      toastError('Nothing was restored: ' + errorText(e));
      return;
    }
    await load();
    onRestored();
  }

  if (!status) {
    return (
      <Page>
        <PageHead title="Backups" sub="the database and every game's latest save" />
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
  const intervalMs = (status.frequency === 'daily' ? 1 : 7) * 24 * 3600_000;
  const stale = !newest || Date.now() - toMs(newest.createdAt) > intervalMs + 2 * 3600_000;
  const hour = `${String(status.hourOfDay).padStart(2, '0')}:00 UTC`;
  const schedule = status.frequency === 'daily' ? `daily at ${hour}` : `every ${DAYS[status.dayOfWeek]} at ${hour}`;

  const lastRun = status.lastError
    ? <Chip tone="warn">Last run failed</Chip>
    : !newest
      ? <Chip tone="warn">No backups yet</Chip>
      : <Chip tone={stale ? 'warn' : 'ok'}>Last {ago(newest.createdAt)}</Chip>;

  const columns: Column<BackupInfo>[] = [
    { head: 'File', cell: b => b.fileName, kind: ['k', 'm'] },
    { head: 'Taken', cell: b => <span title={when(b.createdAt)}>{ago(b.createdAt)} · {utcClock(b.createdAt)}</span> },
    { head: 'Size', cell: b => fmtSize(b.sizeBytes), kind: 'n' },
    { head: 'Holds', cell: b => b.includesSaves ? 'Database + latest saves' : <span className="text-dim">Database only</span> },
    { head: 'Reason', cell: b => <Chip tone={b.reason === 'Scheduled' ? 'default' : 'warn'}>{REASON[b.reason]}</Chip> },
    {
      head: '', label: 'Actions', end: true,
      cell: b => (
        <div className="flex gap-1.5 justify-end flex-wrap">
          <Button size="sm" disabled={downloading !== null} onClick={() => void download(b)} aria-label={`Download ${b.fileName}`}>
            {downloading === b.fileName ? 'Downloading…' : 'Download'}
          </Button>
          <InlineConfirm
            label="Restore"
            title={`Restore ${b.fileName}`}
            consequence={<>
              Replaces the whole database with this backup from {when(b.createdAt)}: games, versions, machines,
              settings and the audit log go back to that moment{b.includesSaves ? ', and each game’s latest save in it is put back' : ''}.
              Anything since is set aside in a new <strong className="text-fg">Before restore</strong> backup first, so this can be undone.
              You may be asked to sign in again.
            </>}
            confirmLabel={`Restore ${b.fileName}`}
            onConfirm={() => restore(b)}
          />
        </div>
      ),
    },
  ];

  return (
    <Page>
      <PageHead
        title="Backups"
        sub={<>the database and every game's latest save · {status.enabled ? schedule : 'scheduled backups off'} · keeps the newest {status.retentionCount}</>}
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
        <Stat label="Backups" value={backups.length}
          context={oldest ? `oldest ${new Date(toMs(oldest.createdAt)).toLocaleDateString([], { day: 'numeric', month: 'short' })}` : 'none yet'} />
        <Stat label="On disk" value={fmtSize(onDisk)} context={<span className="font-mono break-all">{status.backupRoot}</span>} />
        <Stat label="All save versions" value={fmtSize(status.archivesBytes)}
          context={`${plural(status.archivesCount, 'version')} · only each game's latest is backed up`} />
        <Stat label="Next run"
          value={status.enabled && status.nextRunAt ? localDayClock(status.nextRunAt) : 'Off'}
          context={status.enabled
            ? status.nextRunAt ? `${countdown(status.nextRunAt)} · ${utcClock(status.nextRunAt)}` : 'being scheduled'
            : 'Scheduled backups are off — Configuration turns them on'} />
      </div>

      <Card
        title="Recent backups"
        headerRight={<span className="font-mono text-[11px] text-faint">zip · VACUUM INTO · safe while running</span>}
        flush
      >
        <p className="px-4 py-3 text-xs text-dim border-b border-line">
          Each backup holds the database — machine-key and password <em>hashes</em>, and the SteamGridDB key
          <em> encrypted</em> with a key that stays on this server — and every game's latest save. Older versions are not
          included. Keep a downloaded one private. <a href="#help/database-backups" className="text-fg underline underline-offset-2 rounded
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">
            More on backups and restoring</a>.
        </p>
        <DataTable
          caption="Backups, newest first"
          columns={columns}
          rows={backups}
          rowKey={b => b.fileName}
          empty={<EmptyState title="No backups yet">
            {status.enabled
              ? `The first runs ${schedule}, or take one now with Back up now.`
              : 'Scheduled backups are off. Take one now with Back up now.'}
          </EmptyState>}
        />
      </Card>
    </Page>
  );
}
