import type { ButtonHTMLAttributes } from 'react';

type Variant = 'default' | 'primary' | 'selected' | 'quiet' | 'alert';
type Size = 'default' | 'sm';

interface Props extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
}

const VARIANT: Record<Variant, string> = {
  default: 'bg-raise border-line text-fg hover:bg-hover',
  primary: 'bg-accent border-accent text-on-accent font-semibold hover:brightness-110',
  // The current tab / chosen option: highlighted, but not the accent — that stays the one action.
  selected: 'bg-tile border-line text-fg font-semibold hover:bg-tile',
  quiet: 'bg-transparent border-transparent text-dim hover:bg-hover',
  alert: 'bg-accent-soft border-accent-line text-accent-ink font-semibold hover:bg-accent-soft',
};

const SIZE: Record<Size, string> = {
  default: 'px-[15px] py-2 text-[13px]',
  sm: 'px-[11px] py-[5px] text-xs',
};

/** plan.md "Components": one filled (`primary`) button per view, everything else outline/quiet/text.
 *  `selected` marks the current tab without spending that one accent. Destructive actions use `alert`
 *  and must name their effect in their own label, not here. */
export function Button({ variant = 'default', size = 'default', className = '', ...rest }: Props) {
  return (
    <button
      className={`font-[inherit] font-medium rounded-full border cursor-pointer whitespace-nowrap
        transition-[background-color,transform,box-shadow] duration-150 ease-[var(--ease)]
        hover:opacity-100 hover:-translate-y-px active:scale-[.96]
        disabled:opacity-50 disabled:cursor-default disabled:hover:translate-y-0 disabled:active:scale-100
        focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
        ${VARIANT[variant]} ${SIZE[size]} ${className}`}
      {...rest}
    />
  );
}
