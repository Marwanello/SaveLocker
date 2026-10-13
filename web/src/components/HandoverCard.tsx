import { useCallback, useEffect, useState } from 'react';
import { api } from '../api';
import type { HandoverOverview } from '../types';
import { ago, fmtSize } from '../format';
import { toastError } from '../toast';
import { Card } from './ui/Card';
import { KV } from './ui/KV';
import { Button } from './ui/Button';

/**
 * DLC hand-overs on the Configuration page (tasks/emulator-saves Phase 5c): the size limit and how long a request waits
 * (server settings, <c>Storage:MaxHandoverMb</c> and <c>Storage:HandoverExpiryDays</c>), what the server holds for them
 * right now, and every open one, which the console can cancel. Packs never stay: delivery or expiry deletes them.
 */
export function HandoverCard() {
  const [data, setData] = useState<HandoverOverview | null>(null);

  const load = useCallback(() => {
    api.handoverOverview().then(setData).catch(() => setData(null));
  }, []);
  useEffect(() => { load(); }, [load]);

  if (!data) return null;

  async function cancel(id: string) {
    try { await api.cancelTransfer(id); } catch (err) { toastError(err); }
    load();
  }

  return (
    <Card title="DLC hand-overs" headerRight={<span className="text-[11.5px] text-dim">packs passed between machines, deleted on delivery</span>}>
      <KV items={[
        { label: 'Largest hand-over', value: `${fmtSize(data.maxBytes)} — Storage:MaxHandoverMb, separate from the save upload limit` },
        { label: 'A request waits', value: `${data.expiryDays} day${data.expiryDays === 1 ? '' : 's'}, then it and its files are deleted — Storage:HandoverExpiryDays` },
        { label: 'Held right now', value: `${fmtSize(data.bytesInTransit)} in ${data.open.length} open hand-over${data.open.length === 1 ? '' : 's'}` },
      ]} />
      {data.open.length > 0 && (
        <ul className="mt-3.5 flex flex-col gap-2">
          {data.open.map(t => {
            const got = t.parts.reduce((n, p) => n + p.received, 0);
            const total = t.parts.reduce((n, p) => n + (p.zipSize ?? p.size), 0);
            return (
              <li key={t.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 rounded-md border border-line px-3 py-2 text-[12.5px]">
                <span className="font-semibold">{t.gameName}</span>
                <span className="text-dim">{t.fromMachineName} → {t.toMachineName}</span>
                <span className="text-dim">{t.status === 'Requested' ? 'asked, not answered' : t.status === 'Approved' ? `sending, ${fmtSize(got)} of ${fmtSize(total)}` : 'on the server, waiting to be fetched'}</span>
                <span className="text-faint">asked {ago(t.createdAt)}</span>
                <span className="ml-auto"><Button size="sm" variant="quiet" onClick={() => void cancel(t.id)}>Cancel</Button></span>
              </li>
            );
          })}
        </ul>
      )}
      <p className="text-xs text-faint mt-3">
        Each machine chooses whether it sends packs when asked (agent Settings → Xbox 360): ask first, automatically, or never.
      </p>
    </Card>
  );
}
