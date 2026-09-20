import type { ReactNode } from 'react'

export type Tone = 'default' | 'ok' | 'warn' | 'crit'

interface Props {
  tone?: Tone
  children: ReactNode
}

const TONE: Record<Tone, string> = {
  default: '',
  ok: 'sl-chip--ok',
  warn: 'sl-chip--warn',
  crit: 'sl-chip--crit',
}

/** plan.md colour rule: `ok` = healthy (green/olive), `warn` = failed but retrying (amber), `crit` =
 *  a decision is waiting (accent). `default` carries no meaning — plain metadata. */
export function Chip({ tone = 'default', children }: Props) {
  return <span className={`sl-chip ${TONE[tone]}`.trim()}>{children}</span>
}
