import { useState } from 'react';
import { Icon } from './Icon';

interface Props {
  patterns: string[];
  onChange: (next: string[]) => void;
  /** Screen-reader name of the add field: which list it adds to. */
  addLabel: string;
}

/**
 * The exclude-pattern chip editor (plan.md 10.5): each pattern a removable chip, then a dashed field
 * that adds one on Enter. Used by a game's Exclude patterns card and by Configuration's server defaults.
 */
export function GlobChips({ patterns, onChange, addLabel }: Props) {
  const [adding, setAdding] = useState('');

  function add() {
    const p = adding.trim();
    if (p && !patterns.includes(p)) onChange([...patterns, p]);
    setAdding('');
  }

  return (
    <div className="flex flex-wrap gap-[7px]">
      {patterns.map(p => (
        <span key={p} className="inline-flex items-center gap-2 font-mono text-[11.5px] bg-tile border border-line rounded-lg pl-[11px] pr-2 py-1.5 text-fg">
          {p}
          <button type="button" onClick={() => onChange(patterns.filter(x => x !== p))}
            title={`Remove ${p}`} aria-label={`Remove ${p}`}
            className="w-[18px] h-[18px] grid place-items-center rounded-[5px] border-0 bg-transparent text-dim cursor-pointer
              hover:bg-panel hover:text-accent-ink hover:opacity-100
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent">
            <Icon name="x" size={12} />
          </button>
        </span>
      ))}
      {/* The field itself is borderless inside the dashed box, so the box carries the focus ring. */}
      <span className="inline-flex items-center gap-2 bg-tile border border-dashed border-line rounded-lg px-2.5
        focus-within:outline focus-within:outline-2 focus-within:outline-accent focus-within:outline-offset-1">
        <input
          value={adding}
          onChange={e => setAdding(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add(); } }}
          placeholder="add a pattern…"
          aria-label={addLabel}
          className="bg-transparent border-0 outline-none text-fg font-mono text-[11.5px] py-[7px] w-[150px] focus:!border-0"
        />
        <span aria-hidden className="font-mono text-[10px] text-faint">↵</span>
      </span>
    </div>
  );
}
