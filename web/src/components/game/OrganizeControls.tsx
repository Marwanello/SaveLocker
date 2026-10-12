import { useEffect, useRef, useState } from 'react';
import { DEFAULT_DIR, GROUP_OPTIONS, SORT_OPTIONS, dirLabel, groupLabel, sortLabel } from './organize';
import type { GroupBy, OrganizeState, SortBy } from './organize';
import { Icon } from '../ui/Icon';

interface Props {
  value: OrganizeState;
  onChange: (v: OrganizeState) => void;
  /** Fill the sidebar's row width (the default); the page head keeps natural widths. */
  stretch?: boolean;
}

const BUTTON = `inline-flex items-center gap-1.5 flex-1 min-w-0 text-[11.5px] font-medium
  px-2.5 py-[6px] rounded-lg border border-line bg-raise cursor-pointer
  transition-colors duration-150 ease-[var(--ease)] hover:bg-hover
  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-1`;

const ITEM = `flex w-full items-center gap-2 text-left text-[12.5px] px-2 py-[7px]
  rounded-lg text-fg cursor-pointer hover:bg-raise focus-visible:outline focus-visible:outline-2
  focus-visible:outline-accent focus-visible:outline-offset-[-2px]`;

const MENU = 'animate-drop absolute left-0 top-[calc(100%+6px)] z-30 p-1.5 bg-panel border border-line rounded-xl shadow-2xl';

/**
 * Option A of tasks/console-games-sort-group: the Group and Sort dropdowns, always visible â€” in the
 * sidebar header (list layout) or the page head (grid layout). Purely a view preference; the choice is
 * persisted by GamesView, and picking a group resets the collapsed headings there.
 */
export function OrganizeControls({ value, onChange, stretch = true }: Props) {
  const [open, setOpen] = useState<'group' | 'sort' | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const groupRef = useRef<HTMLButtonElement>(null);
  const sortRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(null); };
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return;
      (open === 'group' ? groupRef : sortRef).current?.focus();
      setOpen(null);
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  function pickGroup(groupBy: GroupBy) { onChange({ ...value, groupBy }); setOpen(null); }
  function pickSort(sortBy: SortBy) { onChange({ ...value, sortBy, dir: DEFAULT_DIR[sortBy] }); setOpen(null); }

  return (
    <div ref={rootRef} className={stretch ? 'flex gap-[7px] mt-[9px]' : 'flex gap-2'}>
      <span className={stretch ? 'relative flex flex-1 min-w-0' : 'relative flex'}>
        <button
          ref={groupRef}
          type="button"
          className={BUTTON}
          aria-haspopup="menu"
          aria-expanded={open === 'group'}
          title="Group the games"
          onClick={() => setOpen(o => (o === 'group' ? null : 'group'))}
        >
          <span className="text-faint">Group</span>
          <span className="font-semibold truncate">{groupLabel(value.groupBy)}</span>
          <Icon name="chevron-down" size={12} strokeWidth={2.2}
            className={`ml-auto shrink-0 text-faint transition-transform duration-150 ${open === 'group' ? 'rotate-180' : ''}`} />
        </button>
        {open === 'group' && (
          <div role="menu" aria-label="Group games by" className={`${MENU} w-[212px]`}>
            {GROUP_OPTIONS.map(o => (
              <button key={o.value} type="button" role="menuitemradio" aria-checked={o.value === value.groupBy}
                className={ITEM} onClick={() => pickGroup(o.value)}>
                <span className="w-3.5 shrink-0 text-accent">
                  {o.value === value.groupBy && <Icon name="check" size={13} strokeWidth={2.6} />}
                </span>
                {o.label}
              </button>
            ))}
          </div>
        )}
      </span>

      <span className={stretch ? 'relative flex flex-1 min-w-0' : 'relative flex'}>
        <button
          ref={sortRef}
          type="button"
          className={BUTTON}
          aria-haspopup="menu"
          aria-expanded={open === 'sort'}
          title="Sort the games"
          onClick={() => setOpen(o => (o === 'sort' ? null : 'sort'))}
        >
          <span className="text-faint">Sort</span>
          <span className="font-semibold truncate">{sortLabel(value.sortBy)}</span>
          <Icon name="chevron-down" size={12} strokeWidth={2.2}
            className={`ml-auto shrink-0 text-faint transition-transform duration-150 ${open === 'sort' ? 'rotate-180' : ''}`} />
        </button>
        {open === 'sort' && (
          <div role="menu" aria-label="Sort games" className={`${MENU} w-[232px]`}>
            {SORT_OPTIONS.map(o => (
              <button key={o.value} type="button" role="menuitemradio" aria-checked={o.value === value.sortBy}
                className={ITEM} onClick={() => pickSort(o.value)}>
                <span className="w-3.5 shrink-0 text-accent">
                  {o.value === value.sortBy && <Icon name="check" size={13} strokeWidth={2.6} />}
                </span>
                {o.label}
              </button>
            ))}
            <div className="h-px bg-line mx-1 my-1.5" />
            <button type="button" role="menuitem"
              className={ITEM}
              onClick={() => onChange({ ...value, dir: value.dir === 1 ? -1 : 1 })}>
              <span className="w-3.5 shrink-0 text-faint"><Icon name="arrow-up-down" size={13} strokeWidth={2.2} /></span>
              {dirLabel(value.sortBy, value.dir)}
            </button>
          </div>
        )}
      </span>
    </div>
  );
}
