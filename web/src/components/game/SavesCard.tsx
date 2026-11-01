import { useCallback, useEffect, useState } from 'react';
import { useTreeWord } from './treeContext';
import { api, errorText } from '../../api';
import type { AgentHealth, Conflict, Game, Machine, Playthrough, Version, VersionChanges } from '../../types';
import { ago, fmtSize, plural, shortId, toMs, when } from '../../format';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { Chip } from '../ui/Chip';
import { Button } from '../ui/Button';
import { DataTable } from '../ui/DataTable';
import { EmptyState } from '../ui/EmptyState';
import { InlineConfirm } from '../ui/InlineConfirm';
import { Icon } from '../ui/Icon';
import { Select } from '../ui/Select';
import { MachineSelect } from './MachineSelect';
import { ChangeChips, VersionFiles, type ChangesStatus } from './VersionFiles';

interface Props {
  game: Game;
  machines: Machine[];
  health: AgentHealth[];
  /** Every playthrough's versions (GameDetail keeps them current as pushes land); each tab shows its own. */
  versions: Version[];
  loadingVersions: boolean;
  reloadVersions: () => Promise<void>;
  /** This game's open conflicts — their two sides read "Conflicting" and are never pruned. */
  conflicts: Conflict[];
  onRefresh: () => void;
  /** Inside a title: copy a version to another emulator's tree (tasks/multi-emulator-games Phase 6). */
  onCopy?: (v: Version) => void;
}

type Panel = { kind: 'new'; fromVersion?: Version } | { kind: 'switch' } | { kind: 'rename' } | null;
type View = 'main' | 'backups';

/**
 * tasks/save-playthroughs Phase 7, chosen variant E (`mockup-features/features/playthroughs.tsx`): the
 * Versions card grown into "Saves" — a tab per playthrough, the devices that play it, and its own
 * versions underneath with the Versions card's row actions. Every game has at least Main.
 */
