import { useEffect, useRef, useState } from 'react';
import { api, errorText } from '../api';
import { artSrc, artSrcSet } from '../art';
import type { ArtKind, ArtOption, ArtOptionsPage, ArtSource, Game, SgdbGame } from '../types';
import { Button } from './ui/Button';
import { SearchField } from './ui/SearchField';

interface Props {
  game: Game;
  /** The game's art changed on the server — reload it. */
  onChanged: () => void;
  onClose: () => void;
}

const LABEL: Record<ArtKind, { title: string; noun: string }> = {
  grid: { title: 'Box art', noun: 'cover' },
  icon: { title: 'Icon', noun: 'icon' },
};

/**
 * plan.md "No modals … pickers expand inline": opens under the game card. Two strips — the cover and
 * the icon — each five SteamGridDB options at a time with a pager. Choosing one saves it straight
 * away (the server downloads and stores it); there is no separate Apply, and a wrong pick is one click
 * from being replaced.
 *
 * <p>
 * Both strips show the artwork of the same SteamGridDB game (the backlog's "remember the chosen id per
 * game"), named above them. That matters when a SaveLocker game's name matches the wrong SteamGridDB
 * entry: the search box lets its real page be picked, and "Use automatic match" puts it back.
 * </p>
 */
export function ArtPicker({ game, onChanged, onClose }: Props) {
  const [applying, setApplying] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [source, setSource] = useState<ArtSource | null>(null);
  /** Bumped when the source changes, remounting both strips so they reload page 0 from the new game. */
  const [sourceVersion, setSourceVersion] = useState(0);
  const root = useRef<HTMLElement>(null);

  // It opens far from the pen that opened it in tab order (after everything else on the game card),
  // so a keyboard user would otherwise have to walk the whole card to reach it — and Escape would
  // do nothing, because focus was outside. Landing focus here fixes both; GameDetail hands it back
  // to the pen on close.
  useEffect(() => { root.current?.focus(); }, []);

  useEffect(() => {
    let current = true;
    setSource(null);
    api.artSource(game.id)
      .then(s => { if (current) setSource(s); })
      .catch(() => { if (current) setSource(null); });
    return () => { current = false; };
  }, [game.id]);

  async function choose(kind: ArtKind, option: ArtOption) {
    setApplying(`${kind}:${option.url}`);
    setError(null);
    try {
      await api.setArt(game.id, kind, option.url);
      onChanged();
    } catch (e) {
      setError(errorText(e));
    } finally {
      setApplying(null);
    }
  }

  function applySource(next: ArtSource) {
    setSource(next);
    setSourceVersion(v => v + 1);
  }

  return (
    <section
      ref={root}
      tabIndex={-1}
      aria-label="Choose artwork"
      onKeyDown={e => { if (e.key === 'Escape') onClose(); }}
      className="animate-rise bg-panel border border-line rounded-[14px] p-[18px] flex flex-col gap-5 outline-none"
    >
      <header className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold tracking-[-0.02em] text-fg m-0">Artwork from SteamGridDB</h3>
          <p className="text-xs text-dim m-0 mt-0.5">Pick one to use it — it is saved straight away.</p>
        </div>
        <Button size="sm" onClick={onClose}>Done</Button>
      </header>

      <SourceBar game={game} source={source} onChanged={applySource} />

      {error && <p role="alert" className="text-xs text-dim m-0">Could not use that image: {error}</p>}

      <Strip key={`grid:${sourceVersion}`} game={game} kind="grid" applying={applying} onChoose={o => choose('grid', o)} />
      <Strip key={`icon:${sourceVersion}`} game={game} kind="icon" applying={applying} onChoose={o => choose('icon', o)} />
    </section>
  );
}

interface SourceProps {
  game: Game;
  source: ArtSource | null;
  onChanged: (source: ArtSource) => void;
}

/**
 * Names the SteamGridDB game both strips pull from, and changes it. Appears when a SaveLocker game's
 * name matches the wrong SteamGridDB entry — before this, the only way out was renaming the game.
 */
