import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
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
}

/**
 * A listbox in place of a native `<select>`, whose popup the browser draws in the OS's own style with
 * no room for an icon or a second line. The WAI-ARIA select-only combobox pattern, keyboard complete:
 * ↑/↓/Home/End move, Enter or Space picks, Escape closes back onto the trigger, a letter jumps to the
 * next option that starts with it, and Tab or a click elsewhere closes.
 */
export function Select({ value, options, onChange, label, placeholder = 'Choose…', variant = 'pill', align = 'start', disabled }: Props) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const id = useId();
  const chosen = options.find(o => o.value === value) ?? null;

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', onPointer);
    listRef.current?.focus();
    return () => document.removeEventListener('mousedown', onPointer);
  }, [open]);

  useEffect(() => {
    if (open) document.getElementById(`${id}-${active}`)?.scrollIntoView({ block: 'nearest' });
  }, [open, active, id]);

  function show(at?: number) {
    const i = options.findIndex(o => o.value === value);
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
      case 'Home': e.preventDefault(); setActive(0); return;
      case 'End': e.preventDefault(); setActive(last); return;
      case 'Enter': case ' ': e.preventDefault(); pick(active); return;
      case 'Escape': e.preventDefault(); close(true); return;
      case 'Tab': close(false); return;
    }
    if (e.key.length === 1 && /\S/.test(e.key)) {
      const ch = e.key.toLowerCase();
      for (let step = 1; step <= options.length; step++) {
        const i = (active + step) % options.length;
        if (options[i].label.toLowerCase().startsWith(ch)) { setActive(i); return; }
      }
    }
  }

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

      {open && (
        <div
          ref={listRef}
          id={`${id}-list`}
          role="listbox"
          tabIndex={-1}
          aria-label={label}
          aria-activedescendant={`${id}-${active}`}
          onKeyDown={onListKey}
          className={`animate-drop absolute z-30 top-[calc(100%+6px)] ${align === 'end' ? 'right-0' : 'left-0'}
            min-w-full w-max max-w-[min(340px,92vw)] max-h-[320px] overflow-y-auto
            bg-panel border border-line rounded-xl shadow-2xl p-1 outline-none`}
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
      )}
    </div>
  );
}
