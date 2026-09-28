import { api } from '../../api';
import type { Game, Machine, Version } from '../../types';
import { when } from '../../format';
import { card, cardHeader, sectionLabel, thStyle, tdStyle, tdMono, rowSep } from './legacyStyles';

interface Props {
  game: Game;
  machines: Machine[];
  /** Each machine's newest version of this game, keyed by machine id. */
  latestByMachine: Record<string, Version>;
  onRefresh: () => void;
}

/** Every machine, its last upload of this game, and the remote Pull / Push / Sync for it. */
export function MachinesCard({ game, machines, latestByMachine, onRefresh }: Props) {
  async function handleCmd(machineId: string, type: string, force: boolean) {
    try { await api.queueCommand(machineId, game.id, type, force); onRefresh(); } catch (e) { alert('Could not queue command: ' + (e as Error).message); }
  }

  return (
    <div style={card}>
      <div style={cardHeader}><span style={sectionLabel}>Machines</span></div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ background: 'var(--color-raise)' }}>
            <th style={thStyle}>Machine</th>
            <th style={thStyle}>Last upload (this game)</th>
            <th style={thStyle}>Last seen</th>
            <th style={thStyle}>Remote actions</th>
          </tr>
        </thead>
        <tbody>
          {machines.length === 0
            ? <tr><td colSpan={4} style={{ padding: '20px 18px', color: 'var(--color-dim)', fontSize: 13 }}>No machines registered.</td></tr>
            : machines.map(m => {
                const last = latestByMachine[m.id];
                return (
                  <tr key={m.id} style={rowSep}>
                    <td style={tdStyle}>{m.name}</td>
                    <td style={tdMono}>{last ? when(last.createdAt) : '—'}</td>
                    <td style={tdMono}>{when(m.lastSeen)}</td>
                    <td style={{ padding: '11px 18px' }}>
                      <div style={{ display: 'flex', gap: 5 }}>
                        <button style={{ padding: '4px 10px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }} onClick={() => handleCmd(m.id, 'Pull', true)}>Pull</button>
                        <button style={{ padding: '4px 10px', border: '1px solid var(--color-line)', color: 'var(--color-fg)', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }} onClick={() => handleCmd(m.id, 'Push', true)}>Push</button>
                        <button style={{ padding: '4px 10px', border: 'none', color: 'var(--color-on-accent)', background: 'var(--color-accent)', borderRadius: 4, fontSize: 11, cursor: 'pointer', fontWeight: 500 }} onClick={() => handleCmd(m.id, 'Sync', false)}>Sync</button>
                      </div>
                    </td>
                  </tr>
                );
              })
          }
        </tbody>
      </table>
    </div>
  );
}
