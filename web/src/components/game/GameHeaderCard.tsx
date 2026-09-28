import { useRef, useState } from 'react';
import { api } from '../../api';
import type { GameSummary, Machine } from '../../types';
import { isTemplate } from '../../savePathTemplate';
import { artSrc, artSrcSet } from '../../art';
import { fmtSize, shortId, when } from '../../format';
import { ArtPicker } from '../ArtPicker';
import { card, ghostBtn, amberBtn } from './legacyStyles';

interface Props {
  summary: GameSummary;
  machines: Machine[];
  versionCount: number;
  policyDraft: string;
  setPolicyDraft: (v: string) => void;
  preferredMachineDraft: string | null;
  setPreferredMachineDraft: (v: string | null) => void;
  onRefresh: () => void;
}

/** The game card: box art (and its picker), name, state badges, the game-level actions, the latest
 *  version, the fallback save path and the conflict policy. */
export function GameHeaderCard({
  summary, machines, versionCount, policyDraft, setPolicyDraft, preferredMachineDraft, setPreferredMachineDraft, onRefresh,
}: Props) {
  const [savingPolicy, setSavingPolicy] = useState(false);
  const [artOpen, setArtOpen] = useState(false);
  const penRef = useRef<HTMLButtonElement>(null);
  const { game, head, lease, hasOpenConflict } = summary;

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

  async function handleForceRelease() {
    if (!confirm('Force-release this lease?')) return;
    try { await api.forceRelease(game.id); onRefresh(); } catch (e) { alert('Force-release failed: ' + (e as Error).message); }
  }

  return (
    <>
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
              <span style={{ color: 'var(--color-dim)' }}>{versionCount} version{versionCount !== 1 ? 's' : ''}</span>
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
    </>
  );
}
