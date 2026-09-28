import type { ReactNode } from 'react';

interface Props {
  items: { label: string; value: ReactNode }[];
  className?: string;
}

/** The prototype's `dl.kv`: an eyebrow label beside its value, two columns, the value free to wrap. */
export function KV({ items, className = '' }: Props) {
  return (
    <dl className={`grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-[9px] text-[12.5px] ${className}`}>
      {items.map(it => (
        <div key={it.label} className="contents">
          <dt className="text-[10px] tracking-[0.1em] uppercase text-faint pt-[3px]">{it.label}</dt>
          <dd className="text-fg min-w-0">{it.value}</dd>
        </div>
      ))}
    </dl>
  );
}
