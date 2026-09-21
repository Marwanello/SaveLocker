interface Props {
  checked: boolean;
  onChange: (next: boolean) => void;
  'aria-label': string;
  disabled?: boolean;
}

/** plan.md's toggle: the knob springs across (260 ms, a slight overshoot) and the track takes the soft
 *  accent — a switch being ON is not a decision waiting, so it is tinted, never filled. */
export function Switch({ checked, onChange, disabled = false, ...rest }: Props) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={rest['aria-label']}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative w-[42px] h-6 rounded-full border shrink-0 cursor-pointer
        transition-[background-color,border-color] duration-[220ms] ease-[var(--ease)]
        disabled:opacity-50 disabled:cursor-default
        focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
        ${checked ? 'bg-accent-soft border-accent-line' : 'bg-tile border-line'}`}
    >
      <span
        aria-hidden
        className={`absolute top-[2px] left-[2px] w-[18px] h-[18px] rounded-full
          transition-[transform,background-color] duration-[260ms] ease-[cubic-bezier(.34,1.45,.5,1)]
          ${checked ? 'translate-x-[18px] bg-accent' : 'bg-dim'}`}
      />
    </button>
  );
}
