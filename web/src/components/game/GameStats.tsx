import { api, errorText } from '../../api';
import type { GameSummary, Machine } from '../../types';
import { age, ago, fmtSize, plural, shortId } from '../../format';
import { toast, toastError } from '../../toast';
import { Stat } from '../ui/Stat';
import { InlineConfirm } from '../ui/InlineConfirm';

interface Props {
  summary: GameSummary;
  versionCount: number;
  machinesWithGame: Machine[];
  onRefresh: () => void;
}

/** plan.md Phase 10.3: Latest version, Stored, Machines, Lease — the numbers a person checks first,
 *  each with the one line of context that makes it mean something. */
export function GameStats({ summary, versionCount, machinesWithGame, onRefresh }: Props) {
  const { game, head, lease, totalStorageBytes } = summary;
  const holder = lease?.holderMachineName;

  async function forceRelease() {
    try {
      await api.forceRelease(game.id);
      toast(`Released the lease ${holder ?? 'a machine'} held.`);
      onRefresh();
    } catch (e) { toastError('Could not release the lease: ' + errorText(e)); }
  }

  return (
    <div className="grid grid-cols-[repeat(auto-fit,minmax(180px,1fr))] gap-3">
      <Stat
        label="Latest version"
        value={<span className="font-mono text-[21px]">{head ? shortId(head.id) : '—'}</span>}
        context={head ? `${ago(head.createdAt)} from ${head.machineName}` : 'no saves yet'}
      />
      <Stat label="Stored" value={fmtSize(totalStorageBytes)} context={`${plural(versionCount, 'version')} kept`} />
      <Stat
        label="Machines"
        value={machinesWithGame.length}
        context={<span className="block truncate" title={machinesWithGame.map(m => m.name).join(', ')}>
          {machinesWithGame.length > 0 ? machinesWithGame.map(m => m.name).join(', ') : 'none has synced it yet'}
        </span>}
      />
      <Stat
        label="Lease"
        value={holder ? 'Held' : 'Free'}
        context={holder
          ? <div className="flex flex-col items-start gap-2">
              <span>{holder}{lease?.acquiredAt ? `, ${age(lease.acquiredAt)}` : ''}</span>
              <InlineConfirm
                label="Force-release"
                consequence={`Frees the lease ${holder} holds. If ${holder} is still playing, its next push may become a conflict.`}
                confirmLabel="Force-release lease"
                onConfirm={forceRelease}
              />
            </div>
          : 'nothing checked out'}
      />
    </div>
  );
}
