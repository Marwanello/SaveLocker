import { useEffect, useState } from 'react';
import { api, ApiError, errorText } from '../../api';
import type { Game } from '../../types';
import { plural } from '../../format';
import { toast, toastError } from '../../toast';
import { Card } from '../ui/Card';
import { Button } from '../ui/Button';
import { Icon } from '../ui/Icon';

interface Props {
  game: Game;
  onRefresh: () => void;
}

/**
 * plan.md Phase 10.5: the shipped exclude editor, restyled — this game's own patterns as chips over the
 * server's defaults as dashed "inherited" chips, an add field, and a dry run against the head archive.
 */
export function ExcludePatternsCard({ game, onRefresh }: Props) {
  const saved = game.excludeGlobs ?? [];
  const [draft, setDraft] = useState<string[]>(saved);
  const [adding, setAdding] = useState('');
  const [defaults, setDefaults] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  // The dry run reads the head archive's zip index on the server, so it runs only once asked for (or
  // once the draft is being edited), never just because the page opened.
  const [previewOn, setPreviewOn] = useState(false);
  // The dry run's answer, tagged with the draft it was asked about, and shown only while the draft is
  // still that one. Discarding, or undoing an edit by hand, used to leave the abandoned draft's count on
  // screen under the saved patterns' wording (or its refusal, for a pattern no longer there).
  // `error` is the server's reason when it refuses the draft (a pattern the matcher cannot evaluate):
  // shown here and blocks Save, instead of the request failing quietly and the bad pattern being saved.
  const [preview, setPreview] = useState<{ draft: string; count: number | null; error: string | null } | null>(null);

  const draftKey = JSON.stringify(draft);
  const dirty = draftKey !== JSON.stringify(saved);
  // null while a preview is outstanding — distinct from 0 so the count never flashes "0" first.
  const current = preview?.draft === draftKey ? preview : null;
  const previewCount = current?.count ?? null;
  const previewError = current?.error ?? null;

  // Follow a save made elsewhere (another browser, this card's own save once the poll lands) — but
  // only while nothing here is being edited, so a poll never throws away a draft in progress.
  const [seenSaved, setSeenSaved] = useState(JSON.stringify(saved));
  if (seenSaved !== JSON.stringify(saved)) {
    if (JSON.stringify(draft) === seenSaved) setDraft(saved);
    setSeenSaved(JSON.stringify(saved));
  }

  useEffect(() => {
    api.settings().then(s => setDefaults(s.defaultExcludeGlobs ?? [])).catch(() => {});
  }, []);

  useEffect(() => {
    if (!previewOn && !dirty) return;
    let cancelled = false;
    const key = JSON.stringify(draft);
    api.previewExcludes(game.id, draft)
      .then(r => { if (!cancelled) setPreview({ draft: key, count: r.wouldExclude, error: null }); })
      .catch(e => {
        if (cancelled) return;
        if (e instanceof ApiError && e.status === 400) setPreview({ draft: key, count: null, error: e.detail || e.message });
      });
    return () => { cancelled = true; };
  }, [game.id, draft, previewOn, dirty]);

  function add() {
    const p = adding.trim();
    if (p && !draft.includes(p)) setDraft(d => [...d, p]);
    setAdding('');
  }

  async function save() {
    setSaving(true);
    try {
      await api.setExcludes(game.id, draft);
      onRefresh();
      toast('Saved the exclude patterns. Agents apply them from their next push.');
    } catch (e) { toastError('Could not save the patterns: ' + errorText(e)); }
    finally { setSaving(false); }
  }

  return (
    <Card
      title="Exclude patterns"
      headerRight={<a href="#help/glob-patterns" className="inline-flex items-center rounded-full border border-line bg-raise px-[11px] py-[5px] text-xs font-medium text-fg hover:bg-hover
        focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">Glob syntax</a>}
    >
      <p className="text-[12.5px] text-dim mb-3">
        Files matching these never upload. <code className="font-mono text-[11px]">*.log</code> matches at any depth;{' '}
        <code className="font-mono text-[11px]">cache/**</code> is relative to this game's save folder.
      </p>

      <div className="flex flex-wrap gap-[7px]">
        {draft.map(p => (
          <span key={p} className="inline-flex items-center gap-2 font-mono text-[11.5px] bg-tile border border-line rounded-lg pl-[11px] pr-2 py-1.5 text-fg">
            {p}
            <button type="button" onClick={() => setDraft(d => d.filter(x => x !== p))}
              title={`Remove ${p}`} aria-label={`Remove ${p}`}
              className="w-[18px] h-[18px] grid place-items-center rounded-[5px] border-0 bg-transparent text-dim cursor-pointer
                hover:bg-panel hover:text-accent-ink hover:opacity-100
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent">
              <Icon name="x" size={12} />
            </button>
          </span>
        ))}
        {/* The field itself is borderless inside the dashed box, so the box carries the focus ring. */}
        <span className="inline-flex items-center gap-2 bg-tile border border-dashed border-line rounded-lg px-2.5
          focus-within:outline focus-within:outline-2 focus-within:outline-accent focus-within:outline-offset-1">
          <input
            value={adding}
            onChange={e => setAdding(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add(); } }}
            placeholder="add a pattern…"
            aria-label="Add an exclude pattern (Enter adds it)"
            className="bg-transparent border-0 outline-none text-fg font-mono text-[11.5px] py-[7px] w-[150px] focus:!border-0"
          />
          <span aria-hidden className="font-mono text-[10px] text-faint">↵</span>
        </span>
      </div>

      {defaults.length > 0 && (
        <div className="mt-3.5">
          <div className="text-[10px] tracking-[0.1em] uppercase text-faint mb-2">Inherited from the server</div>
          <div className="flex flex-wrap gap-[7px]">
            {defaults.map(p => (
              <span key={p} title="A server default — applies to every game"
                className="inline-flex items-center font-mono text-[11.5px] bg-transparent border border-dashed border-line rounded-lg px-[11px] py-1.5 text-dim">
                {p}
              </span>
            ))}
          </div>
        </div>
      )}

      {/* A dry run against the head archive (SyncService.PreviewExcludesAsync). It counts the whole draft,
          which can include files a SAVED pattern already covers (the latest save may predate it), so the
          wording depends on whether anything is unsaved rather than claiming these are all new. */}
      {previewError && <p role="alert" className="mt-3 text-[11.5px] text-accent-ink">{previewError}</p>}
      {!previewError && previewCount !== null && (
        <p className="mt-3 text-[11.5px] text-dim" role="status">
          {previewCount === 0
            ? 'Nothing in the latest save matches.'
            : dirty
              ? `Saving would leave ${plural(previewCount, 'file')} in the latest save out of future uploads.`
              : `${plural(previewCount, 'file')} in the latest save match${previewCount === 1 ? 'es' : ''}; the next upload leaves ${previewCount === 1 ? 'it' : 'them'} out.`}
        </p>
      )}

      <div className="flex items-center gap-2.5 mt-3.5 flex-wrap">
        <Button size="sm" disabled={saving || !dirty || previewError !== null} onClick={() => void save()}>
          {saving ? 'Saving…' : 'Save patterns'}
        </Button>
        {dirty && <Button size="sm" variant="quiet" onClick={() => setDraft(saved)}>Discard changes</Button>}
        {!previewOn && !dirty && <Button size="sm" variant="quiet" onClick={() => setPreviewOn(true)}>Preview what is skipped</Button>}
      </div>
    </Card>
  );
}
