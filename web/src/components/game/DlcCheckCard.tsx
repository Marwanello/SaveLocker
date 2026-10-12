import { useEffect, useState } from 'react';
import { api } from '../../api';
import type { GameContent } from '../../types';
import { ago, fmtSize } from '../../format';
import { Card } from '../ui/Card';

interface Props {
  gameId: string;
  /** Bumped when the game's versions change, so the card follows the latest save. */
  refreshKey?: number;
}

/**
 * The DLC check on the console's game page (tasks/emulator-saves Phase 5c): which DLC and title updates the latest save
 * was made with, and which machines have them — so who can pull it safely shows before anyone tries. Machines report
 * names, IDs and sizes after every sync; nothing of the packages themselves is stored. Hidden for a game none of whose
 * saves said (any non-Xenia game, or one only older agents push).
 */
export function DlcCheckCard({ gameId, refreshKey = 0 }: Props) {
  const [content, setContent] = useState<GameContent | null>(null);

  useEffect(() => {
    let live = true;
    api.getGameContent(gameId).then(c => { if (live) setContent(c); }).catch(() => { if (live) setContent(null); });
    return () => { live = false; };
  }, [gameId, refreshKey]);

  const head = content?.headContent ?? null;
  if (!content || !head || head.length === 0) return null;
  const key = (p: { kind: string; id: string }) => `${p.kind}/${p.id}`.toLowerCase();

  return (
    <Card
      title="DLC check"
      headerRight={<span className="text-[11px] text-dim">
        latest save from {content.headMachineName ?? 'unknown'} · names and sizes only, 0 B of DLC stored
      </span>}
      flush
    >
      <div className="overflow-x-auto">
        <table className="w-full text-[12.5px]">
          <thead>
            <tr className="text-left text-[10px] tracking-[0.12em] uppercase text-faint">
              <th className="px-4 py-2 font-normal">Machine</th>
              <th className="px-4 py-2 font-normal">DLC and updates</th>
              <th className="px-4 py-2 font-normal">Can pull the latest save</th>
            </tr>
          </thead>
          <tbody>
            {content.machines.map(m => {
              const have = new Set(m.packages.map(key));
              const missing = head.filter(p => !have.has(key(p)));
              const missingSize = missing.reduce((n, p) => n + p.size, 0);
              return (
                <tr key={m.machineId} className="border-t border-line">
                  <td className="px-4 py-2 font-semibold">{m.machineName}<div className="text-[11px] text-dim font-normal">reported {ago(m.updatedAt)}</div></td>
                  <td className="px-4 py-2">
                    {missing.length === 0
                      ? <span className="text-safe-ink">All {head.length}</span>
                      : <span className="text-watch-ink font-semibold">
                          {head.length - missing.length} of {head.length} · missing {missing.map(p => p.name || p.id).join(', ')}
                        </span>}
                  </td>
                  <td className="px-4 py-2">
                    {missing.length === 0
                      ? <span className="text-safe-ink">Yes</span>
                      : <span className="text-watch-ink">Needs {missing.length} · {fmtSize(missingSize)}</span>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <div className="px-4 py-2.5 border-t border-line text-[11.5px] text-dim">
        The latest save was made with {head.length} package{head.length === 1 ? '' : 's'}: {head.map(p => p.name || p.id).join(', ')}.
        A machine missing some holds its pull until they are installed, or until someone pulls anyway on it.
      </div>
    </Card>
  );
}
