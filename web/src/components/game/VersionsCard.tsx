import { useEffect, useState } from 'react';
import { api, errorText } from '../../api';
import type { Conflict, Game, Version, VersionChanges } from '../../types';
import { ago, fmtSize, plural, shortId, toMs, when } from '../../format';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { Chip } from '../ui/Chip';
import { Seg } from '../ui/Seg';
import { Button } from '../ui/Button';
import { DataTable } from '../ui/DataTable';
import { EmptyState } from '../ui/EmptyState';
import { InlineConfirm } from '../ui/InlineConfirm';
import { Icon } from '../ui/Icon';
import { ChangeChips, VersionFiles } from './VersionFiles';

interface Props {
  game: Game;
  headId: string | null;
  versions: Version[];
  /** This game's open conflicts — their two sides read "Conflicting" and are never pruned. */
  conflicts: Conflict[];
  loading: boolean;
  reloadVersions: () => Promise<void>;
  onSetLatest: (v: Version) => Promise<void>;
  onRefresh: () => void;
}

type View = 'main' | 'backups';

/**
 * plan.md Phase 10.5, left column: Versions as a DataTable, with Backups — anything the game still has
 * that is not an ancestor of the current head, almost always the losing side of a past conflict —
 * behind a Seg. No new endpoint: the same list, split by walking parentVersionId back from the head
 * (tasks/conflict-resolution-ui/plan.md).
 *
 * Every row starts collapsed and already says what it changed; its chevron opens the changed files
 * (tasks/save-file-trees Phase 2, variant B). One request covers the whole list.
 */
