import type { Command } from '../../types';
import { ago, asUtc, when } from '../../format';
import { Card } from '../ui/Card';
import { Chip } from '../ui/Chip';
import { Dot } from '../ui/Dot';
import { DataTable } from '../ui/DataTable';
import { EmptyState } from '../ui/EmptyState';

/**
 * What a command with no result yet is waiting for. Delivery is a lease, so the honest answer is
 * either "an agent has it" or "its claim lapsed and the next poll takes it again" — "Dispatched" used
 * to be a dead end that said nothing forever when an agent never answered.
 */
function waiting(c: Command) {
  if (c.status === 'Pending') return 'Waiting for the agent’s next poll.';
  if (c.status !== 'Dispatched') return '—';
  const lapsed = c.leaseExpiresAt && new Date(asUtc(c.leaseExpiresAt)) < new Date();
  return lapsed ? 'No reply — the next poll hands it out again.' : 'Running on the agent.';
}

function state(c: Command) {
  const retried = c.claimCount > 1 ? ` · ×${c.claimCount}` : '';
  switch (c.status) {
    case 'Done': return <Chip tone="ok">Done</Chip>;
    // Amber, not the accent: it failed and says why, but nothing here is waiting on a decision.
    case 'Failed': return <Chip tone="warn">Failed{retried}</Chip>;
    case 'Dispatched': return <Chip><Dot tone="warn" live />Running{retried}</Chip>;
    case 'Cancelled': return <Chip>Cancelled</Chip>;
    default: return <Chip>Queued</Chip>;
  }
}

/** plan.md Phase 10.5: the commands the console has sent for this game, and what each agent answered. */
export function RemoteCommandsCard({ commands }: { commands: Command[] }) {
  return (
    <Card title="Remote commands for this game" flush>
      <DataTable
        caption="Remote commands for this game"
        rows={commands}
        rowKey={c => c.id}
        empty={<EmptyState title="No remote commands yet">Push or Pull from this page and it shows up here with its result.</EmptyState>}
        columns={[
          { head: 'Machine', kind: 'k', cell: c => c.machineName ?? '—' },
          { head: 'Action', cell: c => `${c.type}${c.force ? ' (forced)' : ''}` },
          { head: 'State', cell: state },
          { head: 'Queued', kind: 'n', cell: c => <span title={when(c.createdAt)}>{ago(c.createdAt)}</span> },
          { head: 'Result', kind: 'wrap', cell: c => c.result || waiting(c) },
        ]}
      />
    </Card>
  );
}
