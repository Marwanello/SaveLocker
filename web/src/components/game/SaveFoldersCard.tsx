import { useEffect, useRef, useState } from 'react';
import { api, errorText } from '../../api';
import type { Game, GameIntent, Machine, MachineSavePath, MachineScanCandidate, Version } from '../../types';
import { ago, when } from '../../format';
import { isTemplate, toTemplate } from '../../savePathTemplate';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { Button } from '../ui/Button';
import { PathField } from '../ui/PathField';
import { InlineConfirm } from '../ui/InlineConfirm';

interface Props {
  game: Game;
  machines: Machine[];
  paths: MachineSavePath[];
  candidates: MachineScanCandidate[];
  pathsLoaded: boolean;
  latestByMachine: Record<string, Version>;
  reloadPaths: () => Promise<void>;
  /** A notification's "Set folder" lands here; `seq` makes each request act exactly once. */
  intent?: { value: GameIntent; seq: number } | null;
  onRefresh: () => void;
}

const inputCls = `w-full min-w-0 bg-tile text-fg border border-line rounded-lg px-2.5 py-[7px] font-mono text-[11.5px]
  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1`;

/**
 * plan.md Phase 10.5: Save folders — the game's template, then per machine its folder, its last
 * upload, Push / Pull, Use as template and edit. Merges what used to be two tables (Machines, and
 * Save paths per machine). The forced Push / Pull sit behind "Force…" on each machine, each naming
 * what it overwrites: the plain ones never destroy anything, the forced ones can.
 */
