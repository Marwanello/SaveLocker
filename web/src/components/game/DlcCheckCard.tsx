import { useState } from 'react';
import type { ContentTransfer, GameContent } from '../../types';
import { ago, fmtSize } from '../../format';
import { missingPacks } from '../../dlc';
import { Card } from '../ui/Card';
import { Button } from '../ui/Button';

interface Props {
  content: GameContent | null;
  /** The game's hand-overs, so a machine already being sent its packs says so instead of offering to send again. */
  transfers: ContentTransfer[];
  /** Hand a machine the packs it lacks, from the machine that made the latest save. */
  onSend: (machineId: string) => Promise<void>;
}

const OPEN: ContentTransfer['status'][] = ['Requested', 'Approved', 'Ready'];

/**
 * The DLC check in the console's Emulator settings (tasks/emulator-saves Phase 5c): which DLC and title updates the latest
 * save was made with, and which machines have them — so who can pull it safely shows before anyone tries — with a way to
 * send a machine what it lacks. Machines report names, IDs and sizes after every sync; nothing of the packages themselves
 * is stored.
 */
export function DlcCheckCard({ content, transfers, onSend }: Props) {
  const [sending, setSending] = useState<string | null>(null);

  const head = content?.headContent ?? null;
  if (!content || !head || head.length === 0) {
    return (
      <Card title="DLC and title updates">
        <p className="text-[12.5px] text-dim">
          {content?.headMachineName
            ? `The latest save, from ${content.headMachineName}, needs no DLC or title updates.`
            : 'Nothing is known about DLC yet: no save of this game has said which packs it was made with.'}
        </p>
      </Card>
    );
  }
  async function send(machineId: string) {
    setSending(machineId);
    try { await onSend(machineId); } finally { setSending(null); }
  }

  return (
    <Card
      title="DLC and title updates"
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
              const missing = missingPacks(head, m.packages);
              const missingSize = missing.reduce((n, p) => n + p.size, 0);
              const open = transfers.find(t => t.toMachineId === m.machineId && OPEN.includes(t.status));
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
                    {missing.length === 0 && <span className="text-safe-ink">Yes</span>}
                    {missing.length > 0 && (
                      <div className="flex flex-col items-start gap-1.5">
                        <span className="text-watch-ink">Needs {missing.length} · {fmtSize(missingSize)}</span>
                        {open
                          ? <span className="text-[11.5px] text-dim">
                              {open.status === 'Requested' ? `Asked ${open.fromMachineName}, waiting for it` : open.status === 'Approved' ? `${open.fromMachineName} is sending them` : 'On the server, installing next'}
                            </span>
                          : content.headMachineName && m.machineName !== content.headMachineName && (
                            <Button size="sm" disabled={sending !== null} onClick={() => void send(m.machineId)}>
                              {sending === m.machineId ? 'Asking…' : `Send from ${content.headMachineName}`}
                            </Button>
                          )}
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <div className="px-4 py-2.5 border-t border-line text-[11.5px] text-dim">
        The latest save was made with {head.length} package{head.length === 1 ? '' : 's'}: {head.map(p => p.name || p.id).join(', ')}.
        A machine missing some holds its pull until they are installed, or until someone pulls anyway on it. Sending passes
        them through your server, which deletes them once installed.
      </div>
    </Card>
  );
}
