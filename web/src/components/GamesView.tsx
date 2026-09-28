import { useEffect, useRef, useState } from 'react';
import type { GameSummary, Machine, Command, Conflict, AgentHealth, GameIntent, GameRequest } from '../types';
import { GamesSidebar } from './GamesSidebar';
import { GameDetail } from './game/GameDetail';
import { Button } from './ui/Button';

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

export function GamesView({ games, machines, commands, conflicts, onRefresh, onAddGame, request, onRequestHandled }: Props) {
  const [selectedId, setSelectedId] = useState<string | null>(
    games.length > 0 ? games[0].game.id : null
  );
  // Where on the game's page a deep link asked to land, with the request's sequence number so the
  // page acts on each request exactly once (and again if the same one is made twice).
  const [intent, setIntent] = useState<{ value: GameIntent; seq: number } | null>(null);

  // The callback is read through a ref so this effect fires once per incoming request and not on
  // every render the parent happens to re-create the callback in.
  const handledRef = useRef(onRequestHandled);
  useEffect(() => { handledRef.current = onRequestHandled; });
  useEffect(() => {
    if (!request) return;
    setSelectedId(request.id);
    setIntent(request.intent ? { value: request.intent, seq: request.seq } : null);
    handledRef.current?.();
  }, [request]);

  // Keep selectedId in sync when games list changes (e.g., a game is deleted).
  const validIds = new Set(games.map(s => s.game.id));
  const activeId = selectedId && validIds.has(selectedId) ? selectedId : (games[0]?.game.id ?? null);

  const selectedSummary = games.find(s => s.game.id === activeId) ?? null;

  if (games.length === 0) {
    return (
      // The "+ Add game" button lives in the sidebar, which is not rendered while there are no games —
      // this empty state used to tell a fresh install to click a button that was not on the screen.
      <div className="flex-1 flex flex-col gap-3.5 items-center justify-center text-dim text-sm">
        <span>No games tracked yet.</span>
        <Button variant="primary" onClick={onAddGame}>+ Add game</Button>
      </div>
    );
  }

  return (
    <div className="flex-1 flex min-h-0 overflow-hidden">
      <GamesSidebar games={games} selectedId={activeId} onSelect={id => { setSelectedId(id); setIntent(null); }} onAddGame={onAddGame} onRefresh={onRefresh} />

      <main className="flex-1 overflow-y-auto px-6 py-5">
        {selectedSummary ? (
          <GameDetail
            key={selectedSummary.game.id}
            summary={selectedSummary}
            machines={machines}
            commands={commands}
            conflicts={conflicts}
            onRefresh={onRefresh}
            intent={intent}
          />
        ) : (
          <div className="text-dim text-sm mt-10 text-center">Select a game from the sidebar.</div>
        )}
      </main>
    </div>
  );
}
