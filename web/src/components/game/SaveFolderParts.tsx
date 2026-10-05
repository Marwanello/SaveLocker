import { useState } from 'react';
import { api, errorText } from '../../api';
import type { Game, Machine, MachineSavePath, SavePath, VersionFolder } from '../../types';
import { ago, fmtSize } from '../../format';
import { isTemplate, toTemplate } from '../../savePathTemplate';
import { toast, toastError } from '../../toast';
import { Button } from '../ui/Button';
import { GlobChips } from '../ui/GlobChips';
import { InlineConfirm } from '../ui/InlineConfirm';
import { PathField } from '../ui/PathField';

export const MAIN_KEY = 'main';

const inputCls = `w-full min-w-0 bg-tile text-fg border border-line rounded-lg px-2.5 py-[7px] font-mono text-[11.5px]
  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1`;

const eyebrow = 'text-[10px] tracking-[0.1em] uppercase text-faint';

/**
 * What one save folder holds in Latest: a count and size, and on request the file list (the first 500
 * by name). Read from the head archive by the server, so a multi-folder save is shown as its folders
 * rather than as the `.savelocker/` tree a Download of it contains.
 */
export function FolderFiles({ folder, latestAt }: { folder: VersionFolder | undefined; latestAt: string | null | undefined }) {
  const [open, setOpen] = useState(false);
  if (!folder) return null;
  const shown = folder.files.length;
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline gap-2 flex-wrap">
        <span className="text-[11px] text-dim tabular-nums">
          {folder.fileCount === 0
            ? `Latest holds this folder, empty${latestAt ? ` · ${ago(latestAt)}` : ''}`
            : `Latest holds ${folder.fileCount} file${folder.fileCount === 1 ? '' : 's'} · ${fmtSize(folder.totalBytes)}`}
        </span>
        {folder.fileCount > 0 && (
          <Button size="sm" variant="quiet" aria-expanded={open} onClick={() => setOpen(o => !o)}>
            {open ? 'Hide files' : 'Show files'}
          </Button>
        )}
      </div>
      {open && (
        <ul className="animate-drop max-h-56 overflow-auto rounded-lg border border-row bg-tile px-2.5 py-1.5 flex flex-col gap-0.5">
          {folder.files.map(f => (
            <li key={f.path} className="flex justify-between gap-3 font-mono text-[11px] text-dim">
              <span className="truncate" title={f.path}>{f.path}</span>
              <span className="tabular-nums shrink-0">{fmtSize(f.size)}</span>
            </li>
          ))}
          {folder.fileCount > shown && (
            <li className="text-[11px] text-faint">…and {folder.fileCount - shown} more</li>
          )}
        </ul>
      )}
    </div>
  );
}

/**
 * One save folder's include scope — "only these files of the folder are the game's" — the same chips
 * the exclude editor uses. Identical on every machine; an empty list means the whole folder.
 */
