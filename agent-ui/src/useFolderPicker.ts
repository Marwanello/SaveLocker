import { useRef, useState } from 'react'

/**
 * The one folder-picking flow, shared by Add Games and Settings. The two views drifting apart is
 * exactly what left the Add Games "Set save folder…" button dead on Linux: Settings had the
 * native-first-then-browser fallback and Add Games did not. One implementation removes the
 * possibility of it happening again.
 *
 * The flow is: try the host's native dialog first (the Windows tray returns an Explorer path), and
 * only when that returns null — a headless Deck has no dialog — open the in-app path browser.
 */
export interface PickRequest {
  /** Game/candidate name, for the browser modal title and status line. */
  name: string
  /** Where the browser opens when it has to fall through. Resolved lazily, so a per-target
   *  suggested-path lookup only runs when the native dialog actually declined. */
  start: () => string | null | Promise<string | null>
  /** The host's native folder dialog. Returns { path: null } on a headless box. */
  nativePick: () => Promise<{ path: string | null }>
  /** Persist a chosen path (native or browsed) and refresh local state. */
  apply: (path: string) => Promise<void>
}

export interface FolderPicker {
  /** The active browse session, or null. A view renders <PathBrowserModal> from this. */
  browsing: { name: string; start: string | null } | null
  pick: (req: PickRequest) => Promise<void>
  confirmBrowsed: (path: string) => Promise<void>
  cancel: () => void
}

export function useFolderPicker(): FolderPicker {
  const [browsing, setBrowsing] = useState<{ name: string; start: string | null } | null>(null)
  const applyRef = useRef<((path: string) => Promise<void>) | null>(null)

  const pick = async (req: PickRequest) => {
    const native = await req.nativePick().catch(() => ({ path: null as string | null }))
    if (native.path) {
      await req.apply(native.path)
      return
    }
    const start = await req.start()
    applyRef.current = req.apply
    setBrowsing({ name: req.name, start })
  }

  const confirmBrowsed = async (path: string) => {
    const apply = applyRef.current
    applyRef.current = null
    setBrowsing(null)
    if (apply) await apply(path)
  }

  const cancel = () => {
    applyRef.current = null
    setBrowsing(null)
  }

  return { browsing, pick, confirmBrowsed, cancel }
}
