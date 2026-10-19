import { useCallback, useState } from 'react';

function readOpen(key: string): boolean {
  try { return localStorage.getItem(key) !== 'closed'; }
  catch { return true; }
}

/** Open or closed, remembered per viewer under `key` — the same for every game, like the library's layout. */
export function usePersistedOpen(key: string): [boolean, (open: boolean) => void] {
  const [open, setOpenState] = useState(() => readOpen(key));
  const setOpen = useCallback((v: boolean) => {
    setOpenState(v);
    try { localStorage.setItem(key, v ? 'open' : 'closed'); } catch { /* private browsing, etc — not fatal */ }
  }, [key]);
  return [open, setOpen];
}
