import type { ReactNode } from 'react';

interface Props {
  title?: ReactNode;
  headerRight?: ReactNode;
  children: ReactNode;
  className?: string;
}

/** plan.md "Components": 14px radius, a header row when `title` is given, 18px body padding. */
export function Card({ title, headerRight, children, className = '' }: Props) {
  return (
    <div className={`bg-panel border border-line rounded-[14px] overflow-hidden ${className}`}>
      {title && (
        <header className="px-[17px] py-[13px] border-b border-line flex items-center justify-between gap-3">
          <h3 className="text-sm font-semibold text-fg">{title}</h3>
          {headerRight}
        </header>
      )}
      <div className="p-[18px]">{children}</div>
    </div>
  );
}