export function SavesCard({ game, machines, health, versions, loadingVersions, reloadVersions, conflicts, onRefresh, onCopy }: Props) {
  const tw = useTreeWord();
  const only = tw ? ` Only the${tw} save changes; the other emulators’ are not touched.` : '';

  const [playthroughs, setPlaythroughs] = useState<Playthrough[]>([]);
  const [loadingPts, setLoadingPts] = useState(true);
  const [showArchived, setShowArchived] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [panel, setPanel] = useState<Panel>(null);
  const [dir, setDir] = useState<'left' | 'right'>('right');

  // `select` (a playthrough just created) wins over the open tab; a tab that no longer exists falls back to the default.
  const reloadPlaythroughs = useCallback(async (select?: string) => {
    try {
      const list = await api.playthroughs(game.id);
      setPlaythroughs(list);
      setSelected(cur => [select, cur].find(id => id && list.some(p => p.id === id)) ?? null);
    } catch (e) { toastError('Could not load playthroughs: ' + errorText(e)); }
  }, [game.id]);

  // Re-read whenever a version arrives or goes: each playthrough's head, counts and devices move with them.
  const versionsKey = versions.map(v => v.id).join(',');
  useEffect(() => { void reloadPlaythroughs().finally(() => setLoadingPts(false)); }, [reloadPlaythroughs, versionsKey]);

  const many = playthroughs.length > 1;
  const shown = playthroughs.filter(p => showArchived || !p.archivedAt);
  // Not `playthroughs.find`: archiving the open tab with "Show archived" off must not leave it showing without its tab.
  const current = shown.find(p => p.id === selected) ?? shown.find(p => p.isDefault) ?? shown[0] ?? null;
  const heads = new Set(playthroughs.flatMap(p => p.head ? [p.head.id] : []));
  const nameOf = (id: string | null | undefined) => playthroughs.find(p => p.id === id)?.name ?? 'another playthrough';

  // Direction follows the tab bar's left-to-right order, not click history: picking a tab to the right
  // of the open one slides the new pane in from the right, and the other way for one to its left.
  function switchTo(id: string) {
    const from = shown.findIndex(p => p.id === current?.id);
    const to = shown.findIndex(p => p.id === id);
    setDir(to >= from ? 'right' : 'left');
    setSelected(id);
    setPanel(null);
  }

  return (
    <Card
      flush
      title="Saves"
      headerRight={<>
        {many && <label className="flex items-center gap-1.5 text-[11.5px] text-dim cursor-pointer">
          <input type="checkbox" className="accent-[var(--color-accent)]" checked={showArchived} onChange={e => setShowArchived(e.target.checked)} />Show archived
        </label>}
        <Button size="sm" onClick={() => setPanel({ kind: 'new' })}><Icon name="plus" size={13} />New playthrough</Button>
      </>}
    >
      {panel?.kind === 'new' && (
        <NewPlaythroughPanel
          game={game} playthroughs={playthroughs} versions={versions} current={current} fromVersion={panel.fromVersion}
          onClose={() => setPanel(null)}
          onCreated={async id => { await reloadPlaythroughs(id); setPanel(null); onRefresh(); }}
        />
      )}
      {loadingPts
        ? <p className="px-4 py-5 text-[13px] text-dim">Loading…</p>
        : !current
          ? <p className="px-4 py-5 text-[13px] text-dim">No playthrough yet.</p>
          : <>
              <div className="flex gap-1 px-3 pt-2 border-b border-line overflow-x-auto" role="tablist" aria-label="Playthroughs">
                {shown.map(p => (
                  <button key={p.id} type="button" role="tab" aria-selected={p.id === current.id}
                    onClick={() => switchTo(p.id)}
                    className={`flex items-center gap-1 px-3.5 pt-2 pb-2.5 -mb-px border-b-2 rounded-t-lg cursor-pointer whitespace-nowrap text-[13px] font-semibold ${p.id === current.id ? 'border-accent text-fg' : 'border-transparent text-dim hover:text-fg hover:bg-hover'}`}>
                    {p.name}{p.isDefault && <Chip>Default</Chip>}{p.archivedAt && <Chip>Archived</Chip>}
                  </button>
                ))}
              </div>
              <div key={current.id} className={dir === 'right' ? 'animate-pane-right' : 'animate-pane-left'}>
                <PlaythroughHead
                  game={game} pt={current} panel={panel} onPanel={setPanel}
                  onChanged={async () => { await reloadPlaythroughs(); onRefresh(); }}
                />
                {panel?.kind === 'switch' && !current.archivedAt && (
                  <SwitchDevicePanel
                    game={game} pt={current} machines={machines} health={health}
                    onClose={() => setPanel(null)} onRefresh={onRefresh}
                  />
                )}
                {panel?.kind === 'rename' && (
                  <RenamePanel game={game} pt={current}
                    onClose={() => setPanel(null)}
                    onRenamed={async () => { await reloadPlaythroughs(); setPanel(null); }}
                  />
                )}
                <PlaythroughVersions
                  game={game} pt={current} versions={versions} loading={loadingVersions} heads={heads}
                  nameOf={nameOf} conflicts={conflicts} onCopy={onCopy} only={only}
                  onNewFromVersion={v => setPanel({ kind: 'new', fromVersion: v })}
                  reload={reloadVersions}
                  onChanged={() => { void reloadPlaythroughs(); onRefresh(); }}
                />
              </div>
            </>}
    </Card>
  );
}

