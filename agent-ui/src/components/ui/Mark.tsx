import { MARKS, useLook, type MarkId } from '../../appearance'

interface Props {
  /** Defaults to the app mark the user chose (Appearance). Pass one to draw a specific mark — the picker. */
  id?: MarkId
  size?: number
  /** One colour, no punch — `currentColor` on the panel colour — for where the accent would fight the host. */
  mono?: boolean
  title?: string
}

/** The SaveLocker mark, in the live accent. The geometry is `MARKS` in appearance.ts (the same shapes the
 *  favicon and the Windows tray icon draw); the two colours are the theme's own `--color-accent` and
 *  `--color-on-accent`, so an accent or theme change repaints it with no re-render. */
export function Mark({ id, size = 28, mono = false, title = 'SaveLocker' }: Props) {
  const chosen = useLook().mark
  const mark = MARKS.find(m => m.id === (id ?? chosen)) ?? MARKS[0]
  const body = mono ? 'currentColor' : 'var(--color-accent)'
  const punch = mono ? 'var(--color-panel)' : 'var(--color-on-accent)'
  return (
    <svg
      width={size} height={size} viewBox="0 0 32 32" fill="none" role="img" aria-label={title}
      style={{ flexShrink: 0, display: 'block' }}
      // Constants and two colour tokens only — no user input reaches this string.
      dangerouslySetInnerHTML={{ __html: mark.draw(body, punch) }}
    />
  )
}
