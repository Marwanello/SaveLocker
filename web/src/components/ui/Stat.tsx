import type { ReactNode } from 'react';

interface Props {
  label: string;
  value: ReactNode;
  context?: ReactNode;
  className?: string;
}

/** plan.md "Components": eyebrow label, large tabular figure, one line of context underneath. */
export function Stat({ label, value, context, className = '' }: Props) {
  return (
    <dl className={`bg-panel border border-line rounded-[14px] px-4 py-[15px] ${className}`}>
      <dt className="text-[10px] tracking-[0.12em] uppercase text-faint">{label}</dt>
      <dd className="mt-2 text-[25px] font-bold tracking-[-0.03em] leading-none">{value}</dd>
      {context && <dd className="mt-1.5 text-[11.5px] text-dim">{context}</dd>}
    </dl>
  );
}
