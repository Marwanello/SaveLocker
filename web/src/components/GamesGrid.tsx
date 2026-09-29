import type { GameSummary } from '../types';
import { ago, fmtSize } from '../format';
import { Chip } from './ui/Chip';
import { Dot } from './ui/Dot';
import { GameCover } from './game/GameCover';
import { standing } from './game/gameState';

interface Props {
  games: GameSummary[];
  onOpen: (id: string) => void;
  problems: Set<string>;
}

/** plan.md Phase 10.2: the grid wall, full width — for finding a game. 3:4 covers inset 8 px, a
 *  Conflict / Retrying chip pinned top-right, the `pop` stagger (`.tile-wall`) and a 4 px lift. */
export function GamesGrid({ games, onOpen, problems }: Props) {
  return (
    <div className="tile-wall grid grid-cols-[repeat(auto-fill,minmax(184px,1fr))] gap-[18px]">
      {games.map(s => {
        const { game, head, totalStorageBytes } = s;
        const st = standing(s, problems);
        return (
          <button
            key={game.id}
            type="button"
            onClick={() => onOpen(game.id)}
            title={game.name}
            className="block text-left p-0 rounded-2xl border border-line bg-panel overflow-hidden cursor-pointer text-fg
              transition-[transform,border-color,box-shadow] duration-200 ease-[var(--ease)]
              hover:-translate-y-1 hover:border-accent-line hover:shadow-xl hover:opacity-100 active:-translate-y-px active:scale-[.99]
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
          >
            <span className={`block ${game.enabled ? '' : 'opacity-[.55]'}`}>
              <span className="relative block px-2 pt-2">
                <GameCover game={game} size="m" />
                {(st.tone === 'crit' || st.tone === 'warn') && (
                  <span className="absolute top-[15px] right-[15px] z-[2]">
                    <Chip tone={st.tone === 'crit' ? 'crit' : 'warn'}>{st.tone === 'crit' ? 'Conflict' : 'Retrying'}</Chip>
                  </span>
                )}
              </span>
              <span className="flex flex-col gap-1.5 px-3.5 pt-3 pb-3.5 min-w-0">
                <span className="text-[13.5px] font-semibold tracking-[-0.02em] truncate">{game.name}</span>
                <span className="flex items-center gap-[7px] text-[10.5px] text-dim truncate tabular-nums">
                  <Dot tone={st.tone} label={st.label} />
                  {head ? ago(head.createdAt) : 'no saves yet'}
                  <span aria-hidden>·</span>
                  {fmtSize(totalStorageBytes)}
                </span>
              </span>
            </span>
          </button>
        );
      })}
    </div>
  );
}