/** Devices and this playthrough's own actions — its name and Default/Archived badges are the open tab. */
function PlaythroughHead({ game, pt, panel, onPanel, onChanged }: {
  game: Game; pt: Playthrough; panel: Panel; onPanel: (p: Panel) => void; onChanged: () => void;
}) {
  async function setDefault() {
    try { await api.setDefaultPlaythrough(game.id, pt.id); onChanged(); toast(`New devices now start on ${pt.name}.`); }
    catch (e) { toastError('Could not make it the default: ' + errorText(e)); }
  }
  async function toggleArchived() {
    try { await api.setPlaythroughArchived(game.id, pt.id, !pt.archivedAt); onChanged(); toast(pt.archivedAt ? 'Unarchived.' : 'Archived.'); }
    catch (e) { toastError(`Could not ${pt.archivedAt ? 'unarchive' : 'archive'}: ` + errorText(e)); }
  }
  return (
    <div className="flex items-start justify-between gap-3 flex-wrap px-4 py-3 border-b border-line">
      <div className="min-w-0 flex flex-col gap-1.5">
        {pt.hasOpenConflict && <div><Chip tone="crit">Conflicting</Chip></div>}
        <span className="flex items-center gap-1.5 flex-wrap text-[11.5px] text-dim">
          Played on {pt.devices.length
            ? <span className="flex gap-1 flex-wrap">{pt.devices.map(d => <Chip key={d.machineId} tone="ok"><Icon name="monitor" size={11} />{d.machineName}</Chip>)}</span>
            : <span className="text-faint">No device</span>}
        </span>
      </div>
      <span className="flex gap-1.5 flex-wrap items-center">
        {!pt.archivedAt && <Button size="sm" variant="primary" onClick={() => onPanel(panel?.kind === 'switch' ? null : { kind: 'switch' })}>Switch a device to this…</Button>}
        <Button size="sm" variant="quiet" onClick={() => onPanel(panel?.kind === 'rename' ? null : { kind: 'rename' })}>Rename</Button>
        {!pt.isDefault && !pt.archivedAt && <Button size="sm" variant="quiet" onClick={() => void setDefault()}>Make default</Button>}
        {!pt.isDefault && (
          <InlineConfirm
            label={pt.archivedAt ? 'Unarchive' : 'Archive'}
            triggerVariant="quiet"
            consequence={pt.archivedAt
              ? `${pt.name} is shown again and can be switched to.`
              : `${pt.name} is hidden, with no time limit. Its saves are kept; unarchive it to use it again.`}
            confirmLabel={pt.archivedAt ? `Unarchive ${pt.name}` : `Archive ${pt.name}`}
            onConfirm={toggleArchived}
          />
        )}
      </span>
    </div>
  );
}

function RenamePanel({ game, pt, onClose, onRenamed }: { game: Game; pt: Playthrough; onClose: () => void; onRenamed: () => void }) {
  const [name, setName] = useState(pt.name);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  async function submit() {
    if (!name.trim() || name.trim() === pt.name) { onClose(); return; }
    setBusy(true);
    setError('');
    try { await api.renamePlaythrough(game.id, pt.id, name.trim()); onRenamed(); toast('Renamed.'); }
    catch (e) { setError(errorText(e)); }
    finally { setBusy(false); }
  }

  return (
    <div className="flex items-center gap-2.5 px-4 py-3 border-b border-line bg-raise flex-wrap">
      <input autoFocus value={name} maxLength={60} onChange={e => setName(e.target.value)}
        onKeyDown={e => { if (e.key === 'Escape') onClose(); if (e.key === 'Enter') void submit(); }}
        aria-label="Playthrough name"
        className="px-3 py-1.5 bg-panel text-fg border border-line rounded-md text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent" />
      <Button size="sm" variant="primary" disabled={busy || !name.trim()} onClick={() => void submit()}>Rename</Button>
      <Button size="sm" variant="quiet" onClick={onClose}>Cancel</Button>
      {error && <span role="alert" className="text-[11.5px] text-accent-ink">{error}</span>}
    </div>
  );
}

/** "Switch a device to this…": the console only queues the command (`AgentCommandType.SwitchPlaythrough`);
 *  the device runs the switch itself and the result shows up in the command history below. */
