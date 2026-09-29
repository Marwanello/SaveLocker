interface Option<T extends string> {
  value: T;
  label: string;
  count?: number;
}

interface Props<T extends string> {
  value: T;
  options: Option<T>[];
  onChange: (v: T) => void;
  'aria-label': string;
  className?: string;
}

/** The prototype's `fchip` row: one choice at a time, the current one filled in the text colour (a
 *  filter being on is not a decision waiting, so it is not the accent), each with its count. */
export function FilterChips<T extends string>({ value, options, onChange, className = '', ...rest }: Props<T>) {
  return (
    <div role="group" aria-label={rest['aria-label']} className={`flex gap-[7px] flex-wrap items-center ${className}`}>
      {options.map(o => {
        const on = o.value === value;
        return (
          <button
            key={o.value}
            type="button"
            aria-pressed={on}
            onClick={() => onChange(o.value)}
            className={`text-[12.5px] px-[13px] py-1.5 rounded-full border cursor-pointer
              transition-[background-color,color,transform] duration-150 ease-[var(--ease)] active:scale-[.96]
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
              ${on ? 'bg-fg border-fg text-ink font-semibold' : 'bg-raise border-line text-dim hover:text-fg'}`}
          >
            {o.label}
            {o.count !== undefined && <span className="ml-[5px] text-[10px] tabular-nums opacity-70">{o.count}</span>}
          </button>
        );
      })}
    </div>
  );
}
