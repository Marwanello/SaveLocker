import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { Button } from './Button';

interface Props {
  /** The control at rest — what the action is ("Delete", "Prune 5 versions"). */
  label: ReactNode;
  /** What pressing the confirm button will do, in one or two sentences. Shown only once expanded. */
  consequence: ReactNode;
  /** The one button that commits. Names the effect: "Delete version 3f9a1c2e", "Keep the Deck save". */
  confirmLabel: ReactNode;
  /** Runs the action. A throw is the caller's to report (a toast) — this only collapses afterwards. */
  onConfirm: () => Promise<unknown> | void;
  /** `alert` for anything that deletes or overwrites; `primary` for the page's one chosen action. */
  tone?: 'alert' | 'primary' | 'default';
  triggerVariant?: 'default' | 'quiet' | 'alert' | 'primary';
  size?: 'default' | 'sm';
  disabled?: boolean;
  title?: string;
  /** The trigger's accessible name, for an icon-only `label`. */
  ariaLabel?: string;
  className?: string;
}

/**
 * plan.md "No modals" and "Destructive controls name their effect": the control expands in place into
 * its consequence and a single button naming what it does, instead of a browser `confirm()` that says
 * "Are you sure?" over a page it has frozen. Escape or Cancel folds it back; focus goes to the confirm
 * button on opening and back to the control on closing, so a keyboard user never loses their place.
 */
export function InlineConfirm({
  label, consequence, confirmLabel, onConfirm, tone = 'alert', triggerVariant = 'default',
  size = 'sm', disabled = false, title, ariaLabel, className = '',
}: Props) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);

  useEffect(() => {
    if (open) confirmRef.current?.focus();
    else if (wasOpen.current) triggerRef.current?.focus();
    wasOpen.current = open;
  }, [open]);

  async function commit() {
    setBusy(true);
    try { await onConfirm(); }
    catch { /* the caller reports it; a thrown action must not leave this stuck "working" */ }
    finally { setBusy(false); setOpen(false); }
  }

  if (!open) {
    return (
      <Button
        ref={triggerRef} variant={triggerVariant} size={size} disabled={disabled} title={title}
        aria-label={ariaLabel} aria-expanded={false} onClick={() => setOpen(true)} className={className}
      >
        {label}
      </Button>
    );
  }

  return (
    <div
      role="group"
      aria-label="Confirm"
      onKeyDown={e => { if (e.key === 'Escape' && !busy) { e.stopPropagation(); setOpen(false); } }}
      className={`animate-drop inline-flex flex-wrap items-center gap-2 whitespace-normal text-left
        max-w-[520px] rounded-xl border border-line bg-raise px-3 py-2 ${className}`}
    >
      <span className="text-xs text-dim leading-[1.45] min-w-[160px] flex-1">{consequence}</span>
      <span className="flex gap-1.5 items-center">
        {/* Focus lands here, and a button clicks on Enter's keydown — which auto-repeats. Without the
            guard, holding Enter on "Delete game" a moment too long opened AND confirmed it. */}
        <Button ref={confirmRef} variant={tone} size="sm" disabled={busy}
          onKeyDown={e => { if (e.repeat) e.preventDefault(); }}
          onClick={() => void commit()}>
          {busy ? 'Working…' : confirmLabel}
        </Button>
        <Button variant="quiet" size="sm" disabled={busy} onClick={() => setOpen(false)}>Cancel</Button>
      </span>
    </div>
  );
}
