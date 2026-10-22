import { useEffect, useState } from 'react';
import { api, errorText } from '../../src/api';
import type { Conflict, Game, Version, VersionChanges } from '../../src/types';
import { ago, fmtSize, plural, shortId, toMs, when } from '../../src/format';
import { toast, toastError } from '../../src/toast';
import { Card } from '../../src/components/ui/Card';
import { Chip } from '../../src/components/ui/Chip';
import { Seg } from '../../src/components/ui/Seg';
import { Button } from '../../src/components/ui/Button';
import { DataTable } from '../../src/components/ui/DataTable';
import { EmptyState } from '../../src/components/ui/EmptyState';
import { InlineConfirm } from '../../src/components/ui/InlineConfirm';
import { Icon } from '../../src/components/ui/Icon';
import { ChangeChips, VersionFiles, type ChangesStatus } from './VersionFiles';
import { G } from '../fixtures';
import { useMock } from '../store';
import { InlineName, NameChip, NameDialog, NamedStrip, useLabels } from '../features/names';
import { KeepHistory, useRestore } from '../features/history';

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

type View = 'main' | 'backups' | 'deleted';

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
  // Mockup: names (vh-names) and the "Deleted" segment (vh-trash, B) on the demo game.
  const feature = useMock(s => s.feature);
  const mockVariant = useMock(s => s.variant);
  const trash = useMock(s => s.trash);
  const demo = game.id === G.hollow;
  const namesOn = demo && feature === 'vh-names';
  // B: the name has its own column, so the row's actions stay as they are.
  const namesB = namesOn && mockVariant === 'B';
  const deletedOn = demo && feature === 'vh-trash' && mockVariant === 'B';
  const labels = useLabels();
  const [editing, setEditing] = useState<string | null>(null);
  const [naming, setNaming] = useState<Version | null>(null);
  const keepOn = demo && feature === 'vh-keep';
  const [keeping, setKeeping] = useState(false);
  const restore = useRestore();
  const gameTrash = trash.filter(t => t.game === game.name);
  const [open, setOpen] = useState<Set<string>>(new Set());
  const [changes, setChanges] = useState<Map<string, VersionChanges>>(new Map());
  // Which list the last answer was for, and whether it failed. Once the current list's answer is in, a
  // version missing from the map is one whose archive the server could not read: no row may go on
  // saying it is still loading.
  const [answered, setAnswered] = useState<{ listKey: string; failed: boolean } | null>(null);

  // Re-read whenever the list itself changes: a new push brings a version nobody has diffed yet.
  const listKey = versions.map(v => v.id).join(',');
  const changesStatus: ChangesStatus = answered?.listKey !== listKey ? 'loading' : answered.failed ? 'failed' : 'loaded';
  useEffect(() => {
    if (!listKey) return;
    let live = true;
    api.versionChanges(game.id)
      .then(list => {
        if (live) { setChanges(new Map(list.map(c => [c.versionId, c]))); setAnswered({ listKey, failed: false }); }
      })
      .catch(() => { if (live) setAnswered({ listKey, failed: true }); });
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
    if (v.mergedFrom) return <span title={`Made as ${v.mergedFrom}, merged into this game. Read-only history: it can be downloaded, never made Latest.`}><Chip>From {v.mergedFrom}</Chip></span>;
    if (namesOn && labels[v.id]) return <Chip tone="ok">Kept · named</Chip>;
    if (v.protected) return <Chip tone="warn">Protected</Chip>;
    return <Chip>{view === 'main' ? 'Kept' : 'Backup'}</Chip>;
  }

  return (
    <Card
      flush
      title="Versions"
      headerRight={<>
        <Seg size="sm" value={view} onChange={setView} aria-label="Which versions"
          options={[{ value: 'main', label: `History ${main.length}` }, { value: 'backups', label: `Backups ${backups.length}` },
            ...(deletedOn ? [{ value: 'deleted' as View, label: `Deleted ${gameTrash.length}` }] : [])]} />
        <Chip>{versions.length} kept</Chip>
        {keepOn && <Button size="sm" onClick={() => setKeeping(true)}><Icon name="sliders-horizontal" size={13} />Keep history…</Button>}
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
      {namesOn && mockVariant === 'C' && view === 'main' && <NamedStrip list={main} labels={labels} onRename={setNaming} />}
      {view === 'deleted' && (
        <>
          <p className="px-4 pt-3 text-[11.5px] text-dim leading-[1.5]">Pruned or deleted versions of {game.name}. Each stays for 14 days, then it’s gone. All games’ trash: Storage › Trash.</p>
          <DataTable caption="Deleted versions" rows={gameTrash} rowKey={t => t.id}
            empty={<EmptyState title="Nothing deleted">Pruned and deleted versions wait here for 14 days.</EmptyState>}
            columns={[
              { head: 'Version', cell: t => <span className="line-through text-dim">{t.made}</span> },
              { head: 'Why', cell: t => t.why },
              { head: 'Gone in', cell: t => <Chip tone={t.left <= 3 ? 'warn' : 'default'}>{t.left} days</Chip> },
              { head: '', label: 'Actions', end: true, cell: t => <Button size="sm" onClick={() => restore.ask(t)}>Restore</Button> },
            ]} />
          {restore.dialog}
        </>
      )}
      {view === 'deleted' ? null : loading
        ? <p className="px-4 py-5 text-[13px] text-dim">Loading…</p>
        : <DataTable
            caption={view === 'main' ? 'Versions' : 'Backups'}
            rows={shown}
            rowKey={v => v.id}
            expanded={v => open.has(v.id)
              ? <VersionFiles id={`version-files-${v.id}`} game={game} version={v} changes={changes.get(v.id)} status={changesStatus} />
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
              ...(namesB ? [{
                head: 'Name', kind: 'wrap' as const,
                cell: (v: Version) => labels[v.id]
                  ? <button type="button" onClick={() => setNaming(v)} title="Rename" className="cursor-pointer"><NameChip text={labels[v.id]!} /></button>
                  : <button type="button" onClick={() => setNaming(v)} title="Name this save"
                      className="inline-flex items-center gap-1 rounded-full border border-dashed border-line px-2 py-0.5 text-[11px] text-faint hover:text-fg hover:border-faint cursor-pointer whitespace-nowrap">
                      <Icon name="pencil" size={10} />Name
                    </button>,
              }] : []),
              { head: 'When', kind: namesOn && !namesB ? 'wrap' : 'n', cell: v => (
                <span className="flex flex-col items-start gap-1">
                  <span title={when(v.createdAt)}>{ago(v.createdAt)}</span>
                  {namesOn && !namesB && editing !== v.id && labels[v.id] && <NameChip text={labels[v.id]!} />}
                  {namesOn && editing === v.id && <InlineName v={v} label={labels[v.id] ?? null} onDone={() => setEditing(null)} />}
                </span>
              ) },
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
                    {namesOn && mockVariant === 'A' && editing !== v.id && (
                      <Button size="sm" variant="quiet" onClick={() => setEditing(v.id)} title={labels[v.id] ? 'Rename' : 'Name this save'}>
                        <Icon name="pencil" size={12} />{labels[v.id] ? 'Rename' : 'Name'}
                      </Button>
                    )}
                    {namesOn && mockVariant === 'C' && (
                      <Button size="sm" onClick={() => setNaming(v)}>{labels[v.id] ? 'Rename' : 'Name…'}</Button>
                    )}
                    {v.id !== headId && !v.mergedFrom && (
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
      {keeping && <KeepHistory scope="game" onClose={() => setKeeping(false)} />}
      {naming && <NameDialog v={naming} label={labels[naming.id] ?? null} onClose={() => setNaming(null)} />}
    </Card>
  );
}
