import type { ReactNode } from 'react';

interface Props {
  children: ReactNode;
  className?: string;
}

/** plan.md Phase 9.2: the canvas every console view renders into — 22/24 px padding, 16 px between
 *  sections, and the `rise` entrance staggered over the first six (`.page-canvas` in index.css). It owns
 *  its own scrollbar, so the top bar and a sidebar beside it stay put. */
export function Page({ children, className = '' }: Props) {
  return (
    <div className="flex-1 min-w-0 min-h-0 overflow-y-auto">
      <div className={`page-canvas flex flex-col gap-4 px-6 pt-[22px] pb-7 min-w-0 ${className}`}>{children}</div>
    </div>
  );
}
