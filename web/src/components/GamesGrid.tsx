import type { GameSummary } from '../types';
import { Chip } from './ui/Chip';

interface Props {
  games: GameSummary[];
  selectedId: string | null;
  onSelect: (id: string) => void;
}

/** plan.md "Layout rules": grid is for finding a game, list (GamesSidebar's own rows) is for
 *  working on one. Cover art at 3:4, `pop` entrance staggered on the first six tiles only. */
export function GamesGrid({ games, selectedId, onSelect }: Props) {
  return (
    <div className="grid grid-cols-2 gap-3 p-3">
      {games.map((s, i) => {
        const { game, hasOpenConflict } = s;
        const isSelected = game.id === selectedId;
        return (
          <button
            key={game.id}
            onClick={() => onSelect(game.id)}
            className={`animate-pop flex flex-col gap-1.5 rounded-[9px] border p-1.5 text-left
              ${isSelected ? 'border-accent bg-raise' : 'border-line bg-panel'}`}
            style={{ opacity: game.enabled ? 1 : 0.55, animationDelay: `${Math.min(i, 5) * 35}ms` }}
          >
            <div className="aspect-[3/4] w-full rounded-[6px] overflow-hidden bg-raise border border-line flex items-center justify-center">
              {game.gridUrl
                ? <img src={game.gridUrl} alt="" className="w-full h-full object-cover" />
                : <span className="text-faint text-[10px] font-mono">no art</span>
              }
            </div>
            <div className="text-xs font-semibold text-fg truncate">{game.name}</div>
            {hasOpenConflict && <Chip tone="crit" className="self-start">conflict</Chip>}
          </button>
        );
      })}
    </div>
  );
}
