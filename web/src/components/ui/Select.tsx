import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import type { CSSProperties, KeyboardEvent, ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Icon } from './Icon';

export interface SelectOption {
  value: string;
  label: string;
  /** A second, dim line under the label: an OS, a state. */
  sub?: string;
  /** Beside the label in the list, and in the trigger when this option is chosen. */
  icon?: ReactNode;
}

interface Props {
  value: string | null;
  options: SelectOption[];
  onChange: (value: string) => void;
  /** Names the control; the trigger reads "<label>: <chosen option>". */
  label: string;
  /** Trigger text when nothing is chosen yet. */
  placeholder?: string;
  /** `pill` sits among buttons; `field` sits among form fields. */
  variant?: 'pill' | 'field';
  /** Which trigger edge the list lines up with — `end` near the right of the page, so it opens inward. */
  align?: 'start' | 'end';
  disabled?: boolean;
  /** A filter box at the top of the list, for a list too long to scroll through: typing narrows it by label. */
  searchable?: boolean;
}

/**
 * A listbox in place of a native `<select>`, whose popup the browser draws in the OS's own style with
 * no room for an icon or a second line. The WAI-ARIA select-only combobox pattern, keyboard complete:
 * ↑/↓/Home/End move, Enter or Space picks, Escape closes back onto the trigger, a letter jumps to the
 * next option that starts with it, and Tab or a click elsewhere closes.
 */
