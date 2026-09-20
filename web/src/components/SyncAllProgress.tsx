import { useEffect, useRef, useState } from 'react';
import { api } from '../api';

export interface SyncAllOutcome {
  total: number;
  /** Commands that reached a terminal state (done or failed). */
  done: number;
  failures: { machine: string; reason: string }[];
  /** True when the wait gave up before every command finished. They are still queued and will run. */
  timedOut: boolean;
}

interface Props {
  /** The batch this "Sync all" run queued. A stable reference across renders — the caller must
   *  only create a new array when a new batch actually starts, or this restarts its poll. */
  commandIds: string[];
  onDone: (outcome: SyncAllOutcome) => void;
  /** Stop waiting after this long. A machine that goes offline mid-batch would otherwise leave
   *  this polling every two seconds for as long as the page stays open. */
  maxWaitMs?: number;
}

/**
 * plan.md §3 item 4: "a progress tick must not re-render its surroundings." This owns its own poll
 * and its own state — nothing above it is a dependency of a tick, so `NavBar` never re-renders on
 * one, and no entrance animation anywhere else replays while this counts up.
 */
export function SyncAllProgress({ commandIds, onDone, maxWaitMs = 10 * 60_000 }: Props) {
  const [doneCount, setDoneCount] = useState(0);
  const onDoneRef = useRef(onDone);
  useEffect(() => { onDoneRef.current = onDone; });

  useEffect(() => {
    if (commandIds.length === 0) return;
    const ids = new Set(commandIds);
    let stopped = false;
    let inFlight = false;
    let lastTerminal: { machineName?: string | null; status: string; result?: string | null }[] = [];
    let interval: ReturnType<typeof setInterval> | undefined;
    let deadline: ReturnType<typeof setTimeout> | undefined;

    function finish(timedOut: boolean) {
      if (stopped) return;
      stopped = true;
      clearInterval(interval);
      clearTimeout(deadline);
      onDoneRef.current({
        total: ids.size,
        done: lastTerminal.length,
        failures: lastTerminal
          .filter(c => c.status === 'Failed')
          .map(c => ({ machine: c.machineName ?? 'A machine', reason: c.result ?? 'no reason given' })),
        timedOut,
      });
    }

    async function tick() {
      // One request at a time: a slow reply must not let a second tick overtake it and move the
      // counter backwards.
      if (inFlight || stopped) return;
      inFlight = true;
      try {
        const all = await api.commands();
        if (stopped) return;
        lastTerminal = all.filter(c => ids.has(c.id) && (c.status === 'Done' || c.status === 'Failed'));
        setDoneCount(lastTerminal.length);
        if (lastTerminal.length >= ids.size) finish(false);
      } catch { /* transient — the next tick retries; the deadline below still ends the wait */ }
      finally { inFlight = false; }
    }

    void tick();
    interval = setInterval(() => void tick(), 2000);
    deadline = setTimeout(() => finish(true), maxWaitMs);
    return () => { stopped = true; clearInterval(interval); clearTimeout(deadline); };
  }, [commandIds, maxWaitMs]);

  const total = commandIds.length;
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
