import { useEffect, useMemo, useRef, useState } from 'react';
import type { GameSummary, Machine, Command, Conflict, AgentHealth, GameIntent, GameRequest } from '../types';
import { plural } from '../format';
import { GamesSidebar } from './GamesSidebar';
import { GamesGrid } from './GamesGrid';
import { GameDetail } from './game/GameDetail';
import { LAYOUT_OPTIONS, problemGameIds } from './game/gameState';
import type { Layout } from './game/gameState';
import { DEFAULT_ORGANIZE, GROUP_OPTIONS, SORT_OPTIONS, groupLabel, organize } from './game/organize';
import type { GroupBy, OrganizeState, SortBy } from './game/organize';
import { OrganizeControls } from './game/OrganizeControls';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Card } from './ui/Card';
import { EmptyState } from './ui/EmptyState';
import { Button } from './ui/Button';
import { Seg } from './ui/Seg';
import { Icon } from './ui/Icon';

const LAYOUT_KEY = 'sl_games_layout';
const ORGANIZE_KEY = 'sl_games_organize';

function loadLayout(): Layout {
  try { return localStorage.getItem(LAYOUT_KEY) === 'grid' ? 'grid' : 'list'; }
  catch { return 'list'; }
}

/** The saved arrangement, validated value by value — a stale shape (a removed group, a hand-edited
 *  localStorage) falls back to today's screen rather than rendering nothing. */
function loadOrganize(): OrganizeState {
  try {
    const raw = localStorage.getItem(ORGANIZE_KEY);
    if (!raw) return DEFAULT_ORGANIZE;
    const p = JSON.parse(raw) as Partial<OrganizeState>;
    return {
      groupBy: GROUP_OPTIONS.some(o => o.value === p.groupBy) ? p.groupBy as GroupBy : 'none',
      sortBy: SORT_OPTIONS.some(o => o.value === p.sortBy) ? p.sortBy as SortBy : 'name',
      dir: p.dir === -1 ? -1 : 1,
    };
  } catch { return DEFAULT_ORGANIZE; }
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
  const [organizeState, setOrganizeState] = useState<OrganizeState>(loadOrganize);
  // Collapsed headings live for one visit only: group ids change with the arrangement, so a persisted
  // set would silently apply to different headings after a reload.
  const [collapsed, setCollapsed] = useState<Set<string>>(() => new Set());
  const [selectedId, setSelectedId] = useState<string | null>(games[0]?.game.id ?? null);
  // In the grid layout: whether the wall or one game's page is showing.
  const [gridOpen, setGridOpen] = useState(false);
  // Where on the game's page a deep link asked to land, with the request's sequence number so the
  // page acts on each request exactly once (and again if the same one is made twice).
  const [intent, setIntent] = useState<{ value: GameIntent; seq: number } | null>(null);
  const problems = useMemo(() => problemGameIds(health), [health]);
  const groups = useMemo(
    () => organize(games, organizeState, { machines, problems }),
    [games, organizeState, machines, problems]);
  const grouped = organizeState.groupBy !== 'none';

  function changeLayout(v: Layout) {
    setLayout(v);
    setGridOpen(false);
    try { localStorage.setItem(LAYOUT_KEY, v); } catch { /* private browsing, etc — not fatal */ }
  }

  function changeOrganize(v: OrganizeState) {
    setOrganizeState(v);
    setCollapsed(new Set());
    try { localStorage.setItem(ORGANIZE_KEY, JSON.stringify(v)); } catch { /* not fatal */ }
  }

  function toggleGroup(id: string) {
    setCollapsed(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
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
              sub={`${plural(games.length, 'game')} tracked${grouped ? ` · grouped by ${groupLabel(organizeState.groupBy).toLowerCase()}` : ''} · art from SteamGridDB`}
              actions={<>
                <Seg size="sm" value={layout} onChange={changeLayout} aria-label="Games layout" options={LAYOUT_OPTIONS} />
                <OrganizeControls value={organizeState} onChange={changeOrganize} stretch={false} />
                <Button onClick={onAddGame}><Icon name="plus" size={13} strokeWidth={2.4} />Add game</Button>
              </>}
            />
            <GamesGrid
              groups={groups}
              grouped={grouped}
              collapsed={collapsed}
              onToggleGroup={toggleGroup}
              onOpen={open}
              problems={problems}
            />
          </>
        )}
      </Page>
    );
  }

  return (
    <div className="flex-1 min-h-0 grid grid-cols-[250px_minmax(0,1fr)] overflow-hidden">
      <GamesSidebar
        games={games}
        groups={groups}
        grouped={grouped}
        collapsed={collapsed}
        onToggleGroup={toggleGroup}
        controls={<OrganizeControls value={organizeState} onChange={changeOrganize} />}
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
