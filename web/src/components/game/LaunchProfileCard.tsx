import { useEffect, useState } from 'react';
import { api } from '../../api';
import type { GameContent } from '../../types';
import { ago } from '../../format';
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
 * A Xenia game's "Profile at launch" table, in the console's Emulator settings (tasks/emulator-saves Phase 5c): per
 * machine, which profile Xenia was set to at the game's last launch and what SaveLocker did about it — reported by the
 * agent each time the launch check runs.
 */
export function LaunchProfileCard({ gameId }: Props) {
  const [content, setContent] = useState<GameContent | null>(null);

  useEffect(() => {
    let live = true;
    api.getGameContent(gameId).then(c => { if (live) setContent(c); }).catch(() => { if (live) setContent(null); });
    return () => { live = false; };
  }, [gameId]);

  const launches = (content?.machines ?? []).filter(m => m.launch);
  const tag = (xuid?: string | null, name?: string | null) => xuid ? (name ? `${name} (${xuid})` : xuid) : 'no profile';

  return (
    <Card title="Xbox 360 profile at launch" headerRight={<span className="text-[11px] text-dim">the last launch on each machine</span>} flush>
      {launches.length === 0 && (
        <div className="px-4 py-3 text-[12.5px] text-dim">
          No launch checked yet. Each machine reports here when it starts the game through SaveLocker (Steam's
          launch option, Decky or Playnite): which profile Xenia was set to, and what SaveLocker did about it.
        </div>
      )}
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
    </Card>
  );
}
