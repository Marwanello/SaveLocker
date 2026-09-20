import { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';

interface Props {
  children: ReactNode;
  onDismiss: () => void;
  /** plan.md motion table: "2.6s dwell". */
  dwellMs?: number;
}

/** A single toast, positioned by its caller (there is no global toast host yet — plan.md's "No
 *  modals" rule is about dialogs, not this). Confirms in the past tense, per plan.md "Voice":
 *  "Synced 6 games — 19.3 MB sent" rather than "Sync completed successfully!". */
export function Toast({ children, onDismiss, dwellMs = 2600 }: Props) {
  // The callback is read through a ref, not listed as a dependency: a caller that passes an inline
  // arrow (or a parent that re-renders every poll) would otherwise restart the dwell timer each
  // time, and the toast would outlive its 2.6 s for as long as anything above it kept re-rendering.
  const dismissRef = useRef(onDismiss);
  useEffect(() => { dismissRef.current = onDismiss; });
  useEffect(() => {
    const id = setTimeout(() => dismissRef.current(), dwellMs);
    return () => clearTimeout(id);
  }, [dwellMs]);

  return (
    <div
      role="status"
      className="animate-toast-in bg-panel border border-line rounded-xl px-4 py-3 text-[13px] text-fg shadow-lg"
    >
      {children}
    </div>
  );
}
