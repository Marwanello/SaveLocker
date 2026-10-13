import { useEffect, useState } from 'react';
import { api } from '../../api';
import type { ContentTransfer, GameContent } from '../../types';
import { ago, fmtSize } from '../../format';
import { Card } from '../ui/Card';

interface Props {
  gameId: string;
}

const ACTION: Record<string, string> = {
  already: 'Already set to the game’s profile',
  switched: 'Switched to the game’s profile',
  asked: 'Switched, and asked',
  kept: 'Asked — kept the other profile',
  left: 'Left as it was (setting: leave)',
  running: 'Left as it was (Xenia already open)',
};

/**
 * A Xenia game's "Profile at launch" table and its DLC hand-overs on the console's game page (tasks/emulator-saves
 * Phase 5c): per machine, which profile Xenia was set to at the game's last launch and what SaveLocker did about it —
 * reported by the agent each time the launch check runs — and the game's recent hand-overs, newest first.
 */
export function LaunchProfileCard({ gameId }: Props) {
  const [content, setContent] = useState<GameContent | null>(null);
  const [transfers, setTransfers] = useState<ContentTransfer[]>([]);

  useEffect(() => {
    let live = true;
    api.getGameContent(gameId).then(c => { if (live) setContent(c); }).catch(() => { if (live) setContent(null); });
    api.getGameTransfers(gameId).then(t => { if (live) setTransfers(t); }).catch(() => { if (live) setTransfers([]); });
    return () => { live = false; };
  }, [gameId]);

  const launches = (content?.machines ?? []).filter(m => m.launch);
  if (launches.length === 0 && transfers.length === 0) return null;
  const tag = (xuid?: string | null, name?: string | null) => xuid ? (name ? `${name} (${xuid})` : xuid) : 'no profile';

  return (
    <Card title="Xbox 360 profile at launch" headerRight={<span className="text-[11px] text-dim">the last launch on each machine</span>} flush>
      {launches.length > 0 && (
        <div className="overflow-x-auto">
          <table className="w-full text-[12.5px]">
            <thead>
              <tr className="text-left text-[10px] tracking-[0.12em] uppercase text-faint">
                <th className="px-4 py-2 font-normal">Machine</th>
                <th className="px-4 py-2 font-normal">Xenia was set to</th>
                <th className="px-4 py-2 font-normal">What SaveLocker did</th>
              </tr>
            </thead>
            <tbody>
              {launches.map(m => {
                const l = m.launch!;
                const other = !!l.configuredXuid && l.configuredXuid !== l.gameXuid;
                const startedOther = l.action === 'kept' || l.action === 'left' || l.action === 'running';
                return (
                  <tr key={m.machineId} className="border-t border-line">
                    <td className="px-4 py-2 font-semibold">{m.machineName}<div className="text-[11px] text-dim font-normal">{ago(l.at)}</div></td>
                    <td className={`px-4 py-2 ${other ? 'text-watch-ink' : ''}`}>{tag(l.configuredXuid, l.configuredTag)}</td>
                    <td className={`px-4 py-2 ${startedOther && other ? 'text-watch-ink font-semibold' : ''}`}>
                      {ACTION[l.action] ?? l.action}
                      {startedOther && other && <div className="text-[11px] text-dim font-normal">the game started without its saves</div>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {transfers.length > 0 && (
        <div className="px-4 py-2.5 border-t border-line text-[12px]">
          <div className="text-[10px] tracking-[0.12em] uppercase text-faint mb-1.5">DLC hand-overs</div>
          <ul className="flex flex-col gap-1">
            {transfers.map(t => (
              <li key={t.id} className="flex flex-wrap gap-x-3">
                <span>{t.fromMachineName} → {t.toMachineName}</span>
                <span className="text-dim">{t.parts.length} pack{t.parts.length === 1 ? '' : 's'}, {fmtSize(t.parts.reduce((n, p) => n + p.size, 0))}</span>
                <span className={t.status === 'Delivered' ? 'text-safe-ink' : t.status === 'Declined' || t.status === 'Expired' ? 'text-watch-ink' : 'text-dim'}>{t.status.toLowerCase()}</span>
                <span className="text-faint">{ago(t.createdAt)}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  );
}
