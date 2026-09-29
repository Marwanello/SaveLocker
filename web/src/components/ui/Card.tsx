import type { ReactNode } from 'react';

interface Props {
  title?: ReactNode;
  headerRight?: ReactNode;
  children: ReactNode;
  /** No body padding — for a card whose body is a table or a list that draws its own rules. */
  flush?: boolean;
  className?: string;
  id?: string;
}

/** plan.md "Components": 14px radius, a header row when `title` is given — the prototype's `.card`
 *  (12/16 px header, 15/16 px body). */
export function Card({ title, headerRight, children, flush = false, className = '', id }: Props) {
  return (
    <section id={id} className={`bg-panel border border-line rounded-[14px] overflow-hidden min-w-0 ${className}`}>
      {title && (
        <header className="px-4 py-3 border-b border-line flex items-center justify-between gap-3 flex-wrap">
          <h3 className="text-sm font-semibold tracking-[-0.02em] text-fg">{title}</h3>
          {headerRight && <div className="flex items-center gap-2 flex-wrap">{headerRight}</div>}
        </header>
      )}
      <div className={flush ? '' : 'px-4 py-[15px]'}>{children}</div>
    </section>
  );
}
