import type { ReactNode } from 'react';

interface Props {
  path: string | null | undefined;
  /** What to say when there is no path. */
  empty?: ReactNode;
  /** Colours a path the save engine will expand per machine (a template) as healthy, not plain data. */
  tone?: 'default' | 'safe';
  title?: string;
}

/** A path, read-only, in the prototype's `.path` inset: mono, one line, ellipsised — the full value is
 *  its tooltip, because a truncated path is the one you most need to read in full. */
export function PathField({ path, empty = 'not set', tone = 'default', title }: Props) {
  return (
    <span
      title={title ?? path ?? undefined}
      className={`block min-w-0 font-mono text-[11px] bg-tile border border-line rounded-lg px-2.5 py-[7px]
        overflow-hidden text-ellipsis whitespace-nowrap ${path ? (tone === 'safe' ? 'text-safe-ink' : 'text-dim') : 'text-dim italic'}`}
    >
      {path || empty}
    </span>
  );
}
