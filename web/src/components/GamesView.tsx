import { useEffect, useRef, useState } from 'react';
import type { GameSummary, Machine, Command, Conflict } from '../types';
import { GamesSidebar } from './GamesSidebar';
import { GameDetail } from './GameDetail';
import { Button } from './ui/Button';

interface Props {
  games: GameSummary[];
  machines: Machine[];
  commands: Command[];
  conflicts: Conflict[];
  onRefresh: () => void;
  onAddGame: () => void;
  /** Set by a notification's deep link (a conflict or a missing save dir) — select this game once,
   *  then report it consumed. `null`/undefined means no pending request. */
  selectGameId?: string | null;
  onSelectGameHandled?: () => void;
}

export function GamesView({ games, machines, commands, conflicts, onRefresh, onAddGame, selectGameId, onSelectGameHandled }: Props) {
  const [selectedId, setSelectedId] = useState<string | null>(
    games.length > 0 ? games[0].game.id : null
  );

  // The callback is read through a ref so this effect fires once per incoming request (`selectGameId`
  // changing) and not on every render the parent happens to re-create the callback in.
  const handledRef = useRef(onSelectGameHandled);
  useEffect(() => { handledRef.current = onSelectGameHandled; });
  useEffect(() => {
    if (!selectGameId) return;
    setSelectedId(selectGameId);
    handledRef.current?.();
  }, [selectGameId]);

  // Keep selectedId in sync when games list changes (e.g., a game is deleted).
  const validIds = new Set(games.map(s => s.game.id));
  const activeId = selectedId && validIds.has(selectedId) ? selectedId : (games[0]?.game.id ?? null);

  const selectedSummary = games.find(s => s.game.id === activeId) ?? null;

  if (games.length === 0) {
    return (
      // The "+ Add game" button lives in the sidebar, which is not rendered while there are no games —
      // this empty state used to tell a fresh install to click a button that was not on the screen.
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 14, alignItems: 'center', justifyContent: 'center', color: 'var(--color-dim)', fontSize: 14 }}>
        <span>No games tracked yet.</span>
        <Button variant="primary" onClick={onAddGame}>+ Add game</Button>
      </div>
    );
  }

  return (
    <div style={{ flex: 1, display: 'flex', minHeight: 0, overflow: 'hidden' }}>
      <GamesSidebar games={games} selectedId={activeId} onSelect={id => setSelectedId(id)} onAddGame={onAddGame} onRefresh={onRefresh} />

      <main style={{ flex: 1, overflowY: 'auto', padding: '20px 24px' }}>
        {selectedSummary ? (
          <GameDetail
            key={selectedSummary.game.id}
            summary={selectedSummary}
            machines={machines}
            commands={commands}
            conflicts={conflicts}
            onRefresh={onRefresh}
          />
        ) : (
          <div style={{ color: 'var(--color-dim)', fontSize: 14, marginTop: 40, textAlign: 'center' }}>Select a game from the sidebar.</div>
        )}
      </main>
    </div>
  );
}