export function Select({ value, options: all, onChange, label, placeholder = 'Choose…', variant = 'pill', align = 'start', disabled, searchable }: Props) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const [query, setQuery] = useState('');
  const q = query.trim().toLowerCase();
  const options = q ? all.filter(o => o.label.toLowerCase().includes(q)) : all;
  const searchRef = useRef<HTMLInputElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const popRef = useRef<HTMLDivElement>(null);
  const [place, setPlace] = useState<CSSProperties>({});
  const id = useId();
  const chosen = all.find(o => o.value === value) ?? null;

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: MouseEvent) => {
      const t = e.target as Node;
      if (![rootRef, popRef, listRef].some(r => r.current?.contains(t))) setOpen(false);
    };
    document.addEventListener('mousedown', onPointer);
    (searchRef.current ?? listRef.current)?.focus();
    return () => document.removeEventListener('mousedown', onPointer);
  }, [open]);

  // The list is drawn on <body>, fixed beside the trigger: inside a Card (overflow hidden) it was clipped to the
  // card's own height — a long list in the Merge card showed a row or two. Below the trigger when the whole list
  // fits there, else on whichever side has more room, as tall as that side allows up to the cap.
  useLayoutEffect(() => {
    if (!open) return;
    function update(e?: Event) {
      // Scrolling the list itself moves nothing.
      if (e?.target instanceof Node && (popRef.current ?? listRef.current)?.contains(e.target)) return;
      const r = triggerRef.current?.getBoundingClientRect();
      const pop = popRef.current ?? listRef.current;
      const list = listRef.current;
      if (!r || !pop || !list) return;
      const below = window.innerHeight - r.bottom - 12;
      const above = r.top - 12;
      const cap = searchable ? 420 : 320;
      // The whole popup's height, list unscrolled: a short list fits below even where the cap would not.
      const need = Math.min(cap, pop.offsetHeight - list.clientHeight + list.scrollHeight);
      const up = below < need && above > below;
      setPlace({
        ...(up ? { bottom: window.innerHeight - r.top + 6 } : { top: r.bottom + 6 }),
        ...(align === 'end' ? { right: document.documentElement.clientWidth - r.right } : { left: r.left }),
        minWidth: r.width,
        maxHeight: Math.max(120, Math.min(cap, (up ? above : below) - 6)),
      });
    }
    update();
    window.addEventListener('resize', update);
    window.addEventListener('scroll', update, true);
    return () => {
      window.removeEventListener('resize', update);
      window.removeEventListener('scroll', update, true);
    };
  }, [open, align, searchable]);

  useEffect(() => {
    if (open) document.getElementById(`${id}-${active}`)?.scrollIntoView({ block: 'nearest' });
  }, [open, active, id]);

  function show(at?: number) {
    setQuery('');
    const i = all.findIndex(o => o.value === value);
    setActive(at ?? Math.max(0, i));
    setOpen(true);
  }

  function close(refocus: boolean) {
    setOpen(false);
    if (refocus) triggerRef.current?.focus();
  }

  function pick(i: number) {
    const o = options[i];
    if (o && o.value !== value) onChange(o.value);
    close(true);
  }

  function onTriggerKey(e: KeyboardEvent) {
    if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(e.key)) {
      e.preventDefault();
      show();
    }
  }

  function onListKey(e: KeyboardEvent) {
    const last = options.length - 1;
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); setActive(a => Math.min(last, a + 1)); return;
      case 'ArrowUp': e.preventDefault(); setActive(a => Math.max(0, a - 1)); return;
      // In the filter box Home/End move its caret, as in any text field.
      case 'Home': if (searchable) return; e.preventDefault(); setActive(0); return;
      case 'End': if (searchable) return; e.preventDefault(); setActive(last); return;
      case 'Enter': e.preventDefault(); if (options.length > 0) pick(active); return;
      case ' ': if (searchable) return; e.preventDefault(); pick(active); return;
      case 'Escape': e.preventDefault(); close(true); return;
      // Back on the trigger before the browser moves focus: the list sits at the end of <body>, so Tab from it
      // would leave the page instead of reaching the control after this one.
      case 'Tab': close(true); return;
    }
    if (!searchable && e.key.length === 1 && /\S/.test(e.key)) {
      const ch = e.key.toLowerCase();
      for (let step = 1; step <= options.length; step++) {
        const i = (active + step) % options.length;
        if (options[i].label.toLowerCase().startsWith(ch)) { setActive(i); return; }
      }
    }
  }

  const popup = 'animate-drop fixed z-50 w-max max-w-[min(340px,92vw)] bg-panel border border-line rounded-xl shadow-2xl';

  const trigger = variant === 'pill'
    ? 'rounded-full bg-raise pl-2 pr-2.5 py-[5px] text-[12.5px]'
    : 'rounded-lg bg-tile pl-2 pr-2 py-[4px] text-[12.5px]';

  return (
    <div ref={rootRef} className="relative inline-flex">
      <button
        ref={triggerRef}
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? `${id}-list` : undefined}
        aria-label={`${label}: ${chosen?.label ?? placeholder}`}
        onClick={() => (open ? close(false) : show())}
        onKeyDown={onTriggerKey}
        className={`${trigger} inline-flex items-center gap-2 max-w-[260px] border text-fg cursor-pointer
          transition-[border-color,background-color] duration-150 ease-[var(--ease)]
          ${open ? 'border-dim' : 'border-line hover:border-dim'}
          disabled:opacity-50 disabled:cursor-default
          focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2`}
      >
        {chosen?.icon}
        <span className={`truncate font-semibold ${chosen ? '' : 'text-dim font-normal'}`}>{chosen?.label ?? placeholder}</span>
        <Icon name="chevron-down" size={13}
          className={`text-dim transition-transform duration-150 ease-[var(--ease)] ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && !searchable && createPortal(
        <div
          ref={listRef}
          id={`${id}-list`}
          role="listbox"
          tabIndex={-1}
          aria-label={label}
          aria-activedescendant={`${id}-${active}`}
          onKeyDown={onListKey}
          style={place}
          className={`${popup} overflow-y-auto p-1 outline-none`}
        >
          {options.map((o, i) => {
            const selected = o.value === value;
            return (
              <div
                key={o.value}
                id={`${id}-${i}`}
                role="option"
                aria-selected={selected}
                onMouseMove={() => { if (active !== i) setActive(i); }}
                onClick={() => pick(i)}
                className={`flex items-center gap-2.5 pl-2 pr-2.5 py-[7px] rounded-lg cursor-pointer select-none
                  ${i === active ? 'bg-row shadow-[inset_0_0_0_1px_var(--color-line)]' : ''}`}
              >
                {o.icon}
                <span className="flex flex-col min-w-0 flex-1">
                  <span className={`text-[13px] truncate ${selected ? 'font-semibold text-fg' : 'text-fg'}`}>{o.label}</span>
                  {o.sub && <span className="text-[11.5px] text-dim truncate">{o.sub}</span>}
                </span>
                <Icon name="check" size={14} className={`ml-2 ${selected ? 'text-fg' : 'invisible'}`} />
              </div>
            );
          })}
        </div>
      , document.body)}

      {/* The filter box keeps focus while the arrows move through the list: the combobox-with-listbox pattern. */}
      {open && searchable && createPortal(
        <div ref={popRef} style={place} className={`${popup} min-w-[260px] flex flex-col overflow-hidden`} onKeyDown={onListKey}>
          <div className="p-1.5 border-b border-line">
            <input
              ref={searchRef}
              type="text"
              role="combobox"
              aria-expanded
              aria-controls={`${id}-list`}
              aria-autocomplete="list"
              aria-activedescendant={options.length > 0 ? `${id}-${active}` : undefined}
              aria-label={`Search: ${label}`}
              placeholder="Type to search"
              autoComplete="off"
              value={query}
              onChange={e => { setQuery(e.target.value); setActive(0); }}
              className="w-full bg-tile text-fg border border-line rounded-lg px-2.5 py-[5px] text-[12.5px] outline-none focus:border-accent-line"
            />
          </div>
          <div ref={listRef} id={`${id}-list`} role="listbox" aria-label={label} className="flex-1 min-h-0 overflow-y-auto p-1">
            {options.map((o, i) => {
              const selected = o.value === value;
              return (
                <div
                  key={o.value}
                  id={`${id}-${i}`}
                  role="option"
                  aria-selected={selected}
                  onMouseMove={() => { if (active !== i) setActive(i); }}
                  onClick={() => pick(i)}
                  className={`flex items-center gap-2.5 pl-2 pr-2.5 py-[7px] rounded-lg cursor-pointer select-none
                    ${i === active ? 'bg-row shadow-[inset_0_0_0_1px_var(--color-line)]' : ''}`}
                >
                  {o.icon}
                  <span className="flex flex-col min-w-0 flex-1">
                    <span className={`text-[13px] truncate ${selected ? 'font-semibold text-fg' : 'text-fg'}`}>{o.label}</span>
                    {o.sub && <span className="text-[11.5px] text-dim truncate">{o.sub}</span>}
                  </span>
                  <Icon name="check" size={14} className={`ml-2 ${selected ? 'text-fg' : 'invisible'}`} />
                </div>
              );
            })}
            {options.length === 0 && <p className="px-2 py-2 text-[12.5px] text-dim">Nothing matches “{query.trim()}”.</p>}
          </div>
        </div>
      , document.body)}
    </div>
  );
}
