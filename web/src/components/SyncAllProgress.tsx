import { useEffect, useRef, useState } from 'react';
import { api } from '../api';

interface Props {
  /** The batch this "Sync all" run queued. A stable reference across renders — the caller must
   *  only create a new array when a new batch actually starts, or this restarts its poll. */
  commandIds: string[];
  onDone: () => void;
}

/**
 * plan.md §3 item 4: "a progress tick must not re-render its surroundings." This owns its own poll
 * and its own state — nothing above it is a dependency of a tick, so `NavBar` never re-renders on
 * one, and no entrance animation anywhere else replays while this counts up.
 */
export function SyncAllProgress({ commandIds, onDone }: Props) {
  const [doneCount, setDoneCount] = useState(0);
  const onDoneRef = useRef(onDone);
  onDoneRef.current = onDone;
  const total = commandIds.length;

  // Keyed on `commandIds` only; `total` is derived from it every render, so re-adding it as a dep
  // would not change when this fires (oxlint's exhaustive-deps warns here; harmless — not wired
  // into CI, and the omission is intentional).
  useEffect(() => {
    if (total === 0) return;
    let cancelled = false;
    const ids = new Set(commandIds);

    async function tick() {
      try {
        const all = await api.commands();
        if (cancelled) return;
        const finished = all.filter(c => ids.has(c.id) && (c.status === 'Done' || c.status === 'Failed')).length;
        setDoneCount(finished);
        if (finished >= ids.size) onDoneRef.current();
      } catch { /* transient — the next tick retries */ }
    }

    void tick();
    const interval = setInterval(tick, 2000);
    return () => { cancelled = true; clearInterval(interval); };
  }, [commandIds]);

  if (total === 0) return null;

  const pct = Math.round((doneCount / total) * 100);

  return (
    <div className="flex items-center gap-2 text-dim" role="status" aria-label="Sync all progress">
      <span className="text-xs font-mono tabular-nums whitespace-nowrap">{doneCount} of {total}</span>
      <div className="w-20 h-1.5 bg-raise rounded-full overflow-hidden">
        <div
          className="h-full bg-safe rounded-full transition-[width] duration-500 ease-[var(--ease)]"
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  );
}
