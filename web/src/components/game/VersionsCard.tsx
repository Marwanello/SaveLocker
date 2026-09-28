import { useEffect, useState } from 'react';
import { api } from '../../api';
import type { Game, Version } from '../../types';
import { fmtSize, shortId, when } from '../../format';
import { card, cardHeader, sectionLabel, thStyle, tdStyle, tdMono, rowSep, ghostBtn, pillBtn } from './legacyStyles';

interface Props {
  game: Game;
  headId: string | null;
  versions: Version[];
  loading: boolean;
  /** Re-read the version list after an action changed it. */
  reloadVersions: () => Promise<void>;
  onSetLatest: (versionId: string) => void;
  onRefresh: () => void;
}

/**
 * Versions / Backups. A backup is anything the game still has that isn't an ancestor of the current
 * head — almost always the losing side of a past conflict (a Force push that rode over one, or an
 * admin/agent resolve that didn't keep both). No new endpoint: same version list the console already
 * fetches, just split by walking parentVersionId back from head. Reuses the existing Download and
 * Set as Latest actions unchanged — tasks/conflict-resolution-ui/plan.md.
 */
export function VersionsCard({ game, headId, versions, loading, reloadVersions, onSetLatest, onRefresh }: Props) {
  const [versionsView, setVersionsView] = useState<'main' | 'backups'>('main');
  useEffect(() => { setVersionsView('main'); }, [game.id]);

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

  async function handleDeleteVersion(versionId: string) {
    if (!confirm('Delete this version? The archive will be permanently removed from the server.')) return;
    try {
      await api.deleteVersion(game.id, versionId);
      await reloadVersions();
      onRefresh();
    } catch (e) { alert('Delete failed: ' + (e as Error).message); }
  }

  async function handleSetVersionProtected(v: Version, value: boolean) {
    if (!value && !confirm(
      'Unprotect this version?\n\nIt may be permanently deleted the next time retention runs.'
    )) return;
    try {
      await api.setVersionProtected(game.id, v.id, value);
      await reloadVersions();
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
      await reloadVersions();
      onRefresh();
      alert(r.removed > 0 ? `Removed ${r.removed} version(s).` : 'Nothing to remove — already within the limit.');
    } catch (e) { alert('Prune failed: ' + (e as Error).message); }
  }

  async function handleDownloadVersion(v: Version) {
    const safe = game.name.replace(/[^\w.-]+/g, '_');
    try { await api.downloadVersion(game.id, v.id, `${safe}-${shortId(v.id)}.zip`); }
    catch (e) { alert('Download failed: ' + (e as Error).message); }
  }

  return (
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
          {loading
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
                          : <button style={{ padding: '2px 8px', border: '1px solid var(--color-watch-line)', color: 'var(--color-watch-ink)', background: 'transparent', borderRadius: 3, fontSize: 10, cursor: 'pointer' }} onClick={() => onSetLatest(v.id)}>Set as Latest</button>
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
  );
}