function SwitchDevicePanel({ game, pt, machines, health, onClose, onRefresh }: {
  game: Game; pt: Playthrough; machines: Machine[]; health: AgentHealth[]; onClose: () => void; onRefresh: () => void;
}) {
  const onIt = new Set(pt.devices.map(d => d.machineId));
  const [machineId, setMachineId] = useState<string | null>(machines.find(m => !onIt.has(m.id))?.id ?? machines[0]?.id ?? null);
  const [busy, setBusy] = useState(false);

  async function send() {
    if (!machineId) return;
    setBusy(true);
    try {
      await api.switchDevicePlaythrough(machineId, game.id, pt.id);
      const name = machines.find(m => m.id === machineId)?.name ?? 'the device';
      toast(`Queued the switch to ${pt.name} on ${name}. It runs on that agent's next poll, pushing its own save first.`, 5000);
      onRefresh();
      onClose();
    } catch (e) { toastError('Could not queue the switch: ' + errorText(e)); }
    finally { setBusy(false); }
  }

  return (
    <div className="flex items-center gap-2.5 px-4 py-3 border-b border-line bg-raise flex-wrap">
      <MachineSelect machines={machines} health={health} value={machineId} onChange={setMachineId}
        label="Device" placeholder="Choose a device" variant="field" />
      <Button size="sm" variant="primary" disabled={busy || !machineId || onIt.has(machineId)} onClick={() => void send()}>
        Switch {machines.find(m => m.id === machineId)?.name ?? 'device'} to {pt.name}
      </Button>
      <Button size="sm" variant="quiet" onClick={onClose}>Cancel</Button>
      {machineId && onIt.has(machineId) && <span className="text-[11.5px] text-dim">Already plays {pt.name}.</span>}
    </div>
  );
}

/**
 * Name, then the playthrough and version to start from (preset when opened via Fork, otherwise the open
 * tab), shared by both "Current save" and "Start fresh" below them. mockup-features/features/playthroughs.tsx
 * (this task's mockup): a popup, not an inline panel — the one deliberate exception to plan.md's "No modals".
 */