export function IncludePatterns({ game, folderKey, saved, onRefresh }: {
  game: Game; folderKey: string; saved: string[]; onRefresh: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<string[]>(saved);
  const [busy, setBusy] = useState(false);
  const dirty = JSON.stringify(draft) !== JSON.stringify(saved);

  async function save() {
    setBusy(true);
    try {
      await api.setIncludeGlobs(game.id, draft, folderKey === MAIN_KEY ? undefined : folderKey);
      onRefresh();
      setOpen(false);
      toast(draft.length === 0 ? 'The whole folder syncs again.' : 'Saved. Agents apply it from their next sync.');
    } catch (e) { toastError('Could not save the patterns: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  if (!open) {
    return (
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-[11px] text-dim">
          {saved.length === 0 ? 'Every file in the folder syncs.' : <>Only <span className="font-mono">{saved.join(', ')}</span> sync.</>}
        </span>
        <Button size="sm" variant="quiet" onClick={() => { setDraft(saved); setOpen(true); }}>
          {saved.length === 0 ? 'Limit to some files…' : 'Edit'}
        </Button>
      </div>
    );
  }
  return (
    <div className="animate-drop flex flex-col gap-2">
      <span className="text-[11px] text-dim">
        Only files matching these sync — relative to the folder, e.g. <code className="font-mono">*.sav</code> or{' '}
        <code className="font-mono">slot*/**</code>. None: the whole folder.
      </span>
      <GlobChips patterns={draft} onChange={setDraft} addLabel={`Add an include pattern for ${folderKey} (Enter adds it)`} />
      <div className="flex gap-1.5">
        <Button size="sm" disabled={busy || !dirty} onClick={() => void save()}>{busy ? 'Saving…' : 'Save'}</Button>
        <Button size="sm" variant="quiet" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </div>
  );
}

/** A folder's template for every machine, with an editor. Blank clears it. */
function TemplateRow({ game, folderKey, template, onRefresh }: {
  game: Game; folderKey: string; template: string | null | undefined; onRefresh: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false);
  const templated = isTemplate(template);

  async function save(value: string) {
    setBusy(true);
    try {
      await api.setSaveDir(game.id, value.trim(), folderKey);
      setOpen(false);
      onRefresh();
      toast(value.trim() === '' ? 'Cleared the template.' : 'Saved the template for every machine.');
    } catch (e) { toastError('Could not save the template: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between gap-2 flex-wrap">
        <span className={eyebrow}>{templated ? 'Template · every machine' : 'Fallback path'}</span>
        {!open && <Button size="sm" variant="quiet" onClick={() => { setDraft(template ?? ''); setOpen(true); }}>Edit</Button>}
      </div>
      {open ? (
        <form className="flex gap-1.5 items-center" onSubmit={e => { e.preventDefault(); void save(draft); }}>
          <input
            autoFocus value={draft} onChange={e => setDraft(e.target.value)}
            onKeyDown={e => { if (e.key === 'Escape') setOpen(false); }}
            aria-label={`Template of the ${folderKey} folder for every machine`}
            placeholder="e.g. <winLocalAppData>/Example/Saves — blank clears it"
            className={inputCls}
          />
          <Button size="sm" type="submit" disabled={busy}>Save</Button>
          <Button size="sm" variant="quiet" type="button" onClick={() => setOpen(false)}>Cancel</Button>
        </form>
      ) : (
        <PathField path={template} empty="none — machines without a folder of their own keep a copy" tone={templated ? 'safe' : 'default'} />
      )}
    </div>
  );
}

/**
 * One of a game's extra save folders (tasks/multiple-save-paths): its template, include scope, each
 * machine's folder for it and what Latest holds. A machine with no folder for it keeps a copy inside the
 * agent's own state, so the folder still travels through it — "not on this machine" is not an error.
 */
export function ExtraFolderSection({ game, sp, machines, paths, folder, latestAt, onPathsChanged, onRefresh }: {
  game: Game; sp: SavePath; machines: Machine[]; paths: MachineSavePath[];
  folder: VersionFolder | undefined; latestAt: string | null | undefined;
  onPathsChanged: () => Promise<void>; onRefresh: () => void;
}) {
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false);
  const mine = paths.filter(p => p.pathKey === sp.key);

  async function savePath(m: Machine, value: string) {
    setBusy(true);
    try {
      if (value.trim() === '') await api.clearMachinePath(game.id, m.id, sp.key);
      else await api.setMachinePath(game.id, m.id, value.trim(), sp.key);
      setEditing(null);
      await onPathsChanged();
      toast(value.trim() === '' ? `Cleared ${m.name}'s ${sp.key} folder.` : `Saved ${m.name}'s ${sp.key} folder. It applies on that agent's next poll.`);
    } catch (e) { toastError('Could not save the folder: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  async function remove() {
    try {
      await api.removeSavePath(game.id, sp.key);
      onRefresh();
      await onPathsChanged();
      toast(`“${sp.label || sp.key}” no longer syncs. Files on each machine stay where they are.`);
    } catch (e) { toastError('Could not remove the folder: ' + errorText(e)); }
  }

  return (
    <section aria-label={`Save folder ${sp.key}`} className="border-t border-line">
      <div className="px-4 py-3 border-b border-row flex flex-col gap-2.5">
        <div className="flex items-baseline justify-between gap-2 flex-wrap">
          <span className="text-[13px] font-semibold text-fg">
            {sp.label || sp.key}
            {sp.label && <span className="ml-2 font-mono text-[11px] font-normal text-dim">{sp.key}</span>}
          </span>
          <InlineConfirm
            label="Remove…"
            triggerVariant="quiet"
            consequence={`Stops syncing ${sp.label || sp.key} on every machine. Each machine's files stay where they are, and stored versions still hold it. The key ${sp.key} cannot be reused.`}
            confirmLabel={`Stop syncing ${sp.label || sp.key}`}
            onConfirm={remove}
          />
        </div>
        <TemplateRow game={game} folderKey={sp.key} template={sp.template} onRefresh={onRefresh} />
        <IncludePatterns game={game} folderKey={sp.key} saved={sp.includeGlobs ?? []} onRefresh={onRefresh} />
        <FolderFiles folder={folder} latestAt={latestAt} />
      </div>

      {machines.map(m => {
        const stored = mine.find(p => p.machineId === m.id);
        const asTemplate = stored ? toTemplate(stored.savePath) : null;
        return (
          <div key={m.id} className="px-4 py-2.5 border-b border-row last:border-b-0 flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-2 flex-wrap">
              <span className="text-[12.5px] font-semibold text-fg">{m.name}</span>
              {editing !== m.id && (
                <div className="flex gap-1.5">
                  <Button size="sm" variant="quiet" onClick={() => { setDraft(stored?.savePath ?? ''); setEditing(m.id); }}>
                    {stored ? 'Edit' : 'Set'}
                  </Button>
                  {asTemplate && asTemplate !== sp.template && (
                    <InlineConfirm
                      label="Use as template"
                      tone="default"
                      triggerVariant="quiet"
                      consequence={`Every machine without a ${sp.key} folder of its own expands ${asTemplate} against its own folders.`}
                      confirmLabel="Set as the template"
                      onConfirm={async () => {
                        try { await api.setSaveDir(game.id, asTemplate, sp.key); onRefresh(); toast('Saved the template for every machine.'); }
                        catch (e) { toastError('Could not save the template: ' + errorText(e)); }
                      }}
                    />
                  )}
                </div>
              )}
            </div>
            {editing === m.id ? (
              <form className="flex gap-1.5 items-center" onSubmit={e => { e.preventDefault(); void savePath(m, draft); }}>
                <input
                  autoFocus value={draft} onChange={e => setDraft(e.target.value)}
                  onKeyDown={e => { if (e.key === 'Escape') setEditing(null); }}
                  aria-label={`${sp.key} folder on ${m.name}`}
                  placeholder="Leave blank to clear the stored folder"
                  className={inputCls}
                />
                <Button size="sm" type="submit" disabled={busy}>Save</Button>
                <Button size="sm" variant="quiet" type="button" onClick={() => setEditing(null)}>Cancel</Button>
              </form>
            ) : stored ? (
              <PathField path={stored.savePath} />
            ) : (
              <PathField path={null} empty="not on this machine — its agent keeps a copy so the folder still syncs" />
            )}
          </div>
        );
      })}
    </section>
  );
}

/** A key the way the server wants one: 1–32 lower-case letters, digits or "-". */
function slug(text: string): string {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32).replace(/-+$/, '');
}

/** "Add a save folder": a key every machine agrees on, an optional label, template and include scope. */
export function AddFolderForm({ game, onAdded }: { game: Game; onAdded: () => void }) {
  const [open, setOpen] = useState(false);
  const [key, setKey] = useState('');
  const [label, setLabel] = useState('');
  const [template, setTemplate] = useState('');
  const [include, setInclude] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  async function add() {
    setBusy(true);
    try {
      await api.addSavePath(game.id, {
        key: key.trim(), label: label.trim() || null, template: template.trim() || null,
        includeGlobs: include.length > 0 ? include : null,
      });
      setOpen(false);
      setKey(''); setLabel(''); setTemplate(''); setInclude([]);
      onAdded();
      toast(template.trim()
        ? 'Added. Every machine where the template names a folder syncs it from its next poll.'
        : 'Added. Set each machine’s folder for it below; until then their agents keep a copy.');
    } catch (e) { toastError('Could not add the folder: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  if (!open) {
    return (
      <div className="px-4 py-3 border-t border-line flex items-center gap-2 flex-wrap">
        <Button size="sm" onClick={() => setOpen(true)}>Add a save folder</Button>
        <span className="text-[11px] text-dim">For a game that keeps saves in more than one place.</span>
      </div>
    );
  }
  return (
    <form className="animate-drop px-4 py-3 border-t border-line flex flex-col gap-2" onSubmit={e => { e.preventDefault(); void add(); }}>
      <span className={eyebrow}>New save folder</span>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-1.5">
        <input value={label} onChange={e => { setLabel(e.target.value); if (!key || key === slug(label)) setKey(slug(e.target.value)); }}
          placeholder="Label, e.g. Save states" aria-label="Label" className={inputCls} />
        <input value={key} onChange={e => setKey(e.target.value)} required
          placeholder="key, e.g. states" aria-label="Key (cannot be changed later)" className={inputCls} />
      </div>
      <input value={template} onChange={e => setTemplate(e.target.value)}
        placeholder="Template, e.g. <winLocalAppData>/Example/States (optional)" aria-label="Template for every machine" className={inputCls} />
      <GlobChips patterns={include} onChange={setInclude} addLabel="Add an include pattern for the new folder (Enter adds it)" />
      <span className="text-[11px] text-dim">
        The key names the folder on every machine and inside each save, so it cannot be changed or reused later.
      </span>
      <div className="flex gap-1.5">
        <Button size="sm" type="submit" disabled={busy || slug(key) !== key || key === '' || key === MAIN_KEY}>{busy ? 'Adding…' : 'Add folder'}</Button>
        <Button size="sm" variant="quiet" type="button" onClick={() => setOpen(false)}>Cancel</Button>
      </div>
    </form>
  );
}
