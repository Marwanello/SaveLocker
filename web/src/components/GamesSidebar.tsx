import { useState } from 'react';
import type { GameSummary } from '../types';
import { Row } from './ui/Row';
import { Chip } from './ui/Chip';
import { Seg } from './ui/Seg';
import { Button } from './ui/Button';
import { GamesGrid } from './GamesGrid';
import { artSrc, artSrcSet } from '../art';

interface Props {
  games: GameSummary[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onAddGame: () => void;
  onRefresh: () => void;
}

type Layout = 'list' | 'grid';
const LAYOUT_KEY = 'sl_games_layout';

function loadLayout(): Layout {
  try { return localStorage.getItem(LAYOUT_KEY) === 'grid' ? 'grid' : 'list'; }
  catch { return 'list'; }
}

const fmtMb = (bytes: number) => (bytes / (1024 * 1024)).toFixed(1) + ' MB';

export function GamesSidebar({ games, selectedId, onSelect, onAddGame, onRefresh }: Props) {
  const [layout, setLayout] = useState<Layout>(loadLayout);
  const grandTotal = games.reduce((sum, s) => sum + s.totalStorageBytes, 0);

  function changeLayout(v: Layout) {
    setLayout(v);
    try { localStorage.setItem(LAYOUT_KEY, v); } catch { /* private browsing, etc — not fatal */ }
  }

  return (
    <aside className={`flex-shrink-0 bg-panel border-r border-line flex flex-col min-h-0 ${layout === 'grid' ? 'w-[340px]' : 'w-[260px]'}`}>
      <div className="px-3.5 py-2.5 border-b border-line flex items-baseline justify-between flex-shrink-0">
        <span className="text-[10px] font-bold text-accent tracking-[0.12em] uppercase">Games</span>
        <span className="text-[9.5px] text-dim font-mono" title="Total save data stored on server">{fmtMb(grandTotal)}</span>
      </div>

      {/* Action buttons — anchored below header, always visible */}
      <div className="flex-shrink-0 border-b border-line p-2 flex flex-col gap-2">
        <div className="flex gap-1.5">
          <Button variant="primary" className="flex-1 text-center" onClick={onAddGame}>+ Add game</Button>
          <Button variant="default" onClick={onRefresh} title="Refresh">↻</Button>
        </div>
        <Seg value={layout} onChange={changeLayout} aria-label="Games layout" className="self-start"
          options={[{ value: 'list', label: 'List' }, { value: 'grid', label: 'Grid' }]} />
      </div>

      <div className="flex-1 overflow-y-auto min-h-0">
        {games.length === 0 && (
          <div className="p-3.5 text-xs text-dim">No games tracked yet.</div>
        )}

        {layout === 'grid' ? (
          <GamesGrid games={games} selectedId={selectedId} onSelect={onSelect} />
        ) : (
          <div className="flex flex-col gap-1.5 p-1.5">
            {games.map(s => {
              const { game, head, hasOpenConflict, lease, totalStorageBytes } = s;
              const art = game.iconUrl || game.gridUrl;
              return (
                <Row
                  key={game.id}
                  onClick={() => onSelect(game.id)}
                  className={`${game.id === selectedId ? 'border-accent' : ''} ${game.enabled ? '' : 'opacity-[.55]'}`}
                  // The row's cover is a 38 px SQUARE, so it shows the game's icon — box art is 2:3 and
                  // has to be cropped to fit. The cover is only the fallback for a game with no icon.
                  cover={
                    art
                      ? <img src={artSrc(art, 64)} srcSet={artSrcSet(art, [64, 128])} sizes="38px"
                          alt="" loading="lazy" decoding="async" className="w-full h-full object-cover" />
                      : <span className="text-dim text-[8px] font-mono">no art</span>
                  }
                  title={game.name}
                  subtext={[
                    totalStorageBytes > 0 ? fmtMb(totalStorageBytes) : null,
                    head ? head.id.replace(/-/g, '').slice(0, 6) : null,
                    lease?.holderMachineName ? `leased by ${lease.holderMachineName}` : null,
                  ].filter(Boolean).join(' · ') || '—'}
                  // A healthy game is the normal case, so it gets a quiet dot rather than a word: the
                  // "in sync" chip took a quarter of the row and left the title ~60px (measured) —
                  // most names were cut to about eight characters. Only a conflict earns a chip.
                  end={
                    hasOpenConflict
                      ? <Chip tone="crit">conflict</Chip>
                      : <span className="w-2 h-2 rounded-full bg-safe" role="img" aria-label="in sync" title="in sync" />
                  }
                />
              );
            })}
          </div>
        )}
      </div>
    </aside>
  );
}
