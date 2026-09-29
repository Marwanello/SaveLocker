export type DotTone = 'ok' | 'warn' | 'crit' | 'idle';

const TONE: Record<DotTone, string> = {
  ok: 'bg-safe',
  warn: 'bg-watch',
  crit: 'bg-accent',
  idle: 'bg-faint',
};

interface Props {
  tone: DotTone;
  /** Pulses — something is happening right now. */
  live?: boolean;
  /** When set, the dot is announced (it is the only thing saying the state); otherwise decorative. */
  label?: string;
  className?: string;
}

/** A 7px state dot, in the colour rule's three meanings plus `idle` (disabled, nothing expected). */
export function Dot({ tone, live = false, label, className = '' }: Props) {
  return (
    <span
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      title={label}
      className={`inline-block w-[7px] h-[7px] rounded-full shrink-0 ${TONE[tone]} ${live ? 'animate-pulse-dot' : ''} ${className}`}
    />
  );
}
