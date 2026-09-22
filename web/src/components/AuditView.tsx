import { useEffect, useState, useCallback } from 'react';
import { api } from '../api';
import type { AuditEntry } from '../types';

// plan.md colour rule, applied to history: ok = it went fine, warn = it needed a second look or removed
// something, crit = a decision was waiting (a conflict), info/mute = neutral bookkeeping. The old map used
// six hex colours; a badge is now one of five tones, each built from the same derived tokens as Chip.
type Tone = 'ok' | 'warn' | 'crit' | 'info' | 'mute';

const TONES: Record<Tone, { bg: string; line: string; ink: string }> = {
  ok:   { bg: 'var(--color-safe-soft)',   line: 'var(--color-safe-line)',   ink: 'var(--color-safe-ink)' },
  warn: { bg: 'var(--color-watch-soft)',  line: 'var(--color-watch-line)',  ink: 'var(--color-watch-ink)' },
  crit: { bg: 'var(--color-accent-soft)', line: 'var(--color-accent-line)', ink: 'var(--color-accent-ink)' },
  info: { bg: 'var(--color-raise)',       line: 'var(--color-line)',        ink: 'var(--color-fg)' },
  mute: { bg: 'var(--color-raise)',       line: 'var(--color-line)',        ink: 'var(--color-dim)' },
};

const ACTION_TONE: Record<string, Tone> = {
  'upload.create': 'ok',
  'upload.force': 'ok',
  'upload.conflict': 'crit',
  'conflict.resolve': 'ok',
  'lease.acquire': 'info',
  'lease.release': 'info',
  'lease.force_release': 'warn',
  'game.create': 'ok',
  'game.delete': 'warn',
  'game.enable': 'ok',
  'game.disable': 'mute',
  'game.save_dir': 'mute',
  'machine.register': 'ok',
  'machine.reregister': 'info',
  'machine.delete': 'warn',
  'machine_path.set': 'mute',
  'command.enqueue': 'info',
  'command.complete': 'ok',
  'enrollment.create': 'info',
  'enrollment.redeem': 'ok',
  'enrollment.revoke': 'warn',
  'enrollment.expire': 'mute',
  'agent_installer.upload': 'info',
  'agent_installer.fetch_github': 'info',
  'agent_installer.auto_fetch': 'info',
  'settings.appearance': 'mute',
};

function ActionBadge({ action }: { action: string }) {
  const tone = TONES[ACTION_TONE[action] ?? 'mute'];
  return (
    <span style={{
      display: 'inline-block',
      padding: '2px 7px',
      borderRadius: 4,
      fontSize: 11,
      fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
      background: tone.bg,
      color: tone.ink,
      border: `1px solid ${tone.line}`,
      whiteSpace: 'nowrap',
    }}>
      {action}
    </span>
  );
}

function csvCell(v: string | null | undefined): string {
  const s = v ?? '';
  return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
}

/** Exports what's currently loaded, not the server's whole history — GetAuditLogAsync caps at 200. */
function exportCsv(entries: AuditEntry[]) {
  const header = ['Time', 'Machine', 'Game', 'Action', 'Detail'];
  const rows = entries.map(e => [
    e.timestamp, e.machineName ?? '', e.gameName ?? '', e.action, e.detail ?? '',
  ].map(csvCell).join(','));
  const csv = [header.join(','), ...rows].join('\r\n');

  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `savelocker-audit-${new Date().toISOString().slice(0, 10)}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

function formatTs(iso: string) {
  const normalized = /[Z+]/.test(iso.slice(-6)) ? iso : iso + 'Z';
  const d = new Date(normalized);
  return d.toLocaleString(undefined, {
    month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

export function AuditView() {
  const [entries, setEntries] = useState<AuditEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setEntries(await api.audit());
    } catch (e) {
      setError('Failed to load audit log: ' + (e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  return (
    <div style={{ padding: '20px 24px', flex: 1, minHeight: 0, overflowY: 'auto' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
        <span style={{ color: 'var(--color-safe-ink)', fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.12em' }}>
          Audit Log — last {entries.length} events
        </span>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            onClick={() => exportCsv(entries)}
            disabled={entries.length === 0}
            title={`Export the ${entries.length} loaded event(s) as CSV`}
            style={{
              padding: '5px 13px', background: 'transparent', border: '1px solid var(--color-line)',
              borderRadius: 4, color: entries.length === 0 ? 'var(--color-dim)' : 'var(--color-fg)', fontSize: 12,
              cursor: entries.length === 0 ? 'default' : 'pointer', fontFamily: 'inherit',
            }}
          >
            ⤓ Export CSV
          </button>
          <button
            onClick={load}
            style={{
              padding: '5px 13px', background: 'transparent', border: '1px solid var(--color-line)',
              borderRadius: 4, color: 'var(--color-fg)', fontSize: 12, cursor: 'pointer', fontFamily: 'inherit',
            }}
          >
            ↻ Refresh
          </button>
        </div>
      </div>

      {error && <div style={{ color: 'var(--color-accent-ink)', fontSize: 13, marginBottom: 12 }}>{error}</div>}
      {loading && entries.length === 0 && (
        <div style={{ color: 'var(--color-dim)', fontSize: 13 }}>Loading…</div>
      )}

      {entries.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ borderBottom: '1px solid var(--color-line)' }}>
                {['Time', 'Machine', 'Game', 'Action', 'Detail'].map(h => (
                  <th key={h} style={{
                    padding: '6px 10px', textAlign: 'left',
                    color: 'var(--color-dim)', fontSize: 10, textTransform: 'uppercase',
                    letterSpacing: '0.09em', fontWeight: 600, whiteSpace: 'nowrap',
                  }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {entries.map((e, i) => (
                <tr
                  key={e.id}
                  style={{
                    borderBottom: '1px solid var(--color-row)',
                    background: i % 2 === 0 ? 'transparent' : 'color-mix(in oklab, var(--color-fg) 3%, transparent)',
                  }}
                >
                  <td style={{ padding: '7px 10px', color: 'var(--color-dim)', whiteSpace: 'nowrap', fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace", fontSize: 11 }}>
                    {formatTs(e.timestamp)}
                  </td>
                  <td style={{ padding: '7px 10px', color: 'var(--color-fg)', whiteSpace: 'nowrap' }}>
                    {e.machineName ?? <span style={{ color: 'var(--color-dim)' }}>—</span>}
                  </td>
                  <td style={{ padding: '7px 10px', color: 'var(--color-fg)', whiteSpace: 'nowrap' }}>
                    {e.gameName ?? <span style={{ color: 'var(--color-dim)' }}>—</span>}
                  </td>
                  <td style={{ padding: '7px 10px' }}>
                    <ActionBadge action={e.action} />
                  </td>
                  <td style={{
                    padding: '7px 10px', color: 'var(--color-dim)',
                    fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
                    fontSize: 11, maxWidth: 380,
                    overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                  }}>
                    {e.detail ?? ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {!loading && entries.length === 0 && !error && (
        <div style={{ color: 'var(--color-dim)', fontSize: 13 }}>No audit events yet.</div>
      )}
    </div>
  );
}
