import { useCallback, useEffect, useState } from 'react';
import { api, errorText } from '../../api';
import type { ContentTransfer, Game, GameContent, HandoverOverview } from '../../types';
import { ago, fmtSize } from '../../format';
import { missingPacks } from '../../dlc';
import { toast, toastError } from '../../toast';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { KV } from '../ui/KV';
import { PageHead } from '../ui/PageHead';
import { Seg } from '../ui/Seg';
import { DlcCheckCard } from './DlcCheckCard';
import { LaunchProfileCard } from './LaunchProfileCard';

export type EmulatorTab = 'profile' | 'dlc' | 'handovers';

interface Props {
  game: Game;
  tab: EmulatorTab;
  onTab: (tab: EmulatorTab) => void;
  onBack: () => void;
  /** Bumped when the game's versions change, so the DLC check follows the latest save. */
  refreshKey: number;
}

const OPEN: ContentTransfer['status'][] = ['Requested', 'Approved', 'Ready'];

const STATUS: Record<ContentTransfer['status'], string> = {
  Requested: 'asked, not answered',
  Approved: 'sending',
  Ready: 'on the server, installing next',
  Delivered: 'delivered',
  Declined: 'declined',
  Cancelled: 'cancelled',
  Expired: 'expired',
};

/**
 * A game's emulator options across the fleet, reached from its page. Only the emulator the game runs in is shown; for
 * Xenia, each machine's Xbox 360 profile at its last launch, the DLC the latest save needs against what each machine has
 * (with a way to send a machine what it lacks), and the game's DLC hand-overs. Each machine still chooses its own launch
 * and sharing settings, in its agent.
 */
export function EmulatorSettings({ game, tab, onTab, onBack, refreshKey }: Props) {
  const [content, setContent] = useState<GameContent | null>(null);
  const [transfers, setTransfers] = useState<ContentTransfer[]>([]);
  const [overview, setOverview] = useState<HandoverOverview | null>(null);

  const load = useCallback(() => {
    api.getGameContent(game.id).then(setContent).catch(() => setContent(null));
    api.getGameTransfers(game.id).then(setTransfers).catch(() => setTransfers([]));
    api.handoverOverview().then(setOverview).catch(() => setOverview(null));
  }, [game.id]);
  useEffect(() => { load(); }, [load, refreshKey]);

  // While a hand-over is moving, follow it.
  const moving = transfers.some(t => OPEN.includes(t.status));
  useEffect(() => {
    if (!moving) return;
    const timer = window.setInterval(load, 5000);
    return () => window.clearInterval(timer);
  }, [moving, load]);

  async function send(machineId: string) {
    try {
      const t = await api.sendMissingDlc(game.id, machineId);
      toast(`Asked ${t.fromMachineName} to send ${t.toMachineName} ${t.parts.length} pack${t.parts.length === 1 ? '' : 's'}. ${t.toMachineName} installs them once they arrive.`, 5000);
    } catch (e) { toastError('Could not ask for the DLC: ' + errorText(e)); }
    load();
  }

  async function cancel(id: string) {
    try { await api.cancelTransfer(id); } catch (e) { toastError(errorText(e)); }
    load();
  }

  const head = content?.headContent ?? [];
  const short = (content?.machines ?? []).filter(m => missingPacks(head, m.packages).length > 0).length;
  const open = transfers.filter(t => OPEN.includes(t.status)).length;

  return (
    <>
      <div><Button size="sm" variant="quiet" onClick={onBack}>← {game.name}</Button></div>
      <PageHead
        title="Emulator settings"
        sub={`${game.name} runs in Xenia. Every machine that has it, side by side.`}
      />
      <div>
        <Seg<EmulatorTab>
          aria-label="Xenia settings"
          value={tab}
          onChange={onTab}
          options={[
            { value: 'profile', label: 'Xbox 360 profile' },
            { value: 'dlc', label: short > 0 ? `DLC and title updates · ${short} missing some` : 'DLC and title updates' },
            { value: 'handovers', label: open > 0 ? `Hand-overs · ${open} open` : 'Hand-overs' },
          ]}
        />
      </div>

      {tab === 'profile' && <LaunchProfileCard gameId={game.id} />}

      {tab === 'dlc' && <DlcCheckCard content={content} transfers={transfers} onSend={send} />}

      {tab === 'handovers' && (
        <>
          <Card title="Hand-overs for this game" headerRight={<span className="text-[11px] text-dim">packs passed between machines, deleted on delivery</span>} flush>
            {transfers.length === 0 && (
              <div className="px-4 py-3 text-[12.5px] text-dim">
                None yet. A machine whose pull is held for missing packs asks for them, or send them from DLC and title updates.
              </div>
            )}
            {transfers.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full text-[12.5px]">
                  <thead>
                    <tr className="text-left text-[10px] tracking-[0.12em] uppercase text-faint">
                      <th className="px-4 py-2 font-normal">From → to</th>
                      <th className="px-4 py-2 font-normal">Packs</th>
                      <th className="px-4 py-2 font-normal">Status</th>
                      <th className="px-4 py-2 font-normal">Asked</th>
                      <th className="px-4 py-2 font-normal"></th>
                    </tr>
                  </thead>
                  <tbody>
                    {transfers.map(t => {
                      const got = t.parts.reduce((n, p) => n + p.received, 0);
                      const total = t.parts.reduce((n, p) => n + (p.zipSize ?? p.size), 0);
                      const isOpen = OPEN.includes(t.status);
                      return (
                        <tr key={t.id} className="border-t border-line">
                          <td className="px-4 py-2 font-semibold">{t.fromMachineName} → {t.toMachineName}</td>
                          <td className="px-4 py-2 text-dim">{t.parts.length} · {fmtSize(t.parts.reduce((n, p) => n + p.size, 0))}</td>
                          <td className={`px-4 py-2 ${t.status === 'Delivered' ? 'text-safe-ink' : t.status === 'Declined' || t.status === 'Expired' ? 'text-watch-ink' : 'text-dim'}`}>
                            {STATUS[t.status]}{t.status === 'Approved' ? `, ${fmtSize(got)} of ${fmtSize(total)}` : ''}
                          </td>
                          <td className="px-4 py-2 text-faint">{ago(t.createdAt)}</td>
                          <td className="px-4 py-2 text-right">
                            {isOpen && <Button size="sm" variant="quiet" onClick={() => void cancel(t.id)}>Cancel</Button>}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
          {overview && (
            <Card title="Limits">
              <KV items={[
                { label: 'Largest hand-over', value: `${fmtSize(overview.maxBytes)} — Storage:MaxHandoverMb, separate from the save upload limit` },
                { label: 'A request waits', value: `${overview.expiryDays} day${overview.expiryDays === 1 ? '' : 's'}, then it and its files are deleted — Storage:HandoverExpiryDays` },
                { label: 'Held right now', value: `${fmtSize(overview.bytesInTransit)} across every game` },
              ]} />
              <p className="text-xs text-faint mt-3">
                Each machine chooses whether it sends packs when asked, in its agent's Emulator settings: automatically (the
                default), ask first, or never.
              </p>
            </Card>
          )}
        </>
      )}

      <p className="text-[11.5px] text-dim">
        Only the emulator this game runs in is shown. A game in another emulator lists that emulator's options here.
      </p>
    </>
  );
}
