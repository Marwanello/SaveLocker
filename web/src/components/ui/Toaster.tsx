import { useSyncExternalStore } from 'react';
import { dismissToast, subscribeToasts, toastsSnapshot } from '../../toast';
import { Toast } from './Toast';

/** Mounted once, in App: bottom-centre, above everything, never over the top bar's controls. Anything
 *  shows a toast through `toast()` in `src/toast.ts`. */
export function Toaster() {
  const list = useSyncExternalStore(subscribeToasts, toastsSnapshot);
  if (list.length === 0) return null;
  return (
    <div className="fixed bottom-5 left-1/2 -translate-x-1/2 z-40 flex flex-col items-center gap-2 w-max max-w-[min(92vw,560px)]">
      {list.map(t => (
        <Toast key={t.id} dwellMs={t.ms} onDismiss={() => dismissToast(t.id)}>{t.text}</Toast>
      ))}
    </div>
  );
}
