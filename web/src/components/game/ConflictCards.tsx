import { useEffect, useRef, useState } from 'react';
import { api } from '../../api';
import type { Conflict, Game, Machine, Version, VersionStats } from '../../types';
import { asUtc, fmtSize, shortId, when } from '../../format';

interface Props {
  game: Game;
  headId: string | null;
  /** This game's open conflicts. */
  conflicts: Conflict[];
  machines: Machine[];
  versions: Version[];
  onSetNewestWins: () => void;
  onRefresh: () => void;
}

/** One card per open conflict, newest-active first — each side's machine, time, size and file stats,
 *  and the two ways to resolve it. */
export function ConflictCards({ game, headId, conflicts, machines, versions, onSetNewestWins, onRefresh }: Props) {
  const [versionStats, setVersionStats] = useState<Record<string, VersionStats>>({});
  const requestedStatsRef = useRef<Set<string>>(new Set());

  // File count / newest-mtime for the versions on either side of an open conflict — fetched
  // lazily, only for versions actually shown in a conflict card. An archive never changes once
  // uploaded, so requestedStatsRef stops the 15s poll (App.tsx) from re-fetching what it already
  // has, even though `conflicts` is a fresh array reference every time.
  useEffect(() => {
    for (const c of conflicts) {
      for (const vid of [c.versionAId, c.versionBId]) {
        if (requestedStatsRef.current.has(vid)) continue;
        requestedStatsRef.current.add(vid);
        api.versionStats(game.id, vid)
          .then(stats => setVersionStats(prev => ({ ...prev, [vid]: stats })))
          .catch(() => { requestedStatsRef.current.delete(vid); }); // best-effort — retry on the next poll
      }
    }
  }, [conflicts, game.id]);

  /**
   * Every other destructive action on this page confirms — delete, Set as Latest, delete game — and
   * this was the only one that did not, despite being the most consequential button here. The
   * dialog names the consequence people do not expect: newer saves stop being what machines pull.
   */
  async function handleResolveConflict(conflictId: string, versionId: string, keepBoth: boolean) {
    const v = versions.find(x => x.id === versionId);
    const newer = v ? versions.filter(x => new Date(asUtc(x.createdAt)) > new Date(asUtc(v.createdAt))).length : 0;
    const others = conflicts.length - 1;

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

  return (
    <>
      {conflicts.map(c => {
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
                  onClick={onSetNewestWins}
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
    </>
  );
}
