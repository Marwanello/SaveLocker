interface Option<T extends string> {
  value: T;
  label: string;
}

interface Props<T extends string> {
  value: T;
  options: Option<T>[];
  onChange: (v: T) => void;
  'aria-label': string;
  className?: string;
}

/** plan.md "Games: sidebar list by default, grid wall as an alternative, switch in the sidebar
 *  header." Generic over any two-or-more-option choice, not just list/grid. */
export function Seg<T extends string>({ value, options, onChange, className = '', ...rest }: Props<T>) {
  return (
    <div
      role="group"
      aria-label={rest['aria-label']}
      className={`inline-flex bg-raise border border-line rounded-full p-[3px] gap-0.5 ${className}`}
    >
      {options.map(o => (
        <button
          key={o.value}
          type="button"
          aria-pressed={o.value === value}
          onClick={() => onChange(o.value)}
          className={`text-[12.5px] font-semibold px-[15px] py-[7px] rounded-full border-0 cursor-pointer
            transition-colors duration-150 ease-[var(--ease)]
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
            ${o.value === value ? 'bg-panel text-fg shadow-sm' : 'bg-transparent text-faint hover:opacity-100'}`}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}
