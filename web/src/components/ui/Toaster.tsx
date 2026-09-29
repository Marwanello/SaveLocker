import { useSyncExternalStore } from 'react';
import { announcedSnapshot, dismissToast, subscribeToasts, toastsSnapshot } from '../../toast';
import { Toast } from './Toast';

/** Mounted once, in App: bottom-centre, above everything, never over the top bar's controls. Anything
 *  shows a toast through `toast()` in `src/toast.ts`. */
export function Toaster() {
  const list = useSyncExternalStore(subscribeToasts, toastsSnapshot);
  const said = useSyncExternalStore(subscribeToasts, announcedSnapshot);
  return (
    <>
      {/* The announcers are ALWAYS mounted: a live region created together with its text — which is what
          each toast used to be — is often not read at all. Keyed by toast, so the same text twice is
          read twice; failures go to the assertive one. */}
      <div className="sr-only" role="status">{said.info && <span key={said.info.id}>{said.info.text}</span>}</div>
      <div className="sr-only" role="alert">{said.error && <span key={said.error.id}>{said.error.text}</span>}</div>
      {list.length > 0 && (
        <div className="fixed bottom-5 left-1/2 -translate-x-1/2 z-40 flex flex-col items-center gap-2 w-max max-w-[min(92vw,560px)]">
          {list.map(t => (
            <Toast key={t.id} dwellMs={t.ms} onDismiss={() => dismissToast(t.id)}>{t.text}</Toast>
          ))}
        </div>
      )}
    </>
  );
}
