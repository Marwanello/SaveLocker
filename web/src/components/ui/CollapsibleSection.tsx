import { useId } from 'react';
import type { ReactNode } from 'react';
import { Icon } from './Icon';

interface Props {
  title: string;
  /** Beside the title, so a closed section still says how much it holds. */
  count?: number;
  open: boolean;
  onToggle: (open: boolean) => void;
  children: ReactNode;
}

/** A section inside a flush Card whose heading folds it away. A real button, so Enter/Space and Tab work. */
export function CollapsibleSection({ title, count, open, onToggle, children }: Props) {
  const id = useId();
  return (
    <div className="border-b border-row last:border-b-0">
      <h4>
        <button
          type="button"
          onClick={() => onToggle(!open)}
          aria-expanded={open}
          aria-controls={open ? id : undefined}
          className="flex items-center gap-2 w-full text-left px-4 py-2.5 cursor-pointer hover:bg-raise
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-[-2px]"
        >
          <Icon name="chevron-down" size={12} strokeWidth={2.4}
            className={`shrink-0 text-faint transition-transform duration-150 ${open ? '' : '-rotate-90'}`} />
          <span className="text-[10px] tracking-[0.1em] uppercase text-faint font-normal">{title}</span>
          {count !== undefined && (
            <span className="ml-auto shrink-0 text-[9.5px] tabular-nums text-faint bg-tile border border-line rounded-full px-1.5 py-px">{count}</span>
          )}
        </button>
      </h4>
      {open && <div id={id} className="border-t border-row">{children}</div>}
    </div>
  );
}
