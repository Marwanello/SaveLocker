interface Props {
  checked: boolean
  onChange: (next: boolean) => void
  'aria-label': string
  disabled?: boolean
}

/** Counterpart of web/src/components/ui/Switch.tsx. The knob's spring and the tinted (never filled) ON
 *  track live in ui.css — a switch being on is not a decision waiting. */
export function Switch({ checked, onChange, disabled = false, ...rest }: Props) {
  return (
    <button
      type="button"
      role="switch"
      className="sl-switch"
      aria-checked={checked}
      aria-label={rest['aria-label']}
      disabled={disabled}
      onClick={() => onChange(!checked)}
    />
  )
}
