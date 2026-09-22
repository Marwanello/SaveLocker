import type { ReactNode } from 'react';

interface Props {
  cover: ReactNode;
  /** A string title also becomes the row's tooltip: the column is narrow and the text truncates. */
  title: ReactNode;
  subtext: ReactNode;
  end?: ReactNode;
  onClick?: () => void;
  className?: string;
}

/** plan.md "Layout rules": two-line rows — name on line one, everything else on line two, status
 *  pinned right and centred across both. A three-column CSS grid with two rows, not a flex row, so
 *  `end` never has to guess its own vertical alignment. */
export function Row({ cover, title, subtext, end, onClick, className = '' }: Props) {
  const Tag = onClick ? 'button' : 'div';
  return (
    <Tag
      onClick={onClick}
      className={`grid grid-cols-[auto_minmax(0,1fr)_auto] grid-rows-2 gap-x-3 gap-y-[3px]
        px-3 py-[10px] border border-line rounded-xl bg-panel items-center w-full text-left
        transition-transform duration-150 ease-[var(--ease)]
        focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
        ${onClick ? 'hover:translate-x-0.5 cursor-pointer' : ''} ${className}`}
    >
      <span className="row-span-2 w-[38px] h-[38px] rounded-[9px] flex items-center justify-center flex-shrink-0 overflow-hidden">
        {cover}
      </span>
      <span className="col-start-2 text-[13px] font-semibold self-end truncate" title={typeof title === 'string' ? title : undefined}>{title}</span>
      <span className="col-start-2 text-[10.5px] text-dim self-start truncate">{subtext}</span>
      {end && <span className="col-start-3 row-span-2 flex items-center">{end}</span>}
    </Tag>
  );
}
