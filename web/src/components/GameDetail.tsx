import { useEffect, useRef, useState } from 'react';
import { api, ApiError, errorText } from '../api';
import type { GameSummary, Machine, Command, Conflict, Version, VersionStats, MachineSavePath, MachineScanCandidate } from '../types';
import { toTemplate, isTemplate } from '../savePathTemplate';
import { artSrc, artSrcSet } from '../art';
import { ArtPicker } from './ArtPicker';
import { Chip } from './ui/Chip';
import { Button } from './ui/Button';

const shortId = (id: string | null | undefined) => id ? id.replace(/-/g, '').slice(0, 8) : '—';
const asUtc = (t: string) => /[Z+]|-\d\d:\d\d$/.test(t) ? t : t + 'Z';
const when = (t: string | null | undefined) => t ? new Date(asUtc(t)).toLocaleString() : '—';
/**
 * Adaptive, because this is a decision aid. A fixed "MB" renders every small save as "0.00 MB",
 * which is exactly the case where a size is being read to tell two saves apart.
 */
const fmtSize = (n: number) =>
  n < 1024 ? `${n} B`
    : n < 1024 * 1024 ? (n / 1024).toFixed(1) + ' KB'
      : (n / (1024 * 1024)).toFixed(2) + ' MB';

/**
 * What a command with no result yet is actually waiting for. "Dispatched" used to be a dead end in
 * the UI as much as in the data: an agent that never answered left the row saying nothing forever.
 * Delivery is leased now, so the honest answer is either "an agent has it" or "its claim lapsed and
 * the next poll takes it again".
 */
const commandWaitText = (c: Command) => {
  if (c.status === 'Pending') return 'awaiting next poll…';
  if (c.status !== 'Dispatched') return '—';
  const lapsed = c.leaseExpiresAt && new Date(asUtc(c.leaseExpiresAt)) < new Date();
  return lapsed ? 'no reply — will be retried on the next poll' : 'running on the agent…';
};

interface Props {
  summary: GameSummary;
  machines: Machine[];
  commands: Command[];
  conflicts: Conflict[];
  onRefresh: () => void;
}

