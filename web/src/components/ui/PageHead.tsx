import type { ReactNode } from 'react';

interface Props {
  title: ReactNode;
  /** One line under the title: counts, ids, sizes. */
  sub?: ReactNode;
  actions?: ReactNode;
}

/** plan.md Type: "Page title — Archivo 700, 27px, -0.035em". The sub-line is the page's facts in small
 *  tabular type (mono is not the data face — plan.md Decisions), the actions sit right and wrap under. */
export function PageHead({ title, sub, actions }: Props) {
  return (
    <header className="flex items-start justify-between gap-[18px] flex-wrap">
      <div className="min-w-0">
        <h2 className="text-[27px] font-bold tracking-[-0.035em] leading-[1.05] text-fg break-words">{title}</h2>
        {sub && <div className="mt-1.5 text-[11.5px] text-dim">{sub}</div>}
      </div>
      {actions && <div className="flex gap-2 flex-wrap items-center">{actions}</div>}
    </header>
  );
}
