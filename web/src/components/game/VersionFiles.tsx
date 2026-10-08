import { useEffect, useState, type ReactNode } from 'react';
import { api, errorText } from '../../api';
import type { Game, Version, VersionChanges, VersionFolder } from '../../types';
import { fmtSize, plural } from '../../format';
import { Button } from '../ui/Button';
import { Chip } from '../ui/Chip';
import { Icon } from '../ui/Icon';
import { MAIN_KEY } from './SaveFolderParts';

type Change = 'Added' | 'Changed' | 'Removed';

export type ChangesStatus = 'loading' | 'loaded' | 'failed';

interface FileRow { path: string; size: number; change?: Change }

const BADGE: Record<Change, { label: string; tone: 'ok' | 'warn' | 'crit' }> = {
  Added: { label: 'Added', tone: 'ok' },
  Changed: { label: 'Changed', tone: 'warn' },
  Removed: { label: 'Removed', tone: 'crit' },
};

/** A version's change counts as chips: "1 changed", "2 added", "1 removed". Nothing while still loading. */
export function ChangeChips({ changes }: { changes: VersionChanges | undefined }) {
  if (!changes) return null;
  if (changes.baseMissing) return <Chip>Earlier version pruned</Chip>;
  const { added, changed, removed } = changes;
  if (added + changed + removed === 0) return <Chip>No file changes</Chip>;
  return (
    <>
      {changed > 0 && <Chip tone="warn">{changed} changed</Chip>}
      {added > 0 && <Chip tone="ok">{added} added</Chip>}
      {removed > 0 && <Chip tone="crit">{removed} removed</Chip>}
    </>
  );
}

/**
 * One opened row of the Versions card (tasks/save-file-trees Phase 2, variant B): only what changed
 * since the version before, as a tree per save folder, and the whole version one click away. The full
 * listing comes from `versions/{id}/folders`, read only when asked for.
 */
export function VersionFiles({ id, game, version, changes, status }: {
  id: string; game: Game; version: Version; changes: VersionChanges | undefined; status: ChangesStatus;
}) {
  const [all, setAll] = useState(false);
  const [folders, setFolders] = useState<VersionFolder[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!all || folders) return;
    let live = true;
    api.versionFolders(game.id, version.id)
      .then(f => { if (live) setFolders(f); })
      .catch(e => { if (live) setError(errorText(e)); });
    return () => { live = false; };
  }, [all, folders, game.id, version.id]);

  const listed = changes?.files ?? [];
  const total = changes ? changes.added + changes.changed + changes.removed : 0;
  const changeOf = new Map(listed.map(f => [`${f.key}\n${f.path}`, f.change as Change]));

  // The folders to draw and what each holds in the mode shown: the changed files, or every file plus
  // the removed ones (they are not in this version, but they are what it changed).
  let blocks: { key: string; files: FileRow[]; note?: string }[];
  if (!all) {
    const keys = [...new Set(listed.map(f => f.key))];
    blocks = keys.map(key => ({
      key,
      files: listed.filter(f => f.key === key).map(f => ({ path: f.path, size: f.size, change: f.change as Change })),
    }));
  } else {
    // A folder this version no longer has at all is still where its removed files were.
    const gone = [...new Set(listed.filter(c => c.change === 'Removed').map(c => c.key))]
      .filter(key => folders && !folders.some(f => f.key === key))
      .map(key => ({ key, fileCount: 0, files: [] }));
    blocks = [...(folders ?? []), ...gone].map(f => ({
      key: f.key,
      files: [
        ...f.files.map(x => ({ path: x.path, size: x.size, change: changeOf.get(`${f.key}\n${x.path}`) })),
        ...listed.filter(c => c.key === f.key && c.change === 'Removed').map(c => ({ path: c.path, size: c.size, change: 'Removed' as Change })),
      ],
      note: f.fileCount > f.files.length ? `Showing the first ${f.files.length} of ${f.fileCount} files in ${folderLabel(game, f.key)}.` : undefined,
    }));
  }

  let lead: string;
  if (all) lead = 'Every file in this version';
  else if (!changes) lead = status === 'loading' ? 'Loading what changed…' : 'Could not read what this version changed';
  else if (changes.baseMissing) lead = 'The version this was pushed on top of is gone, so every file shows as added';
  else if (!version.parentVersionId) lead = 'The first version: every file is new';
  else if (total === 0) lead = 'No file changed since the version before';
  else lead = 'What changed since the version before';

  return (
    <div id={id} className="sticky left-0 w-[100cqw] px-4 pt-1 pb-3.5 sm:pl-12 flex flex-col gap-2.5">
      <div className="flex items-center justify-between gap-2 flex-wrap text-[12px] text-dim">
        <span>{lead}</span>
        {(changes ? all || changes.unchanged > 0 : status !== 'loading') && (
          <Button size="sm" onClick={() => setAll(a => !a)}>
            {all ? 'Show changes only' : changes ? `Show all files (${changes.unchanged} unchanged)` : 'Show all files'}
          </Button>
        )}
      </div>
      {!all && total > listed.length && (
        <p className="text-[11px] text-faint">Showing the first {listed.length} of {plural(total, 'changed file')}.</p>
      )}
      {all && !folders && !error && <p className="text-[12px] text-dim">Loading the files…</p>}
      {all && error && <p className="text-[12px] text-accent-ink">Could not load the files: {error}</p>}
      {blocks.map(b => (
        <section key={b.key} aria-label={`Save folder ${folderLabel(game, b.key)}`} className="flex flex-col gap-0.5 min-w-0">
          <div className="flex items-baseline gap-2 flex-wrap px-1.5 pt-1">
            <Icon name="folder" size={13} className="text-faint self-center" />
            <span className="text-[12.5px] font-semibold text-fg">{folderLabel(game, b.key)}</span>
            {folderTemplate(game, b.key) && (
              <span className="font-mono text-[11px] text-faint [overflow-wrap:anywhere] min-w-0">{folderTemplate(game, b.key)}</span>
            )}
          </div>
          {b.files.length === 0
            ? <p className="px-1.5 text-[11.5px] text-faint">Empty in this version.</p>
            : <FileTree files={b.files} />}
          {b.note && <p className="px-1.5 text-[11px] text-faint">{b.note}</p>}
        </section>
      ))}
    </div>
  );
}

