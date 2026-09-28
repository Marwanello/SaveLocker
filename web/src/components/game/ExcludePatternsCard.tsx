import { useEffect, useState } from 'react';
import { api, ApiError, errorText } from '../../api';
import type { Game } from '../../types';
import { Chip } from '../ui/Chip';
import { Button } from '../ui/Button';
import { card, sectionLabel } from './legacyStyles';

interface Props {
  game: Game;
  onRefresh: () => void;
}

/** The game's own exclude patterns over the server's defaults, with a dry run against its head archive. */
export function ExcludePatternsCard({ game, onRefresh }: Props) {
  const [excludeDraft, setExcludeDraft] = useState<string[]>(game.excludeGlobs ?? []);
  const [newPattern, setNewPattern] = useState('');
  const [excludeForGameId, setExcludeForGameId] = useState(game.id);
  const [savingExcludes, setSavingExcludes] = useState(false);
  const [defaultGlobs, setDefaultGlobs] = useState<string[]>([]);
  const [excludeOpen, setExcludeOpen] = useState(false);
  // null while the first preview for this draft hasn't answered yet — kept distinct from 0 so the
  // count never flashes "0" for a moment before the real number lands.
  const [previewCount, setPreviewCount] = useState<number | null>(null);
  // The server's reason when it refuses the draft (a pattern the matcher cannot evaluate): shown on the
  // editor and blocks Save, instead of the request failing quietly and the bad pattern being saved.
  const [previewError, setPreviewError] = useState<string | null>(null);

  // Reset the exclude editor when switching games (not on every poll — avoids clobbering edits).
  if (excludeForGameId !== game.id) {
    setExcludeForGameId(game.id);
    setExcludeDraft(game.excludeGlobs ?? []);
  }

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

  return (
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
  );
}
