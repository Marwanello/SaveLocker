interface Props {
  /** 0–1; clamped. */
  value: number;
  /** `safe` for a quantity that is fine (storage used), `accent` for progress toward a decision. */
  tone?: 'safe' | 'accent' | 'watch';
  'aria-label': string;
  className?: string;
}

const FILL = { safe: 'bg-safe', accent: 'bg-accent', watch: 'bg-watch' };

/** The prototype's `.meter`: a 7px track whose fill transitions its width — progress never jumps. */
export function Meter({ value, tone = 'safe', className = '', ...rest }: Props) {
  const pct = Math.round(Math.min(1, Math.max(0, value)) * 100);
  return (
    <div
      role="meter" aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct} aria-label={rest['aria-label']}
      className={`h-[7px] rounded-full bg-tile overflow-hidden ${className}`}
    >
      <div className={`h-full rounded-full transition-[width] duration-[550ms] ease-[cubic-bezier(.3,.9,.3,1)] ${FILL[tone]}`} style={{ width: `${pct}%` }} />
    </div>
  );
}