function SourceBar({ game, source, onChanged }: SourceProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SgdbGame[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function search(e: React.FormEvent) {
    e.preventDefault();
    const q = query.trim();
    if (!q || busy) return;
    setBusy(true);
    setError(null);
    setResults(null);
    try {
      setResults(await api.artSearch(game.id, q));
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  async function pick(chosen: SgdbGame) {
    setBusy(true);
    setError(null);
    try {
      onChanged(await api.setArtSource(game.id, chosen.id));
      setOpen(false);
      setResults(null);
      setQuery('');
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  async function reset() {
    setBusy(true);
    setError(null);
    try {
      onChanged(await api.setArtSource(game.id, null));
      setOpen(false);
      setResults(null);
    } catch (err) {
      setError(errorText(err));
    } finally {
      setBusy(false);
    }
  }

  const label = source === null
    ? 'SteamGridDB game: …'
    : source.name
      ? `${source.automatic ? 'Automatic match' : 'Chosen'}: ${source.name}`
      : source.automatic
        ? 'Automatic match: no SteamGridDB game found'
        : `Chosen: #${source.sgdbId}`;

  return (
    <div className="border border-line rounded-[10px] px-3.5 py-2.5 flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <span className="text-xs text-dim min-w-0 truncate" title={label}>{label}</span>
        <div className="flex items-center gap-1 shrink-0">
          {source && !source.automatic && (
            <Button size="sm" variant="quiet" disabled={busy} onClick={reset}>Use automatic match</Button>
          )}
          <Button size="sm" variant={open ? 'selected' : 'default'} disabled={busy}
            aria-expanded={open} onClick={() => { setOpen(o => !o); setError(null); }}>
            {open ? 'Cancel' : 'Change'}
          </Button>
        </div>
      </div>

      {open && (
        <div className="flex flex-col gap-2">
          <form onSubmit={search} className="flex items-center gap-2">
            <SearchField value={query} onChange={setQuery} aria-label="Search SteamGridDB games"
              placeholder="Search by name" className="flex-1" />
            <Button size="sm" type="submit" disabled={busy || !query.trim()}>
              {busy ? 'Searching…' : 'Search'}
            </Button>
          </form>

          {error && <p role="alert" className="text-xs text-dim m-0">{error}</p>}

          {results && results.length === 0 && (
            <p className="text-xs text-dim m-0">No SteamGridDB games match “{query.trim()}”.</p>
          )}

          {results && results.length > 0 && (
            <ul className="list-none m-0 p-0 max-h-[180px] overflow-y-auto border border-line rounded-[9px] divide-y divide-line"
              aria-label="SteamGridDB search results">
              {results.map(g => (
                <li key={g.id}>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => pick(g)}
                    className="w-full text-left bg-transparent border-0 px-3 py-2 cursor-pointer font-[inherit] text-xs text-fg
                      hover:bg-hover disabled:cursor-default disabled:opacity-60
                      focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:-outline-offset-2"
                  >
                    {g.name}
                    {g.year && <span className="text-faint"> · {g.year}</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

interface StripProps {
  game: Game;
  kind: ArtKind;
  applying: string | null;
  onChoose: (option: ArtOption) => void;
}

function Strip({ game, kind, applying, onChoose }: StripProps) {
  const [page, setPage] = useState(0);
  const [data, setData] = useState<ArtOptionsPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let current = true;
    // Clearing first is what shows the placeholders while the next page loads, instead of the old
    // page sitting there looking clickable.
    setData(null);
    setError(null);
    api.artOptions(game.id, kind, page)
      .then(d => { if (current) setData(d); })
      .catch(e => { if (current) setError(errorText(e)); });
    return () => { current = false; };
  }, [game.id, kind, page, attempt]);

  const isGrid = kind === 'grid';
  const tile = isGrid ? 'w-[84px] h-[126px]' : 'w-14 h-14';
  const loading = data === null && error === null;
  const busy = applying !== null;
  const currentUrl = isGrid ? game.gridUrl : game.iconUrl;

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <span className="text-[10px] font-semibold text-faint tracking-[0.12em] uppercase">{LABEL[kind].title}</span>
        <div className="flex items-center gap-1">
          <Button size="sm" variant="quiet" aria-label={`Previous ${LABEL[kind].noun} options`}
            disabled={page === 0 || loading} onClick={() => setPage(p => p - 1)}>‹</Button>
          <span className="text-[11px] text-dim tabular-nums min-w-[44px] text-center" aria-live="polite">Page {page + 1}</span>
          <Button size="sm" variant="quiet" aria-label={`Next ${LABEL[kind].noun} options`}
            disabled={!data?.hasMore || loading} onClick={() => setPage(p => p + 1)}>›</Button>
        </div>
      </div>

      <div className="flex flex-wrap items-start gap-3">
        <figure className="m-0 flex flex-col items-center gap-1">
          <span className={`${tile} rounded-[9px] overflow-hidden bg-raise border border-line grid place-items-center`}>
            {currentUrl
              ? <img src={artSrc(currentUrl, 128)} srcSet={artSrcSet(currentUrl, [96, 192])} sizes={isGrid ? '84px' : '56px'}
                  alt={`Current ${LABEL[kind].noun}`} className="w-full h-full object-cover" />
              : <span className="text-[10px] text-dim">none</span>}
          </span>
          <figcaption className="text-[10px] text-dim">Current</figcaption>
        </figure>

        <span aria-hidden className="self-stretch w-px bg-line" />

        {loading && Array.from({ length: 5 }, (_, i) => (
          <span key={i} aria-hidden className={`${tile} rounded-[9px] bg-raise border border-line animate-pulse`} />
        ))}

        {error && (
          <div className="flex items-center gap-3 min-h-[56px]">
            <span className="text-xs text-dim">{error}</span>
            <Button size="sm" onClick={() => setAttempt(a => a + 1)}>Retry</Button>
          </div>
        )}

        {data && data.options.length === 0 && (
          <span className="text-xs text-dim self-center">
            {page === 0 ? `SteamGridDB has no ${LABEL[kind].noun}s for this game.` : 'No more options.'}
          </span>
        )}

        {data?.options.map(o => {
          const saving = applying === `${kind}:${o.url}`;
          return (
            <button
              key={o.url}
              type="button"
              disabled={busy}
              onClick={() => onChoose(o)}
              title={o.author ? `by ${o.author}` : undefined}
              aria-label={`Use this ${LABEL[kind].noun}${o.author ? ` by ${o.author}` : ''}`}
              className="bg-transparent border-0 p-0 flex flex-col items-center gap-1 cursor-pointer font-[inherit]
                transition-transform duration-150 ease-[var(--ease)] hover:-translate-y-px active:scale-[.97]
                disabled:cursor-default disabled:hover:translate-y-0 disabled:active:scale-100 disabled:opacity-60
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2 rounded-[9px]"
            >
              <span className={`${tile} rounded-[9px] overflow-hidden bg-raise border border-line grid place-items-center hover:border-dim`}>
                {o.preview
                  ? <img src={o.preview} alt="" className="w-full h-full object-cover" />
                  : <span className="text-[9px] text-dim">no preview</span>}
              </span>
              <span className="text-[10px] text-dim tabular-nums">
                {saving ? 'Saving…' : o.width && o.height ? `${o.width}×${o.height}` : ' '}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
