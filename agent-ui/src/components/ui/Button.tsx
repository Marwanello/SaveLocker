import type { ButtonHTMLAttributes } from 'react'

type Variant = 'default' | 'primary' | 'quiet'
type Size = 'default' | 'sm'

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
}

const VARIANT: Record<Variant, string> = {
  default: '',
  primary: 'sl-btn--primary',
  quiet: 'sl-btn--quiet',
}

/** Counterpart of web/src/components/ui/Button.tsx (plan.md "Components"): one filled (`primary`)
 *  button per view, everything else outline or quiet. The states live in ui.css because agent-ui
 *  has no Tailwind. `selected` and `alert` are not ported until a surface needs them. */
export function Button({ variant = 'default', size = 'default', className = '', type = 'button', ...rest }: Props) {
  const cls = ['sl-btn', VARIANT[variant], size === 'sm' ? 'sl-btn--sm' : '', className].filter(Boolean).join(' ')
  return <button type={type} className={cls} {...rest} />
}
