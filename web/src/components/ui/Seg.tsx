import { Icon } from './Icon';
import type { IconName } from './Icon';

interface Option<T extends string> {
  value: T;
  label: string;
  icon?: IconName;
}

interface Props<T extends string> {
  value: T;
  options: Option<T>[];
  onChange: (v: T) => void;
  'aria-label': string;
  className?: string;
  /** Ignore clicks — e.g. while a save is in flight, so two rapid choices cannot race each other. */
  disabled?: boolean;
  /** `sm` is the prototype's sidebar/page-head switch (11.5 px); the default is the settings size. */
  size?: 'default' | 'sm';
}

/** plan.md "Games: sidebar list by default, grid wall as an alternative, switch in the sidebar
 *  header." Generic over any two-or-more-option choice, not just list/grid. */
export function Seg<T extends string>({ value, options, onChange, className = '', disabled = false, size = 'default', ...rest }: Props<T>) {
  const sm = size === 'sm';
  return (
    <div
      role="group"
      aria-label={rest['aria-label']}
      className={`inline-flex bg-raise border border-line rounded-full ${sm ? 'p-[2px]' : 'p-[3px]'} gap-0.5 ${className}`}
    >
      {options.map(o => (
        <button
          key={o.value}
          type="button"
          aria-pressed={o.value === value}
          disabled={disabled}
          onClick={() => onChange(o.value)}
          className={`inline-flex items-center gap-1.5 rounded-full border-0 cursor-pointer disabled:cursor-default
            ${sm ? 'text-[11.5px] font-medium px-[11px] py-[5px]' : 'text-[12.5px] font-semibold px-[15px] py-[7px]'}
            transition-colors duration-150 ease-[var(--ease)]
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
            ${o.value === value ? 'bg-panel text-fg shadow-sm' : 'bg-transparent text-dim hover:text-fg hover:opacity-100'}`}
        >
          {o.icon && <Icon name={o.icon} size={12} strokeWidth={2.4} />}
          {o.label}
        </button>
      ))}
    </div>
  );
}
