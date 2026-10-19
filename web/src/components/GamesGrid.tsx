import { Fragment } from 'react';
import type { GameSummary } from '../types';
import { ago, fmtSize } from '../format';
import { Chip } from './ui/Chip';
import { Dot } from './ui/Dot';
import { Icon } from './ui/Icon';
import { PlatformIcon } from './ui/PlatformIcon';
import { GameCover } from './game/GameCover';
import { regionLabel } from './game/gameState';
import { standing } from './game/gameState';
import type { OrganizeGroup } from './game/organize';

interface Props {
  /** The games as headings, sorted — from `organize()`; 'none' grouping yields one unlabelled group. */
  groups: OrganizeGroup[];
  /** False for "no grouping": band headings are skipped, the wall reads exactly as it always has. */
  grouped: boolean;
  collapsed: Set<string>;
  onToggleGroup: (id: string) => void;
  onOpen: (id: string) => void;
  problems: Set<string>;
}

/** plan.md Phase 10.2 + the sorting/grouping mockup (Option A): the grid wall, full width — for
 *  finding a game. 3:4 covers inset 8 px, a Conflict / Retrying chip pinned top-right, the `pop`
 *  stagger (`.tile-wall`) and a 4 px lift. A grouped wall draws a sticky band heading per group and
 *  per-emulator sub-headings where a console was reached through more than one emulator. */
export function GamesGrid({ groups, grouped, collapsed, onToggleGroup, onOpen, problems }: Props) {
  function tile(s: GameSummary) {
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
              {game.release && regionLabel(game.release) && <>{regionLabel(game.release)}<span aria-hidden>·</span></>}
              {head ? ago(head.createdAt) : 'no saves yet'}
              <span aria-hidden>·</span>
              {fmtSize(totalStorageBytes)}
            </span>
          </span>
        </span>
      </button>
    );
  }

  return (
    <div className="flex flex-col gap-7">
      {groups.map(g => {
        const count = g.subGroups.reduce((n, sg) => n + sg.entries.length, 0);
        const isCollapsed = collapsed.has(g.id);
        return (
          <section key={g.id} className="flex flex-col gap-3.5">
            {grouped && (
              <button
                type="button"
                onClick={() => onToggleGroup(g.id)}
                aria-expanded={!isCollapsed}
                className="sticky top-0 z-[3] flex items-center gap-2.5 text-left py-1.5 bg-ink cursor-pointer focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
              >
                <Icon name="chevron-down" size={13} strokeWidth={2.4}
                  className={`shrink-0 text-faint transition-transform duration-150 ${isCollapsed ? '-rotate-90' : ''}`} />
                <PlatformIcon id={g.icon ?? null} size={1.15} className="text-dim" />
                <span className="text-[13px] font-semibold tracking-[-0.01em]">{g.label || 'All games'}</span>
                <span className="text-[11px] text-dim tabular-nums">{count === 1 ? '1 game' : `${count} games`}</span>
              </button>
            )}
            {!isCollapsed && g.subGroups.map(sg => (
              <Fragment key={sg.id}>
                {sg.label && (
                  <div className="flex items-center gap-2 -mb-1.5 text-[10px] tracking-[0.12em] uppercase text-faint">
                    <PlatformIcon id={sg.icon ?? null} size={1.3} />
                    <span>{sg.label}</span>
                    <span className="text-faint tabular-nums">{sg.entries.length}</span>
                  </div>
                )}
                <div className="tile-wall grid grid-cols-[repeat(auto-fill,minmax(184px,1fr))] gap-[18px]">
                  {sg.entries.map(tile)}
                </div>
              </Fragment>
            ))}
          </section>
        );
      })}
    </div>
  );
}
