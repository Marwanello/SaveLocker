import { Fragment } from 'react';
import type { ReactNode } from 'react';
import type { Command, GameSummary } from '../types';
import { ago, fmtSize } from '../format';
import { Seg } from './ui/Seg';
import { Button } from './ui/Button';
import { Chip } from './ui/Chip';
import { Dot } from './ui/Dot';
import { Icon } from './ui/Icon';
import { GameCover } from './game/GameCover';
import { regionLabel } from './game/gameState';
import { LAYOUT_OPTIONS, liveCommand, standing } from './game/gameState';
import type { Layout } from './game/gameState';
import type { OrganizeGroup } from './game/organize';

interface Props {
  games: GameSummary[];
  /** The games as headings, sorted — from `organize()`; 'none' grouping yields one unlabelled group. */
  groups: OrganizeGroup[];
  /** False for "no grouping": headings are skipped and the list reads exactly as it always has. */
  grouped: boolean;
  collapsed: Set<string>;
  onToggleGroup: (id: string) => void;
  /** The Group/Sort dropdowns, rendered in the header under the count + layout switch. */
  controls?: ReactNode;
  selectedId: string | null;
  onSelect: (id: string) => void;
  onAddGame: () => void;
  layout: Layout;
  onLayoutChange: (v: Layout) => void;
  commands: Command[];
  /** Games a machine has an open problem about — see gameState.problemGameIds. */
  problems: Set<string>;
}

/** plan.md Phase 10.1 + the sorting/grouping mockup (Option A): the list is for working on one game —
 *  two-line rows, the current one in the soft accent, a state dot (or the live chip of a command queued
 *  for that game) at the end. With an arrangement on, sticky group headings (count + collapse) sit
 *  above the rows; the rows themselves are untouched. */
export function GamesSidebar({ games, groups, grouped, collapsed, onToggleGroup, controls, selectedId, onSelect, onAddGame, layout, onLayoutChange, commands, problems }: Props) {
  function row(s: GameSummary) {
    const { game, head, totalStorageBytes } = s;
    const current = game.id === selectedId;
    const st = standing(s, problems);
    const live = liveCommand(game.id, commands);
    // Two releases of one game share its name; the region tells them apart.
    const region = game.release ? regionLabel(game.release) : null;
    return (
      <button
        key={game.id}
        type="button"
        onClick={() => onSelect(game.id)}
        aria-current={current ? 'true' : undefined}
        title={game.name}
        className={`grid grid-cols-[auto_minmax(0,1fr)_auto] grid-rows-[auto_auto] gap-x-[11px] gap-y-[3px] w-full text-left
          px-2.5 py-[9px] rounded-[11px] border cursor-pointer text-fg
          transition-[background-color,border-color,transform] duration-150 ease-[var(--ease)] hover:translate-x-0.5 hover:opacity-100
          focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1
          ${current ? 'bg-accent-soft border-accent-line' : 'bg-transparent border-transparent hover:bg-raise'}
          ${game.enabled ? '' : 'opacity-[.55]'}`}
      >
        <span className="row-span-2 self-center"><GameCover game={game} size="s" /></span>
        <span className="col-start-2 row-start-1 self-end text-[13px] font-semibold tracking-[-0.015em] truncate">{game.name}</span>
        <span className="col-start-2 row-start-2 self-start text-[10.5px] text-dim truncate tabular-nums">
          {region ? `${region} · ` : ''}{head ? ago(head.createdAt) : 'no saves yet'} · {fmtSize(totalStorageBytes)}
        </span>
        <span className="col-start-3 row-start-1 row-span-2 self-center flex items-center">
          {live
            ? <Chip tone="crit" className="!px-2 !py-0.5"><Dot tone="crit" live />{live.label}</Chip>
            : <Dot tone={st.tone} label={st.label} />}
        </span>
      </button>
    );
  }

  return (
    <aside aria-label="Games" className="bg-panel border-r border-line flex flex-col min-h-0">
      <header className="px-3.5 py-[13px] border-b border-line">
        <div className="flex items-center justify-between gap-2">
          <h4 className="text-[10px] tracking-[0.14em] uppercase text-faint font-normal tabular-nums">Games · {games.length}</h4>
          <Seg size="sm" value={layout} onChange={onLayoutChange} aria-label="Games layout" options={LAYOUT_OPTIONS} />
        </div>
        {controls}
      </header>

      <div className="flex-1 min-h-0 overflow-y-auto p-1.5 pb-2 flex flex-col">
        {groups.map(g => {
          const count = g.subGroups.reduce((n, sg) => n + sg.entries.length, 0);
          const isCollapsed = collapsed.has(g.id);
          return (
            <Fragment key={g.id}>
              {grouped && (
                <button
                  type="button"
                  onClick={() => onToggleGroup(g.id)}
                  aria-expanded={!isCollapsed}
                  className="sticky top-0 z-[2] flex items-center gap-2 w-full text-left px-2 pt-2.5 pb-1.5 bg-panel cursor-pointer focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-[-2px]"
                >
                  <Icon name="chevron-down" size={12} strokeWidth={2.4}
                    className={`shrink-0 text-faint transition-transform duration-150 ${isCollapsed ? '-rotate-90' : ''}`} />
                  <span className="text-[9.5px] tracking-[0.14em] uppercase text-faint truncate">{g.label || 'All games'}</span>
                  <span className="ml-auto shrink-0 text-[9.5px] tabular-nums text-faint bg-tile border border-line rounded-full px-1.5 py-px">{count}</span>
                </button>
              )}
              {!isCollapsed && g.subGroups.map(sg => (
                <Fragment key={sg.id}>
                  {sg.label && (
                    <div className="flex items-center gap-2 pl-4 pr-1 pt-1.5 text-[10px] font-medium text-dim">
                      <span className="truncate">{sg.label}</span>
                      <span className="text-[9.5px] text-faint tabular-nums">{sg.entries.length}</span>
                    </div>
                  )}
                  {sg.entries.map(row)}
                </Fragment>
              ))}
            </Fragment>
          );
        })}
      </div>

      <footer className="px-3 py-2.5 border-t border-line">
        <Button className="w-full" onClick={onAddGame}><Icon name="plus" size={13} strokeWidth={2.4} />Add game</Button>
      </footer>
    </aside>
  );
}
