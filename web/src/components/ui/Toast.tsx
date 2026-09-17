import { useEffect } from 'react';
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
  useEffect(() => {
    const id = setTimeout(onDismiss, dwellMs);
    return () => clearTimeout(id);
  }, [onDismiss, dwellMs]);

  return (
    <div
      role="status"
      className="animate-toast-in bg-panel border border-line rounded-xl px-4 py-3 text-[13px] text-fg shadow-lg"
    >
      {children}
    </div>
  );
}