export function SaveFoldersCard({ game, machines, paths, candidates, pathsLoaded, latestByMachine, reloadPaths, intent, onRefresh }: Props) {
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState('');
  const [templateOpen, setTemplateOpen] = useState(false);
  const [templateDraft, setTemplateDraft] = useState('');
  const [forceFor, setForceFor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const rowRefs = useRef<Record<string, HTMLDivElement | null>>({});

  // A notification's "Set folder": that machine's row opens for editing (the field focuses itself),
  // once the stored paths are in so it starts from what is stored — or from the scan's guess.
  const applied = useRef(0);
  useEffect(() => {
    if (!intent || intent.seq === applied.current || intent.value.kind !== 'folder' || !pathsLoaded) return;
    applied.current = intent.seq;
    const m = intent.value.machineId;
    setDraft(paths.find(p => p.machineId === m)?.savePath ?? candidates.find(c => c.machineId === m)?.suggestedPath ?? '');
    setEditing(m);
    rowRefs.current[m]?.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }, [intent, pathsLoaded, paths, candidates]);

  /** Write a path for one machine. Blank clears it. */
  async function savePath(m: Machine, value: string) {
    setBusy(true);
    try {
      if (value.trim() === '') await api.clearMachinePath(game.id, m.id);
      else await api.setMachinePath(game.id, m.id, value.trim());
      setEditing(null);
      await reloadPaths();
      toast(value.trim() === '' ? `Cleared ${m.name}'s folder.` : `Saved ${m.name}'s folder. It applies on that agent's next poll.`);
    } catch (e) { toastError('Could not save the folder: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  async function saveTemplate(value: string) {
    setBusy(true);
    try {
      await api.setSaveDir(game.id, value.trim());
      setTemplateOpen(false);
      onRefresh();
      toast(value.trim() === '' ? 'Cleared the template.' : 'Saved the template for every machine.');
    } catch (e) { toastError('Could not save the template: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  async function queue(m: Machine, type: 'Push' | 'Pull', force: boolean) {
    try {
      await api.queueCommand(m.id, game.id, type, force);
      onRefresh();
      toast(`Queued a ${force ? 'forced ' : ''}${type.toLowerCase()} on ${m.name}. It runs on that agent's next poll.`, 4000);
      setForceFor(null);
    } catch (e) { toastError(`Could not queue the ${type.toLowerCase()}: ` + errorText(e)); }
  }

  const template = game.suggestedSaveDir;
  const templated = isTemplate(template);

  return (
    <Card title="Save folders" flush>
      <div className="px-4 py-3 border-b border-row flex flex-col gap-2">
        <div className="flex items-baseline justify-between gap-2 flex-wrap">
          <span className="text-[10px] tracking-[0.1em] uppercase text-faint">{templated ? 'Template · every machine' : 'Fallback path'}</span>
          {!templateOpen && (
            <Button size="sm" variant="quiet" onClick={() => { setTemplateDraft(template ?? ''); setTemplateOpen(true); }}>Edit</Button>
          )}
        </div>
        {templateOpen ? (
          <form className="flex gap-1.5 items-center" onSubmit={e => { e.preventDefault(); void saveTemplate(templateDraft); }}>
            <input
              autoFocus value={templateDraft} onChange={e => setTemplateDraft(e.target.value)}
              onKeyDown={e => { if (e.key === 'Escape') setTemplateOpen(false); }}
              aria-label="Save folder template for every machine"
              placeholder="e.g. <winDocuments>/My Games/Example — blank clears it"
              className={inputCls}
            />
            <Button size="sm" type="submit" disabled={busy}>Save</Button>
            <Button size="sm" variant="quiet" type="button" onClick={() => setTemplateOpen(false)}>Cancel</Button>
          </form>
        ) : (
          <PathField
            path={template}
            empty="none — each machine uses its own folder"
            tone={templated ? 'safe' : 'default'}
            title={templated
              ? `${template}\nEach machine expands this against its own folders — inside the game's Proton prefix on a Steam Deck. A machine with a folder of its own keeps it.`
              : template ? `${template}\nA literal path, used only where it happens to exist. "Use as template" on a machine turns one into a path every machine can use.` : undefined}
          />
        )}
      </div>

      {machines.length === 0 && <p className="px-4 py-4 text-[13px] text-dim">No machines registered.</p>}

      {machines.map(m => {
        const stored = paths.find(p => p.machineId === m.id);
        const candidate = candidates.find(c => c.machineId === m.id);
        const last = latestByMachine[m.id];
        const asTemplate = stored ? toTemplate(stored.savePath) : null;
        return (
          <div key={m.id} ref={el => { rowRefs.current[m.id] = el; }} className="px-4 py-3 border-b border-row last:border-b-0 flex flex-col gap-2">
            <div className="flex items-baseline justify-between gap-2 flex-wrap">
              <span className="text-[13px] font-semibold text-fg">{m.name}</span>
              <span className="text-[11px] text-dim tabular-nums" title={last ? when(last.createdAt) : undefined}>
                {last ? `last upload ${ago(last.createdAt)}` : 'nothing uploaded yet'}
              </span>
            </div>

            {editing === m.id ? (
              // The label names the machine: a path typed into the wrong machine's field is exactly the
              // mistake the old detached prompt() made easy.
              <form className="flex flex-col gap-1.5" onSubmit={e => { e.preventDefault(); void savePath(m, draft); }}>
                <label htmlFor={`path-${m.id}`} className="text-[11px] text-dim">Save folder on <b className="text-fg font-semibold">{m.name}</b></label>
                <div className="flex gap-1.5 items-center">
                  <input
                    id={`path-${m.id}`} autoFocus value={draft} onChange={e => setDraft(e.target.value)}
                    onKeyDown={e => { if (e.key === 'Escape') setEditing(null); }}
                    placeholder="Leave blank to clear the stored folder"
                    className={inputCls}
                  />
                  <Button size="sm" type="submit" disabled={busy}>Save</Button>
                  <Button size="sm" variant="quiet" type="button" onClick={() => setEditing(null)}>Cancel</Button>
                </div>
              </form>
            ) : stored ? (
              <PathField path={stored.savePath} />
            ) : candidate ? (
              // The agent found this but has NOT adopted it — a human confirms here.
              <div className="flex flex-col gap-1">
                <span className="text-[11px] text-dim">Not set. {m.name}'s scan found:</span>
                <PathField path={candidate.suggestedPath} />
              </div>
            ) : (
              <PathField path={null} empty="not set" />
            )}

            {editing !== m.id && (
              <div className="flex gap-1.5 flex-wrap items-center">
                <Button size="sm" onClick={() => void queue(m, 'Push', false)}
                  title={`Upload ${m.name}'s save. If it has diverged from Latest it becomes a conflict, never an overwrite.`}>Push</Button>
                <Button size="sm" onClick={() => void queue(m, 'Pull', false)}
                  title={`Bring Latest to ${m.name}. It will not overwrite local changes that have not been pushed.`}>Pull</Button>
                {!stored && candidate && (
                  <Button size="sm" onClick={() => void savePath(m, candidate.suggestedPath)}>Use this folder</Button>
                )}
                <Button size="sm" variant="quiet" onClick={() => { setDraft(stored?.savePath ?? candidate?.suggestedPath ?? ''); setEditing(m.id); }}>
                  {stored ? 'Edit' : 'Set'}
                </Button>
                {asTemplate && asTemplate !== template && (
                  <InlineConfirm
                    label="Use as template"
                    tone="default"
                    triggerVariant="quiet"
                    title={`Describe this location generically so other machines find their own copy:\n${asTemplate}`}
                    consequence={`Every machine without a folder of its own expands ${asTemplate} against its own folders — on a Steam Deck, inside the game's Proton prefix.`}
                    confirmLabel="Set as the template"
                    onConfirm={() => saveTemplate(asTemplate)}
                  />
                )}
                <Button size="sm" variant="quiet" aria-expanded={forceFor === m.id}
                  onClick={() => setForceFor(f => (f === m.id ? null : m.id))}>
                  Force…
                </Button>
              </div>
            )}

            {forceFor === m.id && editing !== m.id && (
              <div className="animate-drop flex gap-1.5 flex-wrap items-center">
                <InlineConfirm
                  label="Force pull"
                  consequence={`Overwrites ${m.name}'s local save with Latest, including changes it has not pushed.`}
                  confirmLabel={`Overwrite ${m.name}'s save`}
                  onConfirm={() => queue(m, 'Pull', true)}
                />
                <InlineConfirm
                  label="Force push"
                  consequence={`Uploads ${m.name}'s save as Latest even if it has diverged. The other side becomes a backup instead of a conflict.`}
                  confirmLabel={`Make ${m.name}'s save Latest`}
                  onConfirm={() => queue(m, 'Push', true)}
                />
              </div>
            )}
          </div>
        );
      })}
    </Card>
  );
}
