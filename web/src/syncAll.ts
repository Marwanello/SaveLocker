import { useSyncExternalStore } from 'react';
import { api } from './api';
import type { CancelCommandsResponse, Command } from './types';

/** One console "Sync all" batch — one command per machine (implementation.md, "A gap found while
 *  building Group 2"), so everything here counts machines, never games. */
export interface SyncAllProgress {
  total: number;
  /** Reached a terminal state: Done, Failed or Cancelled. */
  done: number;
  /** Still Pending — no agent has claimed them, so Cancel can still withdraw them. */
  pending: number;
}

export interface SyncAllOutcome {
  total: number;
  succeeded: number;
  cancelled: number;
  failures: { machine: string; reason: string }[];
  /** The wait gave up before every command finished. They are still queued and will run. */
  timedOut: boolean;
}

const TERMINAL = new Set<Command['status']>(['Done', 'Failed', 'Cancelled']);

/**
 * plan.md §3 item 4: "a progress tick must not re-render its surroundings." The poll lives here, not in
 * a component, and each reader subscribes to one slice through `useSyncExternalStore`: the rail and the
 * top bar's chip to `progress` (every tick), the top bar itself only to `active` (flips twice a batch).
 * A tick that changes nothing keeps the previous `progress` object, so not even the chip re-renders.
 */
class SyncAllTracker {
  private ids = new Set<string>();
  private progress: SyncAllProgress | null = null;
  private last: Command[] = [];
  private onDone: ((o: SyncAllOutcome) => void) | null = null;
  private interval: ReturnType<typeof setInterval> | undefined;
  private deadline: ReturnType<typeof setTimeout> | undefined;
  private inFlight = false;
  private sent = 0;
  private applied = 0;
  private listeners = new Set<() => void>();

  subscribe = (l: () => void) => { this.listeners.add(l); return () => { this.listeners.delete(l); }; };
  getProgress = () => this.progress;
  getActive = () => this.progress !== null;
  private emit() { this.listeners.forEach(l => l()); }

  start(commandIds: string[], onDone: (o: SyncAllOutcome) => void, maxWaitMs = 10 * 60_000) {
    this.stop();
    if (commandIds.length === 0) return;
    this.ids = new Set(commandIds);
    this.last = [];
    this.onDone = onDone;
    this.progress = { total: this.ids.size, done: 0, pending: this.ids.size };
    this.emit();
    void this.tick();
    this.interval = setInterval(() => void this.tick(), 2000);
    this.deadline = setTimeout(() => this.finish(true), maxWaitMs);
  }

  /** Withdraw every command of this batch no agent has claimed yet. The ones already running finish. */
  async cancel(): Promise<CancelCommandsResponse> {
    const open = [...this.ids].filter(id => {
      const c = this.last.find(x => x.id === id);
      return !c || !TERMINAL.has(c.status);
    });
    const r = await api.cancelCommands(open);
    await this.tick(true);
    return r;
  }

  private async tick(force = false) {
    // One timer tick at a time. A forced tick (right after Cancel, so the withdrawn commands show at
    // once) may overlap one already in flight — so every reply carries a sequence number, and one that
    // lands after a newer reply is dropped rather than moving the counter backwards.
    if (!force && this.inFlight) return;
    if (this.progress === null) return;
    const seq = ++this.sent;
    this.inFlight = true;
    try {
      const all = await api.commands();
      if (this.progress === null || seq < this.applied) return;
      this.applied = seq;
      this.last = all.filter(c => this.ids.has(c.id));
      const done = this.last.filter(c => TERMINAL.has(c.status)).length;
      // A command missing from the list (it holds the newest 50) is counted as still pending.
      const pending = this.ids.size - this.last.filter(c => c.status !== 'Pending').length;
      const p = this.progress;
      if (p.done !== done || p.pending !== pending) {
        this.progress = { total: p.total, done, pending };
        this.emit();
      }
      if (done >= this.ids.size) this.finish(false);
    } catch { /* transient — the next tick retries; the deadline still ends the wait */ }
    finally { this.inFlight = false; }
  }

  private finish(timedOut: boolean) {
    if (this.progress === null) return;
    const total = this.ids.size;
    const cb = this.onDone;
    const outcome: SyncAllOutcome = {
      total,
      succeeded: this.last.filter(c => c.status === 'Done').length,
      cancelled: this.last.filter(c => c.status === 'Cancelled').length,
      failures: this.last
        .filter(c => c.status === 'Failed')
        .map(c => ({ machine: c.machineName ?? 'A machine', reason: c.result ?? 'no reason given' })),
      timedOut,
    };
    this.stop();
    cb?.(outcome);
  }

  private stop() {
    clearInterval(this.interval);
    clearTimeout(this.deadline);
    const wasActive = this.progress !== null;
    this.progress = null;
    this.onDone = null;
    if (wasActive) this.emit();
  }
}

export const syncAll = new SyncAllTracker();

/** Every tick. Only the rail and the progress chip read this. */
export const useSyncAllProgress = () => useSyncExternalStore(syncAll.subscribe, syncAll.getProgress);
/** Flips when a batch starts and when it ends — what the top bar reads to swap Sync all for the chip. */
export const useSyncAllActive = () => useSyncExternalStore(syncAll.subscribe, syncAll.getActive);
