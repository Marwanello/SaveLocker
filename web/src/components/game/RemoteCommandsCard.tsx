import type { Command } from '../../types';
import { asUtc, when } from '../../format';
import { card, cardHeader, sectionLabel, thStyle, tdStyle, tdMono, rowSep } from './legacyStyles';

/**
 * What a command with no result yet is actually waiting for. "Dispatched" used to be a dead end in
 * the UI as much as in the data: an agent that never answered left the row saying nothing forever.
 * Delivery is leased now, so the honest answer is either "an agent has it" or "its claim lapsed and
 * the next poll takes it again".
 */
const commandWaitText = (c: Command) => {
  if (c.status === 'Pending') return 'awaiting next poll…';
  if (c.status !== 'Dispatched') return '—';
  const lapsed = c.leaseExpiresAt && new Date(asUtc(c.leaseExpiresAt)) < new Date();
  return lapsed ? 'no reply — will be retried on the next poll' : 'running on the agent…';
};

interface Props {
  /** This game's newest commands. */
  commands: Command[];
}

/** The commands the console has queued for this game, and what each agent answered. */
export function RemoteCommandsCard({ commands }: Props) {
  return (
    <div style={card}>
      <div style={cardHeader}><span style={sectionLabel}>Recent Remote Commands</span></div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ background: 'var(--color-raise)' }}>
            <th style={{ ...thStyle, whiteSpace: 'nowrap' }}>When</th>
            <th style={thStyle}>Machine</th>
            <th style={thStyle}>Action</th>
            <th style={thStyle}>Status</th>
            <th style={thStyle}>Result</th>
          </tr>
        </thead>
        <tbody>
          {commands.map(c => (
            <tr key={c.id} style={rowSep}>
              <td style={{ ...tdMono, whiteSpace: 'nowrap' }}>{when(c.createdAt)}</td>
              <td style={tdStyle}>{c.machineName}</td>
              <td style={{ padding: '11px 18px', fontSize: 12, color: 'var(--color-fg)' }}>{c.type}{c.force ? ' (force)' : ''}</td>
              <td style={{ padding: '11px 18px', fontSize: 12, fontWeight: 600, whiteSpace: 'nowrap' }}>
                {c.status === 'Done'
                  ? <span style={{ color: 'var(--color-safe-ink)' }}>Done</span>
                  : c.status === 'Failed'
                    ? <span style={{ color: 'var(--color-watch-ink)' }}>Failed</span>
                    : <span style={{ color: 'var(--color-dim)' }}>{c.status}</span>
                }
                {c.claimCount > 1 && (
                  <span style={{ color: 'var(--color-watch-ink)', fontWeight: 400 }}> · retried ×{c.claimCount}</span>
                )}
              </td>
              <td style={{ padding: '11px 18px', fontSize: 11.5, color: 'var(--color-dim)', maxWidth: 340, wordBreak: 'break-word', lineHeight: 1.6 }}>
                {c.result || commandWaitText(c)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