function NewPlaythroughPanel({ game, playthroughs, versions, current, fromVersion, onClose, onCreated }: {
  game: Game; playthroughs: Playthrough[]; versions: Version[]; current: Playthrough | null; fromVersion?: Version;
  onClose: () => void; onCreated: (id: string) => void;
}) {
  // An archived playthrough is offered only when Fork came from one of its versions: without it there, the
  // version picker would have nothing to hold that version and Create would quietly fork another line's latest.
  const live = playthroughs.filter(p => !p.archivedAt || p.id === fromVersion?.playthroughId);
  // Fork's own playthrough; else the open tab, then the default, then whatever's first.
  const fallback = (live.find(p => p.id === current?.id && !p.archivedAt) ?? live.find(p => p.isDefault) ?? live[0])?.id ?? '';
  const initialFrom = fromVersion ? (live.find(p => p.id === fromVersion.playthroughId)?.id ?? fallback) : fallback;
  const initialFromPt = playthroughs.find(p => p.id === initialFrom) ?? null;
  // "Latest" when Fork's version is already that playthrough's head, else its own id.
  const initialVersionId = fromVersion && initialFromPt?.head?.id !== fromVersion.id ? fromVersion.id : 'latest';

  const [name, setName] = useState('');
  const [how, setHow] = useState<'current' | 'fresh'>('current');
  const [from, setFrom] = useState(initialFrom);
  const [fromVersionId, setFromVersionId] = useState(initialVersionId);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    // A Select inside closes its own list on Escape (and marks the event handled); only an unhandled one closes this.
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !e.defaultPrevented && !busy) onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onClose]);

  const fromPt = playthroughs.find(p => p.id === from) ?? null;
  const headId = fromPt?.head?.id ?? null;
  const older = versions.filter(v => v.playthroughId === from && v.id !== headId)
    .sort((a, b) => toMs(b.createdAt) - toMs(a.createdAt));
  const picked = fromVersionId === 'latest' ? null : older.find(v => v.id === fromVersionId) ?? null;

  async function submit() {
    if (!name.trim() || !from) return;
    setBusy(true);
    setError('');
    try {
      const created = await api.createPlaythrough(game.id, {
        name: name.trim(),
        fromVersionId: how === 'current' && picked ? picked.id : null,
        fromPlaythroughId: how === 'current' && !picked ? from : null,
      });
      onCreated(created.id);
      toast(how === 'fresh'
        ? `Created ${created.name}: an empty save. Switch a device to it to start playing it.`
        : picked ? `Created ${created.name} from ${fromPt?.name}'s save ${shortId(picked.id)}.`
        : `Created ${created.name} from ${fromPt?.name}'s latest save.`);
    } catch (e) { setError(errorText(e)); }
    finally { setBusy(false); }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/45 backdrop-blur-sm"
      onMouseDown={e => { if (e.target === e.currentTarget && !busy) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-labelledby="new-pt-title"
        className="w-full max-w-[520px] bg-panel border border-line rounded-xl p-5 flex flex-col gap-4 animate-rise">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 id="new-pt-title" className="text-lg font-bold text-fg">New playthrough</h2>
            <p className="text-[13px] text-dim mt-1">A separate line of saves. The others stay as they are; no device switches until you switch it.</p>
          </div>
          <button type="button" onClick={onClose} disabled={busy} aria-label="Close"
            className="text-dim hover:text-fg text-lg leading-none px-1 cursor-pointer bg-transparent border-0"><Icon name="x" size={16} /></button>
        </div>

        <label className="flex flex-col gap-1.5">
          <span className="text-[10px] tracking-[0.13em] uppercase text-faint">Name</span>
          <input autoFocus maxLength={60} placeholder="e.g. Hard mode" value={name} onChange={e => setName(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter' && name.trim() && from) void submit(); }}
            className="w-full px-3 py-2 bg-panel text-fg border border-line rounded-md text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent" />
        </label>

        <div className="flex flex-col gap-2" role="radiogroup" aria-label="How to start">
          <label className="flex items-center gap-2 cursor-pointer text-[12.5px] text-fg">
            <input type="radio" name="pt-start" checked={how === 'current'} onChange={() => setHow('current')} />
            Current save — a copy of an existing playthrough's save
          </label>
          <label className="flex items-center gap-2 cursor-pointer text-[12.5px] text-fg">
            <input type="radio" name="pt-start" checked={how === 'fresh'} onChange={() => setHow('fresh')} />
            Start fresh — an empty save folder, so the game starts a new game
          </label>
          {how === 'fresh' && (
            <p className="pl-6 text-[11.5px] text-dim">
              The server can't see what's in a device's save folder, so this always starts completely
              empty. To keep some files instead, use that device's own tray or CLI to start fresh there.
            </p>
          )}
        </div>

        {how === 'current' && (
          <div className="flex gap-3">
            <label className="flex flex-col gap-1.5 flex-1 min-w-0">
              <span className="text-[10px] tracking-[0.13em] uppercase text-faint">Playthrough</span>
              <Select value={from} onChange={v => { setFrom(v); setFromVersionId('latest'); }} label="Playthrough" variant="field"
                options={live.map(p => ({ value: p.id, label: p.name, sub: p.head ? `${ago(p.head.createdAt)} on ${p.head.machineName}` : 'No save yet' }))} />
            </label>
            <label className="flex flex-col gap-1.5 flex-1 min-w-0">
              <span className="text-[10px] tracking-[0.13em] uppercase text-faint">Version</span>
              <Select value={fromVersionId} onChange={setFromVersionId} label="Version" variant="field"
                options={[
                  { value: 'latest', label: fromPt?.head ? `Latest · ${shortId(fromPt.head.id)}` : 'Latest', sub: fromPt?.head ? `${ago(fromPt.head.createdAt)} on ${fromPt.head.machineName}` : 'No save yet' },
                  ...older.map(v => ({ value: v.id, label: shortId(v.id), sub: `${ago(v.createdAt)} on ${v.machineName}` })),
                ]} />
            </label>
          </div>
        )}

        {error && <p role="alert" className="text-[11.5px] text-accent-ink">{error}</p>}
        <div className="flex items-center gap-2.5">
          <Button size="sm" variant="primary" disabled={busy || !name.trim() || (how === 'current' && !from)} onClick={() => void submit()}>
            {busy ? 'Creating…' : 'Create'}
          </Button>
          <Button size="sm" variant="quiet" onClick={onClose} disabled={busy}>Cancel</Button>
        </div>
      </div>
    </div>
  );
}

/**
 * This playthrough's own versions, picked by `playthroughId` out of GameDetail's list (which re-reads as pushes land):
 * the old Versions card's table, unchanged in its row actions, plus "New playthrough from this version".
 */
function PlaythroughVersions({ game, pt, versions: all, loading, heads, nameOf, conflicts, onCopy, only, onNewFromVersion, reload, onChanged }: {
  game: Game; pt: Playthrough; versions: Version[]; loading: boolean; heads: Set<string>;
  nameOf: (playthroughId: string | null | undefined) => string; conflicts: Conflict[]; onCopy?: (v: Version) => void; only: string;
  onNewFromVersion: (v: Version) => void; reload: () => Promise<void>; onChanged: () => void;
}) {
  const [view, setView] = useState<View>('main');
  const [open, setOpen] = useState<Set<string>>(new Set());
  const [changes, setChanges] = useState<Map<string, VersionChanges>>(new Map());
  const [answered, setAnswered] = useState<{ listKey: string; failed: boolean } | null>(null);

  const headId = pt.head?.id ?? null;
  // A fork's head is the version it started from, which stays in the playthrough it was made in (nothing is
  // copied): listed here too, or a new fork's History reads "No versions yet" under a header naming its latest.
  const own = all.filter(v => v.playthroughId === pt.id);
  const versions = pt.head && !own.some(v => v.id === headId)
    ? [all.find(v => v.id === headId) ?? pt.head, ...own]
    : own;

  const listKey = versions.map(v => v.id).join(',');
  const changesStatus: ChangesStatus = answered?.listKey !== listKey ? 'loading' : answered.failed ? 'failed' : 'loaded';
  useEffect(() => {
    if (!listKey) return;
    let live = true;
    api.versionChanges(game.id)
      .then(list => { if (live) { setChanges(new Map(list.map(c => [c.versionId, c]))); setAnswered({ listKey, failed: false }); } })
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
  const conflicting = new Set(conflicts.filter(c => c.playthroughId === pt.id).flatMap(c => [c.versionAId, c.versionBId]));

  // The server's rule (SyncService.PruneVersionsAsync) for this playthrough alone: its own versions, newest first, past
  // the limit, never one that is any playthrough's head (a fork's starts as one of these), a conflict side or protected.
  const limit = game.retainVersions;
  const prunable = limit != null && limit > 0
    ? [...own].sort((x, y) => toMs(y.createdAt) - toMs(x.createdAt)).slice(limit)
        .filter(v => !v.protected && !heads.has(v.id) && !conflicting.has(v.id)).length
    : null;

  async function prune() {
    try {
      const r = await api.pruneNow(game.id, pt.id);
      await reload();
      onChanged();
      toast(r.removed > 0 ? `Removed ${plural(r.removed, 'version')}.` : 'Nothing to remove — already within the limit.');
    } catch (e) { toastError('Could not prune: ' + errorText(e)); }
  }

  async function remove(v: Version) {
    try { await api.deleteVersion(game.id, v.id); await reload(); onChanged(); toast(`Deleted version ${shortId(v.id)}.`); }
    catch (e) { toastError('Could not delete the version: ' + errorText(e)); }
  }

  async function unprotect(v: Version) {
    try { await api.setVersionProtected(game.id, v.id, false); await reload(); toast(`Version ${shortId(v.id)} is no longer protected.`); }
    catch (e) { toastError('Could not unprotect: ' + errorText(e)); }
  }

  async function download(v: Version) {
    const safe = `${game.name}-${pt.name}`.replace(/[^\w.-]+/g, '_');
    try { await api.downloadVersion(game.id, v.id, `${safe}-${shortId(v.id)}.zip`); }
    catch (e) { toastError('Could not download: ' + errorText(e)); }
  }

  async function setLatest(v: Version) {
    try {
      await api.setLatest(game.id, v.id);
      await reload();
      onChanged();
      toast(`Set ${v.machineName}'s save as ${pt.name}'s latest. Every device on it was told to pull.`, 4000);
    } catch (e) { toastError('Could not set Latest: ' + errorText(e)); }
  }

  function forked(v: Version) {
    if (v.playthroughId === pt.id) return null;
    return <span title={`${pt.name} started from this save. It belongs to ${nameOf(v.playthroughId)} and stays there.`}><Chip>From {nameOf(v.playthroughId)}</Chip></span>;
  }

  function copied(v: Version) {
    if (!v.copiedFrom) return null;
    return <span title={`Copied from ${v.copiedFrom}'s save of this game.`}><Chip>From {v.copiedFrom}</Chip></span>;
  }

  function state(v: Version) {
    if (v.id === headId) return <Chip tone="ok">Latest</Chip>;
    if (conflicting.has(v.id)) return <Chip tone="crit">Conflicting</Chip>;
    if (v.mergedFrom) return <span title={`Made as ${v.mergedFrom}, merged into this game. Read-only history.`}><Chip>From {v.mergedFrom}</Chip></span>;
    if (v.protected) return <Chip tone="warn">Protected</Chip>;
    return <Chip>{view === 'main' ? 'Kept' : 'Backup'}</Chip>;
  }

  return (
    <>
      <div className="flex items-center gap-2 px-4 py-2.5 border-b border-row flex-wrap">
        <div className="inline-flex bg-raise border border-line rounded-full p-[3px] gap-0.5">
          {(['main', 'backups'] as const).map(v => (
            <button key={v} type="button" onClick={() => setView(v)}
              className={`px-2.5 py-1 rounded-full text-xs cursor-pointer ${view === v ? 'bg-tile text-fg font-semibold' : 'text-dim hover:bg-hover'}`}>
              {v === 'main' ? `History ${main.length}` : `Backups ${backups.length}`}
            </button>
          ))}
        </div>
        <Chip>{own.length} kept</Chip>
        <Button size="sm" variant="quiet" disabled={open.size === 0} onClick={() => setOpen(new Set())}>Collapse all</Button>
        {pt.archivedAt ? null : prunable === null
          ? <InlineConfirm
              label="Apply retention" title="Apply the server's default retention to this playthrough now"
              consequence="Keeps the server's default number of newest versions and permanently deletes the older ones. The Latest, both sides of an open conflict and protected versions are never deleted."
              confirmLabel="Apply retention" onConfirm={prune}
            />
          : prunable === 0
            ? <Button size="sm" disabled title={`Already within Keep ${limit}`}>Nothing to prune</Button>
            : <InlineConfirm
                label={`Prune ${plural(prunable, 'version')}`}
                consequence={`Permanently deletes the ${plural(prunable, 'oldest version')} beyond the newest ${limit}. The Latest, both sides of an open conflict and protected versions are never deleted.`}
                confirmLabel={`Prune ${plural(prunable, 'version')}`} onConfirm={prune}
              />}
      </div>
      {view === 'backups' && (
        <p className="px-4 pt-3 text-[11.5px] text-dim leading-[1.5]">
          Not part of this playthrough's current history — nothing here was deleted, it just isn't what devices on it pull.
          Download one to keep a copy, or Set as Latest to bring it back.
        </p>
      )}
      {loading
        ? <p className="px-4 py-5 text-[13px] text-dim">Loading…</p>
        : <DataTable
            caption={`${view === 'main' ? 'Versions' : 'Backups'} of ${pt.name}`}
            rows={shown}
            rowKey={v => v.id}
            expanded={v => open.has(v.id)
              ? <VersionFiles id={`pt-version-files-${v.id}`} game={game} version={v} changes={changes.get(v.id)} status={changesStatus} />
              : null}
            empty={view === 'main'
              ? <EmptyState title="No versions yet">The first push to this playthrough shows up here.</EmptyState>
              : <EmptyState title="No backups">A save that loses a conflict, or is ridden over by a forced push, is kept here.</EmptyState>}
            columns={[
              {
                head: 'Version', kind: ['k', 'm'],
                cell: v => (
                  <button type="button" onClick={() => toggle(v.id)}
                    aria-expanded={open.has(v.id)} aria-controls={`pt-version-files-${v.id}`}
                    aria-label={`${open.has(v.id) ? 'Hide' : 'Show'} the files of version ${shortId(v.id)}`}
                    className="inline-flex items-center gap-1.5 cursor-pointer rounded hover:text-accent-ink">
                    <Icon name="chevron-right" size={13} className={`text-faint transition-transform ${open.has(v.id) ? 'rotate-90 text-accent-ink' : ''}`} />
                    {shortId(v.id)}
                  </button>
                ),
              },
              { head: 'When', kind: 'n', cell: v => <span title={when(v.createdAt)}>{ago(v.createdAt)}</span> },
              { head: 'Machine', cell: v => v.machineName },
              { head: 'Size', kind: 'n', cell: v => fmtSize(v.size) },
              {
                head: 'State', kind: 'wrap',
                cell: v => <span className="flex gap-1 items-center flex-wrap min-w-[10.5rem]">{state(v)}{forked(v)}{copied(v)}<ChangeChips changes={changes.get(v.id)} /></span>,
              },
              {
                head: '', label: 'Actions', end: true,
                cell: v => (
                  <span className="inline-flex gap-1.5 justify-end flex-wrap">
                    {v.id !== headId && !v.mergedFrom && (
                      <InlineConfirm
                        label="Set as Latest" tone="primary"
                        consequence={`${pt.name}'s latest becomes this save from ${when(v.createdAt)}. ${pt.devices.length ? `${pt.devices.map(d => d.machineName).join(', ')} ${pt.devices.length > 1 ? 'pull' : 'pulls'} it.` : 'No device plays this playthrough yet, so nothing is pulled.'} Other playthroughs aren’t touched.${conflicting.has(v.id) ? ' Its open conflict is resolved in its favour.' : ''}${only}`}
                        confirmLabel={`Make ${shortId(v.id)} Latest`} onConfirm={() => setLatest(v)}
                      />
                    )}
                    <Button size="sm" variant="quiet" aria-label="Download" title="Download this version's archive"
                      onClick={() => void download(v)}><Icon name="download" size={13} /></Button>
                    {!v.mergedFrom && (
                      <Button size="sm" variant="quiet" aria-label="Fork this save into a new playthrough" title="Fork into a new playthrough"
                        onClick={() => onNewFromVersion(v)}><Icon name="git-fork" size={13} /></Button>
                    )}
                    {onCopy && !v.mergedFrom && (
                      <Button size="sm" variant="quiet" onClick={() => onCopy(v)} title="Copy this save to another emulator's tree of this game">Copy to…</Button>
                    )}
                    {v.protected && (
                      <InlineConfirm label="Unprotect" consequence="Retention may then delete it the next time it runs."
                        confirmLabel={`Unprotect ${shortId(v.id)}`} onConfirm={() => unprotect(v)} />
                    )}
                    {v.id !== headId && !conflicting.has(v.id) && (
                      <InlineConfirm
                        label="Delete" triggerVariant="quiet"
                        consequence={`This version's archive is permanently removed from the server. Download it first if you might want it.`}
                        confirmLabel={`Delete ${shortId(v.id)}`} onConfirm={() => remove(v)}
                      />
                    )}
                  </span>
                ),
              },
            ]}
          />}
    </>
  );
}
