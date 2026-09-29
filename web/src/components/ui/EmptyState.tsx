import type { ReactNode } from 'react';

interface Props {
  title: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
}

/** The prototype's `.empty`: says what would be here and how it gets here — never just "No data". */
export function EmptyState({ title, children, action }: Props) {
  return (
    <div className="px-5 py-10 text-center text-[13px] text-dim">
      <b className="block text-fg text-[15px] font-semibold mb-1.5">{title}</b>
      {children}
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  );
}
