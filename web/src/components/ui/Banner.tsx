import type { ReactNode } from 'react';
import { Dot } from './Dot';

type Tone = 'accent' | 'watch' | 'safe';

const TONE: Record<Tone, { box: string; title: string; dot: 'crit' | 'warn' | 'ok' }> = {
  accent: { box: 'border-accent-line bg-accent-soft', title: 'text-accent-ink', dot: 'crit' },
  watch: { box: 'border-watch-line bg-watch-soft', title: 'text-watch-ink', dot: 'warn' },
  safe: { box: 'border-safe-line bg-safe-soft', title: 'text-safe-ink', dot: 'ok' },
};

interface Props {
  tone?: Tone;
  title: ReactNode;
  /** One line under the title: what happened, when, what happens next. */
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
}

/** The prototype's `.banner`. `accent` only when a decision is waiting (plan.md colour rule), `watch`
 *  for a problem that will retry, `safe` for "nothing needs you". */
export function Banner({ tone = 'accent', title, children, action, className = '' }: Props) {
  const t = TONE[tone];
  return (
    <div className={`flex items-center gap-[13px] px-4 py-[13px] rounded-[14px] border flex-wrap ${t.box} ${className}`}>
      <Dot tone={t.dot} />
      <div className="flex-1 min-w-[200px]">
        <strong className={`block text-[13.5px] font-semibold ${t.title}`}>{title}</strong>
        {children && <span className="block text-xs text-dim mt-[3px]">{children}</span>}
      </div>
      {action && <div className="flex gap-2 items-center flex-wrap">{action}</div>}
    </div>
  );
}
