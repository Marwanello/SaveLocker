import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';

export interface BalancedItem {
  key: string;
  node: ReactNode;
  /** Always in this column (0 = the wide left one); otherwise the item goes to whichever column is shorter. */
  pin?: 0 | 1;
}

const GAP = 12;
/** A card stays where it is unless the other column is shorter by more than this. Moving a card changes its
 *  width and so its height — without the margin two near-equal columns could trade it back and forth. */
const STICKY = 40;
const WIDE = '(min-width: 1280px)';

function useWide(): boolean {
  const [wide, setWide] = useState(() => window.matchMedia(WIDE).matches);
  useEffect(() => {
    const mq = window.matchMedia(WIDE);
    const on = () => setWide(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, []);
  return wide;
}

/**
 * tasks/versions-panel-fill, idea A: two columns (1.45 : 1) where every unpinned card goes to the shorter one, so a
 * short Versions list gets the right column's lower cards under it instead of an empty well. One grid of 1 px rows,
 * each card spanning its measured height: a card that changes column keeps its place in the tree, so nothing in it
 * (a half-typed path, an open confirm) is remounted. Below xl it is one column in the given order.
 */
export function BalancedColumns({ items }: { items: BalancedItem[] }) {
  const wide = useWide();
  const els = useRef(new Map<string, HTMLDivElement>());
  const [heights, setHeights] = useState<Record<string, number>>({});
  const [cols, setCols] = useState<Record<string, 0 | 1>>({});
  const keys = items.map(i => i.key).join(',');

  useLayoutEffect(() => {
    function measure() {
      const next: Record<string, number> = {};
      els.current.forEach((el, k) => { next[k] = Math.ceil(el.getBoundingClientRect().height); });
      setHeights(prev => (Object.keys(next).length === Object.keys(prev).length
        && Object.entries(next).every(([k, h]) => prev[k] === h) ? prev : next));
    }
    measure();
    const ro = new ResizeObserver(measure);
    els.current.forEach(el => ro.observe(el));
    return () => ro.disconnect();
  }, [keys]);

  // A layout effect too: placed after paint, every card would first show in its default column and then jump.
  useLayoutEffect(() => {
    setCols(prev => {
      const totals = [0, 0];
      const next: Record<string, 0 | 1> = {};
      for (const it of items) {
        let c: 0 | 1;
        if (it.pin !== undefined) c = it.pin;
        else {
          const kept = prev[it.key] ?? 1;
          const shorter = totals[0] <= totals[1] ? 0 : 1;
          c = Math.abs(totals[0] - totals[1]) > STICKY ? shorter : kept;
        }
        next[it.key] = c;
        totals[c] += (heights[it.key] ?? 0) + GAP;
      }
      return items.every(it => prev[it.key] === next[it.key]) ? prev : next;
    });
  }, [heights, keys]); // eslint-disable-line react-hooks/exhaustive-deps -- items' nodes change every render; keys and heights are what placement reads

  return (
    // -mb-3: every item carries one gap under it, the last one included.
    <div className="grid grid-cols-1 xl:grid-cols-[minmax(0,1.45fr)_minmax(0,1fr)] gap-x-3 grid-flow-row-dense auto-rows-[1px] -mb-3">
      {items.map(it => (
        <div
          key={it.key}
          className="min-w-0"
          style={{
            gridColumn: wide ? (cols[it.key] ?? it.pin ?? 1) + 1 : 1,
            gridRowEnd: `span ${Math.max(1, (heights[it.key] ?? 0) + GAP)}`,
          }}
        >
          <div ref={el => { if (el) els.current.set(it.key, el); else els.current.delete(it.key); }}>{it.node}</div>
        </div>
      ))}
    </div>
  );
}
