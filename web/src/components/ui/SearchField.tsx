import { Icon } from './Icon';

interface Props {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  'aria-label': string;
  className?: string;
  /**
   * Fill the label's width instead of the default 210px input. Needed when the caller stretches the
   * label (`flex-1`): otherwise the field — and the browser's own clear "✕", which sits at the
   * input's end — floats in the middle of the pill instead of at its right edge.
   */
  fill?: boolean;
}

/** The prototype's `.search` pill. Escape clears it — the quickest way back to the whole list. */
export function SearchField({ value, onChange, placeholder = 'Search', className = '', fill = false, ...rest }: Props) {
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
        className={`bg-transparent border-0 text-fg text-[12.5px] py-2 min-w-0 outline-none focus:!border-0 ${fill ? 'flex-1' : 'w-[210px]'}`}
      />
    </label>
  );
}
