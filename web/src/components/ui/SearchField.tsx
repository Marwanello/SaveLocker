import { Icon } from './Icon';

interface Props {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  'aria-label': string;
  className?: string;
}

/** The prototype's `.search` pill. Escape clears it — the quickest way back to the whole list. */
export function SearchField({ value, onChange, placeholder = 'Search', className = '', ...rest }: Props) {
  return (
    <label className={`flex items-center gap-2 bg-raise border border-line rounded-full px-3.5
      transition-colors duration-150 ease-[var(--ease)] focus-within:border-accent-line ${className}`}>
      <Icon name="search" size={13} strokeWidth={2.2} className="text-faint" />
      <input
        type="search"
        value={value}
        onChange={e => onChange(e.target.value)}
        onKeyDown={e => { if (e.key === 'Escape' && value) { e.preventDefault(); onChange(''); } }}
        placeholder={placeholder}
        aria-label={rest['aria-label']}
        className="bg-transparent border-0 text-fg text-[12.5px] py-2 w-[210px] min-w-0 outline-none focus:!border-0"
      />
    </label>
  );
}
