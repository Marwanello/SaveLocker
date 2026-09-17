import type { ReactNode } from 'react';

type Tone = 'default' | 'ok' | 'warn' | 'crit';

interface Props {
  tone?: Tone;
  children: ReactNode;
  className?: string;
}

const TONE: Record<Tone, string> = {
  default: 'text-dim border-line bg-raise',
  ok: 'text-safe-ink border-safe-line bg-safe-soft',
  warn: 'text-watch-ink border-watch-line bg-watch-soft',
  crit: 'text-accent-ink border-accent-line bg-accent-soft',
};

/** plan.md colour rule: `ok` = healthy (green/olive), `warn` = retrying (amber), `crit` = a decision
 *  is waiting (accent). `default` carries no meaning — plain metadata like "41 kept". */
export function Chip({ tone = 'default', children, className = '' }: Props) {
  return (
    <span className={`inline-flex items-center gap-1.5 text-[10.5px] px-2.5 py-1 rounded-full border ${TONE[tone]} ${className}`}>
      {children}
    </span>
  );
}
