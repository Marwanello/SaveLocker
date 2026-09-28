import { useEffect, useMemo, useRef, useState } from 'react';
import type { GameSummary, Machine, Command, Conflict, AgentHealth, GameIntent, GameRequest } from '../types';
import { plural } from '../format';
import { GamesSidebar } from './GamesSidebar';
import { GamesGrid } from './GamesGrid';
import { GameDetail } from './game/GameDetail';
import { LAYOUT_OPTIONS, problemGameIds } from './game/gameState';
import type { Layout } from './game/gameState';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Card } from './ui/Card';
import { EmptyState } from './ui/EmptyState';
import { Button } from './ui/Button';
import { Seg } from './ui/Seg';
import { Icon } from './ui/Icon';

const LAYOUT_KEY = 'sl_games_layout';

function loadLayout(): Layout {
  try { return localStorage.getItem(LAYOUT_KEY) === 'grid' ? 'grid' : 'list'; }
  catch { return 'list'; }
}

interface Props {
  games: GameSummary[];
  machines: Machine[];
  commands: Command[];
  conflicts: Conflict[];
  health: AgentHealth[];
  onRefresh: () => void;
  onAddGame: () => void;
  /** A deep link (a notification, the conflict pill): open this game, at this place, once — then
   *  report it consumed. `null` means no pending request. */
  request?: GameRequest | null;
  onRequestHandled?: () => void;
}

/**
 * plan.md Phase 10: list (sidebar + the game's page) for working on one game, grid (a full-width wall)
 * for finding one — opening a tile shows the same page with "All games" to go back.
 */
export function GamesView({ games, machines, commands, conflicts, health, onRefresh, onAddGame, request, onRequestHandled }: Props) {
  const [layout, setLayout] = useState<Layout>(loadLayout);
  const [selectedId, setSelectedId] = useState<string | null>(games[0]?.game.id ?? null);
  // In the grid layout: whether the wall or one game's page is showing.
  const [gridOpen, setGridOpen] = useState(false);
  // Where on the game's page a deep link asked to land, with the request's sequence number so the
  // page acts on each request exactly once (and again if the same one is made twice).
  const [intent, setIntent] = useState<{ value: GameIntent; seq: number } | null>(null);
  const problems = useMemo(() => problemGameIds(health), [health]);

  function changeLayout(v: Layout) {
    setLayout(v);
    setGridOpen(false);
    try { localStorage.setItem(LAYOUT_KEY, v); } catch { /* private browsing, etc — not fatal */ }
  }

  function open(id: string) {
    setSelectedId(id);
    setIntent(null);
    setGridOpen(true);
  }

  // The callback is read through a ref so this effect fires once per incoming request and not on
  // every render the parent happens to re-create the callback in.
  const handledRef = useRef(onRequestHandled);
  useEffect(() => { handledRef.current = onRequestHandled; });
  useEffect(() => {
    if (!request) return;
    setSelectedId(request.id);
    setGridOpen(true);
    setIntent(request.intent ? { value: request.intent, seq: request.seq } : null);
    handledRef.current?.();
  }, [request]);

  // A deleted game falls back to the first one rather than to an empty page.
  const selected = games.find(s => s.game.id === selectedId) ?? games[0] ?? null;

  if (games.length === 0) {
    return (
      <Page>
        <PageHead title="Games" sub="Nothing tracked yet" />
        <Card>
          <EmptyState
            title="No games tracked yet"
            action={<Button variant="primary" onClick={onAddGame}><Icon name="plus" size={13} strokeWidth={2.4} />Add game</Button>}
          >
            Add one here, or from an agent's Add games screen. Its saves appear here after the first upload.
          </EmptyState>
        </Card>
      </Page>
    );
  }

  const page = selected && (
    <GameDetail
      key={selected.game.id}
      summary={selected}
      machines={machines}
      commands={commands}
      conflicts={conflicts}
      health={health}
      onRefresh={onRefresh}
      intent={intent}
    />
  );

  if (layout === 'grid') {
    return (
      // Keyed, so the canvas's entrance plays when the wall gives way to a game and back.
      <Page key={gridOpen && selected ? selected.game.id : 'wall'}>
        {gridOpen && selected ? (
          <>
            <div>
              <Button size="sm" onClick={() => setGridOpen(false)}><Icon name="arrow-left" size={13} />All games</Button>
            </div>
            {page}
          </>
        ) : (
          <>
            <PageHead
              title="Games"
              sub={`${plural(games.length, 'game')} tracked · art from SteamGridDB`}
              actions={<>
                <Seg size="sm" value={layout} onChange={changeLayout} aria-label="Games layout" options={LAYOUT_OPTIONS} />
                <Button onClick={onAddGame}><Icon name="plus" size={13} strokeWidth={2.4} />Add game</Button>
              </>}
            />
            <GamesGrid games={games} onOpen={open} problems={problems} />
          </>
        )}
      </Page>
    );
  }

  return (
    <div className="flex-1 min-h-0 grid grid-cols-[250px_minmax(0,1fr)] overflow-hidden">
      <GamesSidebar
        games={games}
        selectedId={selected?.game.id ?? null}
        onSelect={id => { setSelectedId(id); setIntent(null); }}
        onAddGame={onAddGame}
        layout={layout}
        onLayoutChange={changeLayout}
        commands={commands}
        problems={problems}
      />
      <Page key={selected?.game.id}>{page}</Page>
    </div>
  );
}