function folderLabel(game: Game, key: string): string {
  if (key === MAIN_KEY) return 'Save folder';
  return game.extraPaths?.find(p => p.key === key)?.label || key;
}

function folderTemplate(game: Game, key: string): string | null | undefined {
  return key === MAIN_KEY ? game.suggestedSaveDir : game.extraPaths?.find(p => p.key === key)?.template;
}

interface Dir { dirs: Map<string, Dir>; files: (FileRow & { name: string })[] }

function nest(files: FileRow[]): Dir {
  const root: Dir = { dirs: new Map(), files: [] };
  for (const f of files) {
    const parts = f.path.split('/');
    let node = root;
    for (const p of parts.slice(0, -1)) {
      let next = node.dirs.get(p);
      if (!next) node.dirs.set(p, next = { dirs: new Map(), files: [] });
      node = next;
    }
    node.files.push({ ...f, name: parts[parts.length - 1] });
  }
  return root;
}

/** Nested paths as folders that fold, open by default, then files with their change and size. */
function FileTree({ files }: { files: FileRow[] }) {
  return <TreeLevel node={nest(files)} prefix="" top />;
}

function TreeLevel({ node, prefix, top }: { node: Dir; prefix: string; top?: boolean }) {
  const [closed, setClosed] = useState<Set<string>>(new Set());
  const toggle = (k: string) => setClosed(s => { const n = new Set(s); if (n.has(k)) n.delete(k); else n.add(k); return n; });
  const dirs = [...node.dirs.entries()].sort(([a], [b]) => a.localeCompare(b));
  const files = [...node.files].sort((a, b) => a.name.localeCompare(b.name));
  return (
    <ul className={top ? 'text-[12.5px]' : 'pl-[18px] ml-[7px] border-l border-line'}>
      {dirs.map(([name, sub]) => {
        const k = prefix + name;
        const open = !closed.has(k);
        return (
          <li key={'d:' + k}>
            <Node>
              <button type="button" onClick={() => toggle(k)} aria-expanded={open}
                aria-label={`${open ? 'Fold' : 'Unfold'} ${name}`}
                className="w-3.5 shrink-0 text-faint hover:text-fg cursor-pointer">
                <Icon name="chevron-right" size={12} className={open ? 'rotate-90 transition-transform' : 'transition-transform'} />
              </button>
              <Icon name="folder" size={13} className="text-faint shrink-0" />
              <span className="flex-1 min-w-0 font-semibold [overflow-wrap:anywhere]">{name}/</span>
            </Node>
            {open && <TreeLevel node={sub} prefix={k + '/'} />}
          </li>
        );
      })}
      {files.map(f => (
        <li key={'f:' + prefix + f.name}>
          <Node>
            <span className="w-3.5 shrink-0" />
            <Icon name="file" size={13} className="text-faint shrink-0" />
            <span className={`flex-1 min-w-0 font-mono text-[11.5px] [overflow-wrap:anywhere] ${f.change === 'Removed' ? 'line-through text-faint' : 'text-fg'}`}>
              {f.name}
            </span>
            {f.change && <Chip tone={BADGE[f.change].tone}>{BADGE[f.change].label}</Chip>}
            <span className="font-mono text-[11px] text-dim tabular-nums whitespace-nowrap">{fmtSize(f.size)}</span>
          </Node>
        </li>
      ))}
    </ul>
  );
}

function Node({ children }: { children: ReactNode }) {
  return <div className="flex items-center gap-2 px-1.5 py-1 rounded-md hover:bg-hover min-w-0">{children}</div>;
}
