import { Icon } from './Icon';
import { LOGOS } from './osLogos';
import type { LogoKey } from './osLogos';
import type { MachineOs } from '../../machineOs';
import { osLine } from '../../machineOs';

/** Decorative, like `Icon`: whatever holds it names the machine. Null is "not reported yet" — a monitor. */
export function OsLogo({ logo, size = 14, className = '' }: { logo: LogoKey | null; size?: number; className?: string }) {
  if (!logo) return <Icon name="monitor" size={size} className={className} />;
  const { viewBox, nodes } = LOGOS[logo];
  return (
    <svg width={size} height={size} viewBox={viewBox} aria-hidden="true" focusable="false" className={`shrink-0 ${className}`}>
      {nodes.map((n, i) => n.stroke
        ? <path key={i} d={n.d} fill="none" stroke="currentColor" strokeWidth={n.stroke} />
        : <path key={i} d={n.d} fill="currentColor" fillRule={n.evenOdd ? 'evenodd' : undefined} clipRule={n.evenOdd ? 'evenodd' : undefined} />)}
    </svg>
  );
}

const TILE = {
  sm: { box: 'w-[22px] h-[22px] rounded-[6px]', logo: 12 },
  md: { box: 'w-[28px] h-[28px] rounded-[7px]', logo: 15 },
} as const;

/**
 * The machine's OS logo on a small tile — the agent's conflict card puts Local/Cloud icons in the same
 * place. `tone="safe"` is the chosen side of a decision. The tooltip names the OS and the device.
 */
export function OsBadge({ os, size = 'md', tone = 'default' }: { os: MachineOs; size?: keyof typeof TILE; tone?: 'default' | 'safe' }) {
  const t = TILE[size];
  return (
    <span
      title={osLine(os) ?? 'Operating system not reported yet'}
      className={`${t.box} grid place-items-center shrink-0 transition-colors duration-150 ease-[var(--ease)]
        ${tone === 'safe' ? 'bg-safe-soft text-safe-ink' : 'bg-raise text-dim'}`}
    >
      <OsLogo logo={os.logo} size={t.logo} />
    </span>
  );
}
