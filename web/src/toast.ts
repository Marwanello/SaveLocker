export interface ToastItem { id: number; text: string; ms: number; tone: 'info' | 'error' }

let items: ToastItem[] = [];
let nextId = 1;
// The newest toast of each tone, for the screen-reader announcers in Toaster. Kept apart from `items`
// because it must change only when a toast ARRIVES: derived from the visible list, a dismissal would
// put an older toast back in the announcer and read it out a second time.
let announced: { info: ToastItem | null; error: ToastItem | null } = { info: null, error: null };
const listeners = new Set<() => void>();
const emit = () => listeners.forEach(l => l());

/**
 * Say something in the past tense, briefly (plan.md "Voice"; Motion: 2.6 s dwell). Callable from
 * anywhere — an action handler deep in a card has no business owning a toast's position or lifetime.
 * `components/ui/Toaster.tsx`, mounted once in App, draws them. At most three stack; the oldest goes.
 */
export function toast(text: string, ms = 2600, tone: ToastItem['tone'] = 'info') {
  const item: ToastItem = { id: nextId++, text, ms, tone };
  items = [...items.slice(-2), item];
  announced = tone === 'error' ? { ...announced, error: item } : { ...announced, info: item };
  emit();
}

/** Failures stay long enough to read the server's reason, and are announced assertively. */
export const toastError = (text: string) => toast(text, 8000, 'error');

export function dismissToast(id: number) {
  items = items.filter(i => i.id !== id);
  emit();
}

export function subscribeToasts(l: () => void) { listeners.add(l); return () => { listeners.delete(l); }; }
export const toastsSnapshot = () => items;
export const announcedSnapshot = () => announced;
