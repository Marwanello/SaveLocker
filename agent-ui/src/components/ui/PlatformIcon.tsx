import { PLATFORM_ICONS } from '../../platformIcons.generated'

// The same component in web/src/components/ui and agent-ui/src/components/ui: both draw the one generated sprite.

const SPRITE_ID = 'sl-platform-icons'

/** Put every icon in the page once, as <symbol>s an icon <use>s: one copy of the paths however many rows show them. */
function ensureSprite() {
  if (typeof document === 'undefined' || document.getElementById(SPRITE_ID)) return
  const symbols = Object.entries(PLATFORM_ICONS)
    .map(([id, icon]) => `<symbol id="pi-${id}" viewBox="${icon.viewBox}">${icon.body}</symbol>`)
    .join('')
  const holder = document.createElement('div')
  holder.innerHTML = `<svg id="${SPRITE_ID}" xmlns="http://www.w3.org/2000/svg" aria-hidden="true" style="position:absolute;width:0;height:0;overflow:hidden">${symbols}</svg>`
  document.body.prepend(holder.firstChild!)
}

interface Props {
  /** An icon id (`emulatorIcon`, `systemIcon`); nothing is drawn for null. */
  id: string | null
  /** In ems; a wordmark (MAME) is drawn wider. */
  size?: number
  /** In ems: the widest it may draw. A wide mark (PSP) shrinks to fit, so a square tile never crops it. */
  maxWidth?: number
  className?: string
}

/** How much wider than tall a glyph draws: 1 for the square ones, more for a PlayStation mark with its number. */
function aspect(viewBox: string): number {
  const [, , w, h] = viewBox.split(' ').map(Number)
  return Math.max(1, w / h)
}

/** An emulator's or a console's flat icon (tasks/emulator-saves Phase 21). Decoration: the text beside it names the same
 *  thing, so it is hidden from assistive technology. Line icons take the stroke width set here, which reads at 15–20 px. */
export function PlatformIcon({ id, size = 1, maxWidth, className }: Props) {
  const icon = id ? PLATFORM_ICONS[id] : undefined
  if (!id || !icon) return null
  ensureSprite()
  const natural = icon.wide ? size * 2.6 : size * aspect(icon.viewBox)
  const width = maxWidth ? Math.min(natural, maxWidth) : natural
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      className={className}
      width={`${width}em`}
      height={`${(size * width) / natural}em`}
      style={{ flex: 'none', verticalAlign: '-0.15em' }}
      strokeWidth={icon.line ? 2.6 : undefined}
    >
      <use href={`#pi-${id}`} />
    </svg>
  )
}
