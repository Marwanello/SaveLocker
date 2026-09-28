export interface ToastItem { id: number; text: string; ms: number }

let items: ToastItem[] = [];
let nextId = 1;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach(l => l());

/**
 * Say something in the past tense, briefly (plan.md "Voice"; Motion: 2.6 s dwell). Callable from
 * anywhere — an action handler deep in a card has no business owning a toast's position or lifetime.
 * `components/ui/Toaster.tsx`, mounted once in App, draws them. At most three stack; the oldest goes.
 */
export function toast(text: string, ms = 2600) {
  items = [...items.slice(-2), { id: nextId++, text, ms }];
  emit();
}

/** Failures stay long enough to read the server's reason. */
export const toastError = (text: string) => toast(text, 8000);

export function dismissToast(id: number) {
  items = items.filter(i => i.id !== id);
  emit();
}

export function subscribeToasts(l: () => void) { listeners.add(l); return () => { listeners.delete(l); }; }
export const toastsSnapshot = () => items;