export function VersionsCard({ game, headId, versions, conflicts, loading, reloadVersions, onSetLatest, onRefresh }: Props) {
  const [view, setView] = useState<View>('main');
  const [open, setOpen] = useState<Set<string>>(new Set());
  const [changes, setChanges] = useState<Map<string, VersionChanges>>(new Map());

  // Re-read whenever the list itself changes: a new push brings a version nobody has diffed yet.
  const listKey = versions.map(v => v.id).join(',');
  useEffect(() => {
    if (!listKey) return;
    let live = true;
    api.versionChanges(game.id)
      .then(list => { if (live) setChanges(new Map(list.map(c => [c.versionId, c]))); })
      .catch(() => { /* rows stay without chips; opening one says it is still loading */ });
    return () => { live = false; };
  }, [game.id, listKey]);

  function toggle(id: string) {
    setOpen(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }

  const mainIds = new Set<string>();
  {
    const byId = new Map(versions.map(v => [v.id, v]));
    let cur = headId;
    while (cur && !mainIds.has(cur)) { mainIds.add(cur); cur = byId.get(cur)?.parentVersionId ?? null; }
  }
  const main = versions.filter(v => mainIds.has(v.id));
  const backups = versions.filter(v => !mainIds.has(v.id));
  const shown = view === 'main' ? main : backups;
  const conflicting = new Set(conflicts.flatMap(c => [c.versionAId, c.versionBId]));

  // What "Prune" would actually remove, by the server's own rule (SyncService.PruneVersionsAsync):
  // newest first, skip the ones kept, never the head, a side of an open conflict, or a protected one.
  // Only knowable when the game has its own limit — the server-wide default is not sent to the console,
  // so then the control says what it does instead of guessing a number.
  const limit = game.retainVersions;
  const prunable = limit != null && limit > 0
    ? [...versions].sort((x, y) => toMs(y.createdAt) - toMs(x.createdAt)).slice(limit)
        .filter(v => !v.protected && v.id !== headId && !conflicting.has(v.id)).length
    : null;

  async function prune() {
    try {
      const r = await api.pruneNow(game.id);
      await reloadVersions();
      onRefresh();
      toast(r.removed > 0 ? `Removed ${plural(r.removed, 'version')}.` : 'Nothing to remove — already within the limit.');
    } catch (e) { toastError('Could not prune: ' + errorText(e)); }
  }

  async function remove(v: Version) {
    try {
      await api.deleteVersion(game.id, v.id);
      await reloadVersions();
      onRefresh();
      toast(`Deleted version ${shortId(v.id)}.`);
    } catch (e) { toastError('Could not delete the version: ' + errorText(e)); }
  }

  async function unprotect(v: Version) {
    try {
      await api.setVersionProtected(game.id, v.id, false);
      await reloadVersions();
      toast(`Version ${shortId(v.id)} is no longer protected.`);
    } catch (e) { toastError('Could not unprotect: ' + errorText(e)); }
  }

  // The escape hatch: a copy of a save taken before doing something destructive to it.
  async function download(v: Version) {
    const safe = game.name.replace(/[^\w.-]+/g, '_');
    try { await api.downloadVersion(game.id, v.id, `${safe}-${shortId(v.id)}.zip`); }
    catch (e) { toastError('Could not download: ' + errorText(e)); }
  }

  function state(v: Version) {
    if (v.id === headId) return <Chip tone="ok">Latest</Chip>;
    if (conflicting.has(v.id)) return <Chip tone="crit">Conflicting</Chip>;
    if (v.protected) return <Chip tone="warn">Protected</Chip>;
    return <Chip>{view === 'main' ? 'Kept' : 'Backup'}</Chip>;
  }

  return (
    <Card
      flush
      title="Versions"
      headerRight={<>
        <Seg size="sm" value={view} onChange={setView} aria-label="Which versions"
          options={[{ value: 'main', label: `History ${main.length}` }, { value: 'backups', label: `Backups ${backups.length}` }]} />
        <Chip>{versions.length} kept</Chip>
        <Button size="sm" variant="quiet" disabled={open.size === 0} onClick={() => setOpen(new Set())}>Collapse all</Button>
        {prunable === null
          ? <InlineConfirm
              label="Apply retention"
              title="Apply the server's default retention to this game now"
              consequence="Keeps the server's default number of newest versions and permanently deletes the older ones. The Latest, both sides of an open conflict and protected versions are never deleted."
              confirmLabel="Apply retention"
              onConfirm={prune}
            />
          : prunable === 0
            ? <Button size="sm" disabled title={`Already within Keep ${limit}`}>Nothing to prune</Button>
            : <InlineConfirm
                label={`Prune ${plural(prunable, 'version')}`}
                consequence={`Permanently deletes the ${plural(prunable, 'oldest version')} beyond the newest ${limit}. The Latest, both sides of an open conflict and protected versions are never deleted.`}
                confirmLabel={`Prune ${plural(prunable, 'version')}`}
                onConfirm={prune}
              />}
      </>}
    >
      {view === 'backups' && (
        <p className="px-4 pt-3 text-[11.5px] text-dim leading-[1.5]">
          Not part of the current save's history — nothing here was deleted, it just isn't what machines
          pull. Download one to keep a copy, or Set as Latest to bring it back.
        </p>
      )}
      {loading
        ? <p className="px-4 py-5 text-[13px] text-dim">Loading…</p>
        : <DataTable
            caption={view === 'main' ? 'Versions' : 'Backups'}
            rows={shown}
            rowKey={v => v.id}
            expanded={v => open.has(v.id)
              ? <VersionFiles id={`version-files-${v.id}`} game={game} version={v} changes={changes.get(v.id)} />
              : null}
            empty={view === 'main'
              ? <EmptyState title="No versions yet">The first push from any machine shows up here.</EmptyState>
              : <EmptyState title="No backups">A save that loses a conflict, or is ridden over by a forced push, is kept here.</EmptyState>}
            columns={[
              {
                head: 'Version', kind: ['k', 'm'],
                cell: v => (
                  <button type="button" onClick={() => toggle(v.id)}
                    aria-expanded={open.has(v.id)} aria-controls={`version-files-${v.id}`}
                    aria-label={`${open.has(v.id) ? 'Hide' : 'Show'} the files of version ${shortId(v.id)}`}
                    className="inline-flex items-center gap-1.5 cursor-pointer rounded hover:text-accent-ink">
                    <Icon name="chevron-right" size={13}
                      className={`text-faint transition-transform ${open.has(v.id) ? 'rotate-90 text-accent-ink' : ''}`} />
                    {shortId(v.id)}
                  </button>
                ),
              },
              { head: 'When', kind: 'n', cell: v => <span title={when(v.createdAt)}>{ago(v.createdAt)}</span> },
              { head: 'Machine', cell: v => v.machineName },
              { head: 'Size', kind: 'n', cell: v => fmtSize(v.size) },
              {
                head: 'State', kind: 'wrap',
                cell: v => <span className="flex gap-1 items-center flex-wrap min-w-[10.5rem]">{state(v)}<ChangeChips changes={changes.get(v.id)} /></span>,
              },
              {
                head: '', label: 'Actions', end: true,
                cell: v => (
                  <span className="inline-flex gap-1.5 justify-end flex-wrap">
                    {v.id !== headId && (
                      <InlineConfirm
                        label="Set as Latest"
                        tone="primary"
                        consequence={`Every machine that syncs ${game.name} is told to pull ${v.machineName}'s save from ${when(v.createdAt)}. One with unpushed local changes reports the pull as blocked instead of losing them.${conflicting.has(v.id) ? ' Its open conflict is resolved in its favour.' : ''}`}
                        confirmLabel={`Make ${shortId(v.id)} Latest`}
                        onConfirm={() => onSetLatest(v)}
                      />
                    )}
                    <Button size="sm" variant="quiet" onClick={() => void download(v)} title="Download this version's archive">Download</Button>
                    {v.protected && (
                      <InlineConfirm
                        label="Unprotect"
                        consequence="Retention may then delete it the next time it runs."
                        confirmLabel={`Unprotect ${shortId(v.id)}`}
                        onConfirm={() => unprotect(v)}
                      />
                    )}
                    {/* Not the Latest, and not a side of an open conflict — the server refuses both
                        (SyncService.DeleteVersionAsync); resolving the conflict frees it. */}
                    {v.id !== headId && !conflicting.has(v.id) && (
                      <InlineConfirm
                        label="Delete"
                        triggerVariant="quiet"
                        consequence="Its archive is permanently removed from the server. Download it first if you might want it."
                        confirmLabel={`Delete ${shortId(v.id)}`}
                        onConfirm={() => remove(v)}
                      />
                    )}
                  </span>
                ),
              },
            ]}
          />}
    </Card>
  );
}