export function GameDetail({ summary, machines, commands, conflicts, onRefresh }: Props) {
  const [versions, setVersions] = useState<Version[]>([]);
  const [loadingVersions, setLoadingVersions] = useState(true);
  const [machinePaths, setMachinePaths] = useState<MachineSavePath[]>([]);
  const [pathCandidates, setPathCandidates] = useState<MachineScanCandidate[]>([]);
  const [editingPathFor, setEditingPathFor] = useState<string | null>(null);
  const [pathDraft, setPathDraft] = useState('');
  const [excludeDraft, setExcludeDraft] = useState<string[]>(summary.game.excludeGlobs ?? []);
  const [newPattern, setNewPattern] = useState('');
  const [excludeForGameId, setExcludeForGameId] = useState(summary.game.id);
  const [savingExcludes, setSavingExcludes] = useState(false);
  const [defaultGlobs, setDefaultGlobs] = useState<string[]>([]);
  const [excludeOpen, setExcludeOpen] = useState(false);
  // null while the first preview for this draft hasn't answered yet — kept distinct from 0 so the
  // count never flashes "0" for a moment before the real number lands.
  const [previewCount, setPreviewCount] = useState<number | null>(null);
  // The server's reason when it refuses the draft (a pattern the matcher cannot evaluate): shown on the
  // editor and blocks Save, instead of the request failing quietly and the bad pattern being saved.
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [policyDraft, setPolicyDraft] = useState<string>(summary.game.conflictPolicy ?? 'Manual');
  const [preferredMachineDraft, setPreferredMachineDraft] = useState<string | null>(summary.game.preferredMachineId ?? null);
  const [policyForGameId, setPolicyForGameId] = useState(summary.game.id);
  const [savingPolicy, setSavingPolicy] = useState(false);
  const [versionStats, setVersionStats] = useState<Record<string, VersionStats>>({});
  const requestedStatsRef = useRef<Set<string>>(new Set());
  const [versionsView, setVersionsView] = useState<'main' | 'backups'>('main');
  const [artOpen, setArtOpen] = useState(false);
  const penRef = useRef<HTMLButtonElement>(null);

  const { game, head, lease, hasOpenConflict } = summary;

  // Reset the policy draft when switching games (not on every poll).
  if (policyForGameId !== game.id) {
    setPolicyForGameId(game.id);
    setPolicyDraft(game.conflictPolicy ?? 'Manual');
    setPreferredMachineDraft(game.preferredMachineId ?? null);
  }

  // Reset the exclude editor when switching games (not on every poll — avoids clobbering edits).
  if (excludeForGameId !== game.id) {
    setExcludeForGameId(game.id);
    setExcludeDraft(game.excludeGlobs ?? []);
  }
  const headId = head?.id ?? null;
  // filter, not find. Taking the first match silently hid every other conflict on the game, and the
  // server used to return them oldest-first — so the console reliably showed the LEAST useful one
  // while the save actually being played sat in a conflict the UI never rendered.
  const gameConflicts = conflicts.filter(c => c.gameId === game.id);
  const gameCmds = commands.filter(c => c.gameId === game.id).slice(0, 8);

  // Latest version per machine (for Machines table "Last upload" column)
  const latestByMachine: Record<string, Version> = {};
  for (const v of versions) {
    // A version whose uploader has been deleted keeps its name but has no machine to key on —
    // it is history, not a live contributor.
    if (!v.machineId) continue;
    if (!latestByMachine[v.machineId]) latestByMachine[v.machineId] = v;
  }

  // A version is "in the main tree" if it's an ancestor of the current head — walk
  // parentVersionId back from it. Everything else the game still has is a backup, almost always
  // the losing side of a past conflict: no schema change needed, this is purely a computed split
  // of the same version list the console already fetches (tasks/conflict-resolution-ui/plan.md).
  const mainVersionIds = new Set<string>();
  {
    const byId = new Map(versions.map(v => [v.id, v]));
    let cur = headId;
    while (cur && !mainVersionIds.has(cur)) {
      mainVersionIds.add(cur);
      cur = byId.get(cur)?.parentVersionId ?? null;
    }
  }
  const mainVersions = versions.filter(v => mainVersionIds.has(v.id));
  const backupVersions = versions.filter(v => !mainVersionIds.has(v.id));
  const shownVersions = versionsView === 'main' ? mainVersions : backupVersions;

  // Initial-sync wizard: show when multiple machines have versions
  const contributors = Object.values(latestByMachine);

  useEffect(() => {
    setLoadingVersions(true);
    api.versions(game.id).then(vs => { setVersions(vs); setLoadingVersions(false); });
    api.getGamePaths(game.id).then(setMachinePaths).catch(() => {});
    api.getGamePathCandidates(game.id).then(setPathCandidates).catch(() => {});
    setEditingPathFor(null);
    setVersionsView('main');
  }, [game.id]);

  // Global exclude defaults (read-only display); fetched once.
  useEffect(() => {
    api.settings().then(s => setDefaultGlobs(s.defaultExcludeGlobs ?? [])).catch(() => {});
  }, []);

  // Dry run against the head archive for whatever the draft currently is. Only while the editor is
  // OPEN: it used to run on every game selection with the section collapsed, and each call makes the
  // server read the head archive's zip index off disk for a number nobody was looking at. Re-fires
  // on every add/remove. A 400 is the server refusing the draft itself (see previewError).
  useEffect(() => {
    if (!excludeOpen) return;
    let cancelled = false;
    setPreviewCount(null);
    setPreviewError(null);
    api.previewExcludes(game.id, excludeDraft)
      .then(r => { if (!cancelled) setPreviewCount(r.wouldExclude); })
      .catch(e => {
        if (cancelled) return;
        setPreviewCount(null);
        if (e instanceof ApiError && e.status === 400) setPreviewError(e.detail || e.message);
      });
    return () => { cancelled = true; };
  }, [game.id, excludeDraft, excludeOpen]);

  // File count / newest-mtime for the versions on either side of an open conflict — fetched
  // lazily, only for versions actually shown in a conflict card. An archive never changes once
  // uploaded, so requestedStatsRef stops the 15s poll (App.tsx) from re-fetching what it already
  // has, even though `conflicts` is a fresh array reference every time.
  useEffect(() => {
    for (const c of conflicts) {
      if (c.gameId !== game.id) continue;
      for (const vid of [c.versionAId, c.versionBId]) {
        if (requestedStatsRef.current.has(vid)) continue;
        requestedStatsRef.current.add(vid);
        api.versionStats(game.id, vid)
          .then(stats => setVersionStats(prev => ({ ...prev, [vid]: stats })))
          .catch(() => { requestedStatsRef.current.delete(vid); }); // best-effort — retry on the next poll
      }
    }
  }, [conflicts, game.id]);

  async function handleSaveExcludes() {
    setSavingExcludes(true);
    try { await api.setExcludes(game.id, excludeDraft); onRefresh(); }
    catch (e) { alert('Could not save exclude patterns: ' + errorText(e)); }
    finally { setSavingExcludes(false); }
  }

  function handleAddPattern() {
    const p = newPattern.trim();
    if (!p || excludeDraft.includes(p)) { setNewPattern(''); return; }
    setExcludeDraft(prev => [...prev, p]);
    setNewPattern('');
  }

  function handleRemovePattern(p: string) {
    setExcludeDraft(prev => prev.filter(x => x !== p));
  }

  const excludeDirty = JSON.stringify(excludeDraft) !== JSON.stringify(game.excludeGlobs ?? []);

  async function handleSavePolicy() {
    setSavingPolicy(true);
    try {
      await api.setConflictPolicy(
        game.id, policyDraft,
        policyDraft === 'PreferMachine' ? preferredMachineDraft : null
      );
      onRefresh();
    } catch (e) { alert('Could not save conflict policy: ' + (e as Error).message); }
    finally { setSavingPolicy(false); }
  }

  async function handleRefreshArt() {
    try { await api.refreshArt(game.id); onRefresh(); } catch (e) { alert('Refresh art failed: ' + (e as Error).message); }
  }

  async function handleSetEnabled() {
    try { await api.setEnabled(game.id, !game.enabled); onRefresh(); } catch (e) { alert('Could not change state: ' + (e as Error).message); }
  }

  async function handleDeleteGame() {
    if (!confirm(`Delete "${game.name}"? Removes versions and history from the server. Agents keep local saves.`)) return;
    try { await api.deleteGame(game.id); onRefresh(); } catch (e) { alert('Delete failed: ' + (e as Error).message); }
  }

  async function handleSetSaveDir() {
    const current = game.suggestedSaveDir ?? '';
    const dir = prompt('Suggested save folder fallback (used when a machine has no stored path). Leave blank to clear:', current);
    if (dir === null) return;
    try { await api.setSaveDir(game.id, dir.trim()); onRefresh(); } catch (e) { alert('Could not set save folder: ' + (e as Error).message); }
  }

  /**
   * Promote one machine's concrete path to the game's template, so every OTHER machine expands it
   * for itself instead of inheriting a path that means nothing on their filesystem.
   */
  async function handleUseAsTemplate(savePath: string, machineName: string) {
    const template = toTemplate(savePath);
    if (!template) {
      alert(
        `Can't turn this into a template:\n\n${savePath}\n\n` +
        `It isn't under a known folder (Documents, AppData, Public, Saved Games, or a Proton prefix), ` +
        `so there's no equivalent to point another machine at.`
      );
      return;
    }
    if (!confirm(
      `Set this game's save path template from ${machineName}?\n\n${template}\n\n` +
      `Each machine expands this against its own folders — on a Steam Deck, inside that game's ` +
      `Proton prefix. Machines that already have a path of their own keep it.`
    )) return;

    try { await api.setSaveDir(game.id, template); onRefresh(); }
    catch (e) { alert('Could not set the template: ' + (e as Error).message); }
  }

  async function reloadPaths() {
    setMachinePaths(await api.getGamePaths(game.id));
    // A stored path retires its candidate server-side, so refresh both together or the row keeps
    // offering a guess for a machine that is now mapped.
    setPathCandidates(await api.getGamePathCandidates(game.id).catch(() => []));
  }

  /** Write a path for one machine. Blank clears it. */
  async function saveMachinePath(machineId: string, path: string) {
    try {
      if (path.trim() === '') await api.clearMachinePath(game.id, machineId);
      else await api.setMachinePath(game.id, machineId, path.trim());
      setEditingPathFor(null);
      await reloadPaths();
    } catch (e) { alert('Could not update path: ' + (e as Error).message); }
  }

  async function handleDeleteVersion(versionId: string) {
    if (!confirm('Delete this version? The archive will be permanently removed from the server.')) return;
    try {
      await api.deleteVersion(game.id, versionId);
      setVersions(await api.versions(game.id));
      onRefresh();
    } catch (e) { alert('Delete failed: ' + (e as Error).message); }
  }

  async function handleSetLatest(versionId: string) {
    // Says what actually happens now: a pull is queued for every machine that syncs this game, and
    // it is unforced, so a machine holding unsynced local work reports blocked rather than losing it.
    if (!confirm(
      'Set this version as Latest?\n\n' +
      'A pull is queued for every machine that syncs this game. Any machine with local changes it ' +
      'has not pushed yet will report the pull as blocked instead of overwriting them.\n\n' +
      'If this version is one of the options in an open conflict, that conflict is marked resolved ' +
      'in its favour.'
    )) return;
    try {
      await api.setLatest(game.id, versionId);
      const vs = await api.versions(game.id);
      setVersions(vs);
      onRefresh();
    } catch (e) { alert('Set as Latest failed: ' + (e as Error).message); }
  }

  async function handleForceRelease() {
    if (!confirm('Force-release this lease?')) return;
    try { await api.forceRelease(game.id); onRefresh(); } catch (e) { alert('Force-release failed: ' + (e as Error).message); }
  }

  /**
   * Every other destructive action on this page confirms — delete, Set as Latest, delete game — and
   * this was the only one that did not, despite being the most consequential button here. The
   * dialog names the consequence people do not expect: newer saves stop being what machines pull.
   */
  async function handleResolveConflict(conflictId: string, versionId: string, keepBoth: boolean) {
    const v = versions.find(x => x.id === versionId);
    const newer = v ? versions.filter(x => new Date(asUtc(x.createdAt)) > new Date(asUtc(v.createdAt))).length : 0;
    const others = gameConflicts.length - 1;

    const msg =
      (keepBoth
        ? 'Keep both saves and use this one as Latest?\n\n'
        : 'Use this save as Latest?\n\n') +
      (v ? `${v.machineName} — ${when(v.createdAt)} (${fmtSize(v.size)})\n\n` : '') +
      (newer > 0
        ? `${newer} newer save${newer > 1 ? 's' : ''} will no longer be what machines pull. ` +
          'Nothing is deleted — you can still promote one later with "Set as Latest".\n\n'
        : '') +
      (keepBoth
        ? 'Both conflict snapshots will be protected from automatic pruning until you unprotect them under Versions.\n\n'
        : '') +
      'Both machines in this conflict will be told to pull.' +
      (others > 0 ? `\n\n${others} other conflict${others > 1 ? 's' : ''} on this game will remain.` : '');

    if (!confirm(msg)) return;
    try { await api.resolveConflict(conflictId, versionId, keepBoth); onRefresh(); }
    catch (e) { alert('Resolve failed: ' + (e as Error).message); }
  }

  async function handleSetVersionProtected(v: Version, value: boolean) {
    if (!value && !confirm(
      'Unprotect this version?\n\nIt may be permanently deleted the next time retention runs.'
    )) return;
    try {
      await api.setVersionProtected(game.id, v.id, value);
      setVersions(await api.versions(game.id));
      onRefresh();
    } catch (e) { alert('Could not update protection: ' + (e as Error).message); }
  }

  async function handlePruneNow() {
    if (!confirm(
      'Apply retention now?\n\n' +
      `Keeps the ${game.retainVersions ?? 'server default'} newest version(s) and permanently deletes ` +
      'the rest. The current Latest and anything in an open conflict are never deleted.'
    )) return;
    try {
      const r = await api.pruneNow(game.id);
      setVersions(await api.versions(game.id));
      onRefresh();
      alert(r.removed > 0 ? `Removed ${r.removed} version(s).` : 'Nothing to remove — already within the limit.');
    } catch (e) { alert('Prune failed: ' + (e as Error).message); }
  }

  async function handleDownloadVersion(v: Version) {
    const safe = game.name.replace(/[^\w.-]+/g, '_');
    try { await api.downloadVersion(game.id, v.id, `${safe}-${shortId(v.id)}.zip`); }
    catch (e) { alert('Download failed: ' + (e as Error).message); }
  }

  async function handleCmd(machineId: string, type: string, force: boolean) {
    try { await api.queueCommand(machineId, game.id, type, force); onRefresh(); } catch (e) { alert('Could not queue command: ' + (e as Error).message); }
  }

  const card = { background: 'var(--color-panel)', border: '1px solid var(--color-line)', borderRadius: 8, overflow: 'hidden' } as const;
  const cardHeader = { padding: '11px 18px', borderBottom: '1px solid var(--color-line)' } as const;
  const sectionLabel = { fontSize: 10, fontWeight: 700, color: 'var(--color-safe-ink)', letterSpacing: '0.12em', textTransform: 'uppercase' as const };
  const thStyle = { padding: '8px 18px', textAlign: 'left' as const, fontSize: 11, color: 'var(--color-dim)', fontWeight: 500 };
  const tdStyle = { padding: '11px 18px', fontSize: 13, fontWeight: 500 };
  const tdMono = { padding: '11px 18px', fontSize: 11, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace" };
  const rowSep = { borderTop: '1px solid var(--color-line)' };

  const ghostBtn = (extra?: React.CSSProperties): React.CSSProperties => ({
    padding: '2px 8px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent',
    borderRadius: 4, fontSize: 10, cursor: 'pointer', ...extra,
  });
  const amberBtn: React.CSSProperties = { padding: '2px 8px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', background: 'transparent', borderRadius: 4, fontSize: 10, cursor: 'pointer' };
  const pillBtn = (active: boolean): React.CSSProperties => ({
    padding: '3px 10px', borderRadius: 4, fontSize: 11, fontWeight: 600, cursor: 'pointer',
    // The current tab is `selected` (neutral tile, not the accent) — plan.md: the accent stays the one action.
    border: '1px solid var(--color-line)',
    background: active ? 'var(--color-tile)' : 'transparent',
    color: active ? 'var(--color-fg)' : 'var(--color-dim)',
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>

      {/* ── Game Details Card ── */}
      <div style={{ ...card, padding: '18px 20px' }}>
        <div style={{ display: 'flex', gap: 18, alignItems: 'flex-start' }}>

          {/* Box art — the pen over it opens the cover/icon picker. Shown on hover and on keyboard focus,
              and always on touch screens, which have no hover to reveal it. */}
          <div className="group relative flex-shrink-0" style={{ width: 94, height: 134 }}>
            {game.gridUrl
              ? <img src={artSrc(game.gridUrl, 192)} srcSet={artSrcSet(game.gridUrl, [96, 192, 256])} sizes="94px" alt="cover" style={{ width: 94, height: 134, objectFit: 'cover', borderRadius: 6, border: '1px solid var(--color-line)', display: 'block' }} />
              : (
                <div style={{ width: 94, height: 134, background: 'var(--color-raise)', border: '1px dashed var(--color-line)', borderRadius: 6, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 5 }}>
                  <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" style={{ color: 'var(--color-faint)' }}><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                  <span style={{ color: 'var(--color-dim)', fontSize: 9, fontFamily: "'JetBrains Mono', monospace", textAlign: 'center', lineHeight: 1.5 }}>box<br/>art</span>
                </div>
              )
            }
            <button
              ref={penRef}
              type="button"
              onClick={() => setArtOpen(o => !o)}
              aria-label="Change cover and icon"
              aria-expanded={artOpen}
              title="Change cover and icon"
              className="absolute inset-0 rounded-[6px] border-0 p-0 cursor-pointer bg-transparent
                transition-colors duration-150 ease-[var(--ease)]
                group-hover:bg-black/45 focus-visible:bg-black/45
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
            >
              <span className="absolute right-1.5 bottom-1.5 grid place-items-center w-7 h-7 rounded-full bg-black/70 text-white
                opacity-0 transition-opacity duration-150 ease-[var(--ease)]
                group-hover:opacity-100 group-focus-within:opacity-100 [@media(hover:none)]:opacity-100">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>
              </span>
            </button>
          </div>

          {/* Info column */}
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 11, minWidth: 0 }}>

            {/* Title + badges + actions */}
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
              <span style={{ fontSize: 17, fontWeight: 700, letterSpacing: '-0.3px' }}>{game.name}</span>

              {hasOpenConflict
                ? <span style={{ padding: '2px 7px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>conflict</span>
                : <span style={{ padding: '2px 7px', border: '1px solid var(--color-safe-line)', color: 'var(--color-safe-ink)', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>in sync</span>
              }

              {lease?.holderMachineName
                ? <span style={{ padding: '2px 7px', border: '1px solid var(--color-line)', color: 'var(--color-dim)', borderRadius: 4, fontSize: 10 }}>leased by {lease.holderMachineName}</span>
                : <span style={{ padding: '2px 7px', border: '1px solid var(--color-line)', color: 'var(--color-dim)', borderRadius: 4, fontSize: 10 }}>free</span>
              }

              {!game.enabled && <span style={{ padding: '2px 7px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', borderRadius: 4, fontSize: 10 }}>disabled</span>}

              <button style={ghostBtn()} onClick={handleRefreshArt}
                title="Fetch SteamGridDB's default cover and icon again. This replaces ones you picked.">Refresh art</button>
              <button style={ghostBtn()} onClick={handleSetEnabled}>{game.enabled ? 'Disable' : 'Enable'}</button>
              {lease?.holderMachineName && (
                <button style={ghostBtn({ borderColor: 'var(--color-watch-line)', color: 'var(--color-watch-ink)' })} onClick={handleForceRelease}>Force-release lease</button>
              )}
              <button style={amberBtn} onClick={handleDeleteGame}>Delete</button>
            </div>

            {/* Latest commit meta */}
            {head ? (
              <p style={{ fontSize: 11.5, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace" }}>
                latest&nbsp;<span style={{ color: 'var(--color-watch-ink)', fontWeight: 500 }}>{shortId(head.id)}</span>&nbsp;from&nbsp;
                <span style={{ color: 'var(--color-fg)' }}>{head.machineName}</span>&nbsp;at&nbsp;
                <span style={{ color: 'var(--color-fg)' }}>{when(head.createdAt)}</span>&nbsp;·&nbsp;
                <span style={{ color: 'var(--color-fg)' }}>{fmtSize(head.size)}</span>
              </p>
            ) : (
              <p style={{ fontSize: 11.5, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace" }}>no saves yet</p>
            )}

            {/* Total storage for this game */}
            <p style={{ fontSize: 11, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace" }}>
              total stored:&nbsp;
              <span style={{ color: 'var(--color-dim)', fontWeight: 500 }}>{fmtSize(summary.totalStorageBytes)}</span>
              &nbsp;across&nbsp;
              <span style={{ color: 'var(--color-dim)' }}>{versions.length} version{versions.length !== 1 ? 's' : ''}</span>
            </p>

            {/* Suggested save dir fallback */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, background: 'var(--color-raise)', padding: '7px 10px', borderRadius: 5, border: '1px solid var(--color-line)' }}>
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{ flexShrink: 0, color: 'var(--color-faint)' }}><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
              <span style={{ fontSize: 10, color: 'var(--color-dim)', flexShrink: 0 }}>
                {isTemplate(game.suggestedSaveDir) ? 'template:' : 'fallback path:'}
              </span>
              <span
                title={isTemplate(game.suggestedSaveDir)
                  ? 'Each machine expands this against its own folders — inside the game\'s Proton prefix on a Steam Deck. A machine that already has its own path keeps it.'
                  : 'A literal path, used only where it happens to exist. "Use as template" on a machine row turns it into one that works everywhere.'}
                style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 11, color: isTemplate(game.suggestedSaveDir) ? 'var(--color-safe-ink)' : 'var(--color-dim)', flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {game.suggestedSaveDir || <span style={{ color: 'var(--color-dim)', fontStyle: 'italic' }}>none</span>}
              </span>
              <button style={{ padding: '3px 9px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent', borderRadius: 4, fontSize: 10, cursor: 'pointer', flexShrink: 0 }} onClick={handleSetSaveDir}>Edit</button>
            </div>

            {/* Conflict policy */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, background: 'var(--color-raise)', padding: '7px 10px', borderRadius: 5, border: '1px solid var(--color-line)', flexWrap: 'wrap' }}>
              <span style={{ fontSize: 10, color: 'var(--color-dim)', flexShrink: 0 }}>conflict policy:</span>
              <select
                value={policyDraft}
                onChange={e => { setPolicyDraft(e.target.value); if (e.target.value !== 'PreferMachine') setPreferredMachineDraft(null); }}
                style={{ background: 'var(--color-panel)', color: 'var(--color-fg)', border: '1px solid var(--color-line)', borderRadius: 4, fontSize: 11, padding: '2px 5px', cursor: 'pointer' }}
              >
                <option value="Manual">Manual — resolve conflicts in the console</option>
                <option value="NewestWins">Newest wins — latest upload always wins</option>
                <option value="PreferMachine">Prefer machine — one machine always wins</option>
              </select>
              {policyDraft === 'PreferMachine' && (
                <select
                  value={preferredMachineDraft ?? ''}
                  onChange={e => setPreferredMachineDraft(e.target.value || null)}
                  style={{ background: 'var(--color-panel)', color: 'var(--color-fg)', border: '1px solid var(--color-line)', borderRadius: 4, fontSize: 11, padding: '2px 5px', cursor: 'pointer' }}
                >
                  <option value="">— pick a machine —</option>
                  {machines.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
              )}
              {(policyDraft !== (game.conflictPolicy ?? 'Manual') ||
                (policyDraft === 'PreferMachine' && preferredMachineDraft !== (game.preferredMachineId ?? null))) && (
                <button
                  disabled={savingPolicy || (policyDraft === 'PreferMachine' && !preferredMachineDraft)}
                  onClick={handleSavePolicy}
                  style={{ padding: '3px 9px', border: '1px solid var(--color-line)', color: savingPolicy ? 'var(--color-dim)' : 'var(--color-fg)', background: 'transparent', borderRadius: 4, fontSize: 10, cursor: savingPolicy ? 'default' : 'pointer', flexShrink: 0 }}
                >
                  {savingPolicy ? 'Saving…' : 'Save'}
                </button>
              )}
            </div>

          </div>
        </div>
      </div>

      {artOpen && (
        <ArtPicker game={game} onChanged={onRefresh} onClose={() => { setArtOpen(false); penRef.current?.focus(); }} />
      )}

      {/* ── Conflict resolution — one card per open conflict, newest-active first ── */}
      {gameConflicts.map(c => {
        const stuck = machines.find(m => m.id === c.machineId)?.name;
        return (
          <div key={c.id} style={{ background: 'var(--color-accent-soft)', border: `1px solid ${c.escalated ? 'var(--color-accent)' : 'var(--color-accent-line)'}`, borderRadius: 8, padding: '10px 12px' }}>
            <b style={{ color: 'var(--color-watch-ink)' }}>
              Conflict{stuck ? ` — ${stuck} cannot sync` : ''}: choose the version to keep
            </b>
            {' '}
            <a href="#help/conflicts" style={{ fontSize: 11, color: 'var(--color-fg)', textDecoration: 'underline' }}>Why did this happen?</a>
            {c.escalated && (
              <div style={{ fontSize: 11, color: 'var(--color-accent-ink)', marginTop: 5, fontWeight: 700 }}>
                Overdue — this conflict has been unresolved for more than six hours.
              </div>
            )}

            {(game.conflictPolicy ?? 'Manual') === 'Manual' && (
              <div style={{ fontSize: 11, color: 'var(--color-dim)', marginTop: 4 }}>
                Playing solo?{' '}
                <button
                  onClick={() => { setPolicyDraft('NewestWins'); void api.setConflictPolicy(game.id, 'NewestWins').then(onRefresh); }}
                  style={{ background: 'none', border: 'none', color: 'var(--color-fg)', fontSize: 11, cursor: 'pointer', textDecoration: 'underline', padding: 0 }}
                >
                  Set to "Newest wins"
                </button>
                {' '}to auto-resolve future conflicts.
              </div>
            )}
            {c.count > 1 && (
              <div style={{ fontSize: 11, color: 'var(--color-dim)', marginTop: 4, lineHeight: 1.5 }}>
                {c.count} divergent saves folded into this conflict — the <b>newest</b> is offered below.
                The older ones are still listed under Versions and can be promoted with "Set as Latest".
              </div>
            )}

            {/* Machine, time and size, because a hash fragment is not enough to choose between two
                saves. This card used to render only a short id and a date. */}
            <div style={{ display: 'flex', gap: 8, marginTop: 8, flexWrap: 'wrap' }}>
              {[c.versionAId, c.versionBId].map(vid => {
                const v = versions.find(x => x.id === vid);
                const stats = versionStats[vid];
                return (
                  <div key={vid} style={{ background: 'var(--color-panel)', border: '1px solid var(--color-accent-line)', borderRadius: 5, padding: 8 }}>
                    <div style={{ fontWeight: 600, fontSize: 12 }}>
                      {v ? v.machineName : shortId(vid)}{vid === headId ? ' — current Latest' : ''}
                    </div>
                    <div style={{ fontSize: 10.5, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace", margin: '2px 0' }}>
                      {v ? `${when(v.createdAt)} · ${fmtSize(v.size)}` : shortId(vid)}
                    </div>
                    {stats && (
                      <div style={{ fontSize: 10.5, color: 'var(--color-dim)', fontFamily: "'JetBrains Mono', monospace" }}>
                        {stats.fileCount} file{stats.fileCount === 1 ? '' : 's'}
                        {stats.newestFileWriteUtc ? ` · newest change ${when(stats.newestFileWriteUtc)}` : ''}
                      </div>
                    )}
                    <div style={{ display: 'flex', gap: 5, marginTop: 7 }}>
                      <button
                        onClick={() => handleResolveConflict(c.id, vid, false)}
                        style={{ padding: '4px 9px', background: 'var(--color-accent)', color: 'var(--color-on-accent)', border: 'none', borderRadius: 4, fontSize: 10.5, cursor: 'pointer' }}
                      >
                        Use as Latest
                      </button>
                      <button
                        onClick={() => handleResolveConflict(c.id, vid, true)}
                        style={{ padding: '4px 9px', background: 'transparent', color: 'var(--color-watch-ink)', border: '1px solid var(--color-watch-line)', borderRadius: 4, fontSize: 10.5, cursor: 'pointer' }}
                      >
                        Keep both · use this
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
        );
      })}

      {/* ── Initial sync wizard ── */}
      {contributors.length > 1 && (
        <div style={{ background: 'var(--color-tile)', border: '1px solid var(--color-line)', borderRadius: 8, padding: '10px 12px' }}>
          <b>Initial sync — which machine has your real progress?</b>
          <p style={{ fontSize: 12, color: 'var(--color-dim)', marginTop: 2 }}>Sets that machine's newest save as Latest (what every machine pulls).</p>
          <div style={{ display: 'flex', gap: 8, marginTop: 8, flexWrap: 'wrap' }}>
            {contributors.map(v => (
              <button key={v.id} onClick={() => handleSetLatest(v.id)}
                style={{ padding: '5px 12px', background: 'var(--color-accent)', color: 'var(--color-on-accent)', border: 'none', borderRadius: 5, fontSize: 12, cursor: 'pointer' }}
              >
                {v.machineName} ({when(v.createdAt)}){v.id === headId ? ' — current' : ''}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* ── Exclude Patterns (collapsible) ── */}
      <div style={card}>
        <button
          onClick={() => setExcludeOpen(o => !o)}
          style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '11px 18px', background: 'transparent', border: 'none', cursor: 'pointer', textAlign: 'left' }}
        >
          <span style={sectionLabel}>Exclude patterns</span>
          <span style={{ fontSize: 11, color: 'var(--color-dim)', userSelect: 'none' }}>{excludeOpen ? '▲' : '▼'}</span>
        </button>
        {excludeOpen && (
          <div className="flex flex-col gap-2.5" style={{ padding: '0 18px 14px', borderTop: '1px solid var(--color-line)', paddingTop: 10 }}>
            <p className="text-[11px] text-dim">
              Files matching these never upload. Bare patterns like <code className="font-mono">*.log</code> match
              at any depth; <code className="font-mono">cache/**</code> anchors at this game's save folder.
              See <a href="#help/glob-patterns" className="text-accent">glob pattern docs</a>.
            </p>

            {defaultGlobs.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <span className="text-[10px] text-faint uppercase tracking-[0.08em]">Inherited from server defaults</span>
                <div className="flex flex-wrap gap-1.5">
                  {defaultGlobs.map(g => <Chip key={g} className="font-mono">{g}</Chip>)}
                </div>
              </div>
            )}

            <div className="flex flex-col gap-1.5">
              <span className="text-[10px] text-faint uppercase tracking-[0.08em]">This game's own patterns</span>
              <div className="flex flex-wrap gap-1.5">
                {excludeDraft.length === 0 && <span className="text-[11px] text-dim italic">none</span>}
                {excludeDraft.map(p => (
                  <Chip key={p} className="font-mono">
                    {p}
                    <button
                      onClick={() => handleRemovePattern(p)}
                      title={`Remove ${p}`}
                      aria-label={`Remove ${p}`}
                      className="ml-1 text-dim hover:text-accent"
                    >
                      ×
                    </button>
                  </Chip>
                ))}
              </div>
            </div>

            <div className="flex items-center gap-1.5">
              <input
                value={newPattern}
                onChange={e => setNewPattern(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && handleAddPattern()}
                placeholder="e.g. *.log or cache/**"
                className="flex-1 bg-ink text-fg border border-line rounded-md px-2.5 py-1.5 text-xs font-mono focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent"
              />
              <Button variant="default" size="sm" onClick={handleAddPattern}>Add</Button>
            </div>

            {/* Dry run against the head archive — see SyncService.PreviewExcludesAsync. It counts the
                whole draft against what the server holds, which can include files a SAVED pattern
                already covers (the latest save may predate that pattern until the next upload), so
                the wording depends on whether there is anything unsaved rather than claiming these
                are all new. */}
            {previewError && (
              <p role="alert" className="text-[11px] text-accent-ink">{previewError}</p>
            )}
            {!previewError && previewCount !== null && previewCount > 0 && (
              <p className="text-[11px] text-watch">
                {excludeDirty
                  ? `Saving would leave ${previewCount} file${previewCount === 1 ? '' : 's'} in the latest save out of future uploads.`
                  : `${previewCount} file${previewCount === 1 ? '' : 's'} in the latest save match${previewCount === 1 ? 'es' : ''} these patterns; the next upload will leave ${previewCount === 1 ? 'it' : 'them'} out.`}
              </p>
            )}

            <div className="flex justify-end">
              <Button variant="primary" size="sm" disabled={savingExcludes || !excludeDirty || previewError !== null} onClick={handleSaveExcludes}>
                {savingExcludes ? 'Saving…' : 'Save patterns'}
              </Button>
            </div>
          </div>
        )}
      </div>

      {/* ── Machines Table ── */}
      <div style={card}>
        <div style={cardHeader}><span style={sectionLabel}>Machines</span></div>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: 'var(--color-raise)' }}>
              <th style={thStyle}>Machine</th>
              <th style={thStyle}>Last upload (this game)</th>
              <th style={thStyle}>Last seen</th>
              <th style={thStyle}>Remote actions</th>
            </tr>
          </thead>
          <tbody>
            {machines.length === 0
              ? <tr><td colSpan={4} style={{ padding: '20px 18px', color: 'var(--color-dim)', fontSize: 13 }}>No machines registered.</td></tr>
              : machines.map(m => {
                  const last = latestByMachine[m.id];
                  return (
                    <tr key={m.id} style={rowSep}>
                      <td style={tdStyle}>{m.name}</td>
                      <td style={tdMono}>{last ? when(last.createdAt) : '—'}</td>
                      <td style={tdMono}>{when(m.lastSeen)}</td>
                      <td style={{ padding: '11px 18px' }}>
                        <div style={{ display: 'flex', gap: 5 }}>
                          <button style={{ padding: '4px 10px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }} onClick={() => handleCmd(m.id, 'Pull', true)}>Pull</button>
                          <button style={{ padding: '4px 10px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }} onClick={() => handleCmd(m.id, 'Push', true)}>Push</button>
                          <button style={{ padding: '4px 10px', border: 'none', color: 'var(--color-on-accent)', background: 'var(--color-accent)', borderRadius: 4, fontSize: 11, cursor: 'pointer', fontWeight: 500 }} onClick={() => handleCmd(m.id, 'Sync', false)}>Sync</button>
                        </div>
                      </td>
                    </tr>
                  );
                })
            }
          </tbody>
        </table>
      </div>

      {/* ── Save paths per machine ── */}
      <div style={card}>
        <div style={cardHeader}><span style={sectionLabel}>Save paths per machine</span></div>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: 'var(--color-raise)' }}>
              <th style={thStyle}>Machine</th>
              <th style={thStyle}>Save folder</th>
              <th style={{ ...thStyle, width: 80 }}></th>
            </tr>
          </thead>
          <tbody>
            {machines.length === 0
              ? <tr><td colSpan={3} style={{ padding: '20px 18px', color: 'var(--color-dim)', fontSize: 13 }}>No machines registered.</td></tr>
              : machines.map(m => {
                  const stored = machinePaths.find(p => p.machineId === m.id);
                  const candidate = pathCandidates.find(c => c.machineId === m.id);
                  const editing = editingPathFor === m.id;
                  return (
                    <tr key={m.id} style={rowSep}>
                      <td style={tdStyle}>{m.name}</td>
                      <td style={tdMono}>
                        {editing ? (
                          // The label names the machine explicitly. The old prompt() said "Save
                          // folder for X" in a modal detached from the table, which made it easy to
                          // type a Deck path into a Windows machine's row.
                          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                            <label style={{ color: 'var(--color-dim)', fontSize: 11 }}>
                              Save path on <span style={{ color: 'var(--color-fg)', fontWeight: 600 }}>{m.name}</span>
                            </label>
                            <input
                              autoFocus
                              value={pathDraft}
                              onChange={e => setPathDraft(e.target.value)}
                              onKeyDown={e => {
                                if (e.key === 'Enter') void saveMachinePath(m.id, pathDraft);
                                if (e.key === 'Escape') setEditingPathFor(null);
                              }}
                              placeholder="Leave blank to clear the stored path"
                              style={{
                                background: 'var(--color-raise)', border: '1px solid var(--color-line)', borderRadius: 4,
                                padding: '6px 8px', color: 'var(--color-fg)', fontSize: 12,
                                fontFamily: 'ui-monospace, Consolas, monospace', outline: 'none',
                              }}
                            />
                          </div>
                        ) : stored ? (
                          stored.savePath
                        ) : candidate ? (
                          // The agent found this but has NOT adopted it — a human confirms here.
                          <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                            <span style={{ color: 'var(--color-dim)', fontStyle: 'italic' }}>not set</span>
                            <span style={{ color: 'var(--color-dim)', fontSize: 11, fontStyle: 'normal' }}>
                              {m.name}'s scan found:
                            </span>
                            <span style={{ color: 'var(--color-fg)', fontSize: 12, wordBreak: 'break-all' }}>
                              {candidate.suggestedPath}
                            </span>
                          </div>
                        ) : (
                          <span style={{ color: 'var(--color-dim)', fontStyle: 'italic' }}>not set</span>
                        )}
                      </td>
                      <td style={{ padding: '11px 18px', whiteSpace: 'nowrap' }}>
                        {editing ? (
                          <div style={{ display: 'flex', gap: 5 }}>
                            <button
                              style={{ padding: '4px 10px', border: 'none', color: 'var(--color-on-accent)', background: 'var(--color-accent)', borderRadius: 4, fontSize: 11, cursor: 'pointer', fontWeight: 500 }}
                              onClick={() => void saveMachinePath(m.id, pathDraft)}
                            >Save</button>
                            <button style={ghostBtn()} onClick={() => setEditingPathFor(null)}>Cancel</button>
                          </div>
                        ) : (
                          <div style={{ display: 'flex', gap: 5 }}>
                            {!stored && candidate && (
                              <button
                                style={{ padding: '4px 10px', border: 'none', color: 'var(--color-on-accent)', background: 'var(--color-accent)', borderRadius: 4, fontSize: 11, cursor: 'pointer', fontWeight: 500 }}
                                onClick={() => void saveMachinePath(m.id, candidate.suggestedPath)}
                              >Apply</button>
                            )}
                            {stored && toTemplate(stored.savePath) && (
                              <button
                                style={ghostBtn()}
                                title={`Describe this location generically so other machines can find their own copy:\n${toTemplate(stored.savePath)}`}
                                onClick={() => void handleUseAsTemplate(stored.savePath, m.name)}
                              >Use as template</button>
                            )}
                            <button
                              style={ghostBtn()}
                              onClick={() => {
                                setPathDraft(stored?.savePath ?? candidate?.suggestedPath ?? '');
                                setEditingPathFor(m.id);
                              }}
                            >{stored ? 'Edit' : 'Set'}</button>
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })
            }
          </tbody>
        </table>
      </div>

      {/* ── Recent Remote Commands ── */}
      {gameCmds.length > 0 && (
        <div style={card}>
          <div style={cardHeader}><span style={sectionLabel}>Recent Remote Commands</span></div>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: 'var(--color-raise)' }}>
                <th style={{ ...thStyle, whiteSpace: 'nowrap' }}>When</th>
                <th style={thStyle}>Machine</th>
                <th style={thStyle}>Action</th>
                <th style={thStyle}>Status</th>
                <th style={thStyle}>Result</th>
              </tr>
            </thead>
            <tbody>
              {gameCmds.map(c => (
                <tr key={c.id} style={rowSep}>
                  <td style={{ ...tdMono, whiteSpace: 'nowrap' }}>{when(c.createdAt)}</td>
                  <td style={tdStyle}>{c.machineName}</td>
                  <td style={{ padding: '11px 18px', fontSize: 12, color: 'var(--color-fg)' }}>{c.type}{c.force ? ' (force)' : ''}</td>
                  <td style={{ padding: '11px 18px', fontSize: 12, fontWeight: 600, whiteSpace: 'nowrap' }}>
                    {c.status === 'Done'
                      ? <span style={{ color: 'var(--color-safe-ink)' }}>Done</span>
                      : c.status === 'Failed'
                        ? <span style={{ color: 'var(--color-watch-ink)' }}>Failed</span>
                        : <span style={{ color: 'var(--color-dim)' }}>{c.status}</span>
                    }
                    {c.claimCount > 1 && (
                      <span style={{ color: 'var(--color-watch-ink)', fontWeight: 400 }}> · retried ×{c.claimCount}</span>
                    )}
                  </td>
                  <td style={{ padding: '11px 18px', fontSize: 11.5, color: 'var(--color-dim)', maxWidth: 340, wordBreak: 'break-word', lineHeight: 1.6 }}>
                    {c.result || commandWaitText(c)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* ── Versions / Backups ──
          A backup is anything the game still has that isn't an ancestor of the current head —
          almost always the losing side of a past conflict (a Force push that rode over one, or an
          admin/agent resolve that didn't keep both). No new endpoint: same version list the console
          already fetches, just split by walking parentVersionId back from head. Reuses the existing
          Download and Set as Latest actions unchanged — tasks/conflict-resolution-ui/plan.md. */}
      <div style={{ ...card, marginBottom: 24 }}>
        <div style={{ ...cardHeader, display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 8 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={sectionLabel}>{versionsView === 'main' ? 'Versions' : 'Backups'}</span>
            <div style={{ display: 'flex', gap: 4 }}>
              <button style={pillBtn(versionsView === 'main')} onClick={() => setVersionsView('main')}>
                Versions ({mainVersions.length})
              </button>
              <button style={pillBtn(versionsView === 'backups')} onClick={() => setVersionsView('backups')}>
                Backups ({backupVersions.length})
              </button>
            </div>
          </div>
          {/* Retention otherwise runs only as a side effect of an upload, so a game nobody is playing
              keeps whatever it accumulated — and clearing that used to need the admin API by hand.
              Applies across both tabs, so it stays visible regardless of which is open. */}
          <button
            style={ghostBtn()}
            title="Apply this game's retention limit now, without waiting for the next upload"
            onClick={handlePruneNow}
          >Prune now</button>
        </div>
        {versionsView === 'backups' && (
          <p style={{ padding: '10px 18px 0', margin: 0, fontSize: 11.5, color: 'var(--color-dim)', lineHeight: 1.5 }}>
            Not part of the current save's history — nothing here was deleted, it just isn't what
            machines currently pull. Download one to keep a copy, or "Set as Latest" to bring it back.
          </p>
        )}
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: 'var(--color-raise)' }}>
              <th style={thStyle}>Version</th>
              <th style={thStyle}>Machine</th>
              <th style={thStyle}>When</th>
              <th style={thStyle}>Size</th>
              <th style={thStyle}></th>
            </tr>
          </thead>
          <tbody>
            {loadingVersions
              ? <tr><td colSpan={5} style={{ padding: '20px 18px', color: 'var(--color-dim)', fontSize: 13 }}>Loading…</td></tr>
              : shownVersions.length === 0
                ? <tr><td colSpan={5} style={{ padding: '20px 18px', color: 'var(--color-dim)', fontSize: 13 }}>
                    {versionsView === 'main' ? 'No versions yet.' : 'No backups — nothing here yet.'}
                  </td></tr>
                : shownVersions.map(v => (
                    <tr key={v.id} style={rowSep}>
                      <td style={{ padding: '11px 18px' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                          <span style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 12, color: 'var(--color-watch-ink)' }}>{shortId(v.id)}</span>
                          {v.id === headId
                            ? <span style={{ padding: '2px 7px', background: 'var(--color-safe-soft)', color: 'var(--color-safe-ink)', borderRadius: 3, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>Latest</span>
                            : <button style={{ padding: '2px 8px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', background: 'transparent', borderRadius: 3, fontSize: 10, cursor: 'pointer' }} onClick={() => handleSetLatest(v.id)}>Set as Latest</button>
                          }
                          {v.protected && (
                            <span title="Protected from automatic pruning" style={{ padding: '2px 7px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', borderRadius: 3, fontSize: 10 }}>
                              Protected
                            </span>
                          )}
                        </div>
                      </td>
                      <td style={tdStyle}>{v.machineName}</td>
                      <td style={tdMono}>{when(v.createdAt)}</td>
                      <td style={{ padding: '11px 18px', fontSize: 11.5, color: 'var(--color-dim)' }}>{fmtSize(v.size)}</td>
                      <td style={{ padding: '11px 18px' }}>
                        <div style={{ display: 'flex', gap: 5, justifyContent: 'flex-end' }}>
                          {/* The escape hatch. Without it there was no way to take a copy of a save
                              from the console before doing something destructive to it, so "back it
                              up first" could not be offered as a step at all. */}
                          <button
                            onClick={() => handleDownloadVersion(v)}
                            title="Download this version's archive"
                            style={{ padding: '2px 8px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent', borderRadius: 3, fontSize: 10, cursor: 'pointer' }}
                          >
                            Download
                          </button>
                          {v.protected && (
                            <button
                              onClick={() => handleSetVersionProtected(v, false)}
                              title="Allow automatic retention to prune this version"
                              style={{ padding: '2px 8px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', background: 'transparent', borderRadius: 3, fontSize: 10, cursor: 'pointer' }}
                            >
                              Unprotect
                            </button>
                          )}
                          {v.id !== headId && (
                            <button
                              onClick={() => handleDeleteVersion(v.id)}
                              style={{ padding: '2px 8px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', background: 'transparent', borderRadius: 3, fontSize: 10, cursor: 'pointer' }}
                            >
                              Delete
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))
            }
          </tbody>
        </table>
      </div>

    </div>
  );
}
