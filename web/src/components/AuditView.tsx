import { useEffect, useState, useCallback } from 'react';
import { api, errorText } from '../api';
import type { AuditEntry } from '../types';
import { plural, toMs, when } from '../format';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Card } from './ui/Card';
import { Button } from './ui/Button';
import { Banner } from './ui/Banner';
import { SearchField } from './ui/SearchField';
import { FilterChips } from './ui/FilterChips';
import { DataTable } from './ui/DataTable';
import type { Column } from './ui/DataTable';
import { EmptyState } from './ui/EmptyState';

/** What the server hands back per request (`GET /audit?limit=`). The page searches only these. */
const LOADED = 200;

/** plan.md 12.1: the action is `accent-ink` when something failed or a decision was waiting, dim otherwise. */
const loud = (action: string) => /fail|conflict|lockout/.test(action);

function csvCell(v: string | null | undefined): string {
  const s = v ?? '';
  return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
}

/** Exports what the filter shows — which is only ever what was loaded, never the server's whole history. */
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

const stamp = (t: string) => new Date(toMs(t)).toLocaleString(undefined, {
  month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit',
});

const SERVER = '__server';

export function AuditView() {
  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [machine, setMachine] = useState('all');

  const load = useCallback(async () => {
    try {
      setEntries(await api.audit(LOADED));
      setError('');
    } catch (e) {
      setError(errorText(e));
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const all = entries ?? [];
  const machines = [...new Set(all.map(e => e.machineName).filter((n): n is string => !!n))].sort();
  const hasServer = all.some(e => !e.machineName);
  const q = query.trim().toLowerCase();
  const shown = all.filter(e =>
    (machine === 'all' || (machine === SERVER ? !e.machineName : e.machineName === machine)) &&
    (!q || `${e.action} ${e.gameName ?? ''} ${e.detail ?? ''}`.toLowerCase().includes(q)));
  const capped = all.length >= LOADED;

  const columns: Column<AuditEntry>[] = [
    { head: 'Time', cell: e => <span title={when(e.timestamp)}>{stamp(e.timestamp)}</span>, kind: 'm' },
    { head: 'Machine', cell: e => e.machineName ?? <span className="text-faint">server</span>, kind: 'm' },
    { head: 'Game', cell: e => e.gameName ?? <span className="text-faint">—</span>, kind: 'k' },
    { head: 'Action', cell: e => <span className={loud(e.action) ? 'text-accent-ink' : 'text-dim'}>{e.action}</span>, kind: 'm' },
    { head: 'Detail', cell: e => e.detail ?? '', kind: 'wrap' },
  ];

  return (
    <Page>
      <PageHead
        title="Audit log"
        sub={entries
          ? <>{shown.length} of {plural(all.length, 'event')} · newest first{capped && ` · the newest ${LOADED} only — older events are not searched`}</>
          : 'loading…'}
        actions={<>
          <SearchField value={query} onChange={setQuery} placeholder="Filter by action, game or detail" aria-label="Filter audit events" />
          <Button onClick={() => exportCsv(shown)} disabled={shown.length === 0}
            title={`Export the ${shown.length} event(s) shown as CSV`}>Export CSV</Button>
          <Button variant="quiet" onClick={() => void load()}>Refresh</Button>
        </>}
      />

      {error && <Banner tone="watch" title="Could not read the audit log">{error}</Banner>}

      {machines.length + (hasServer ? 1 : 0) > 1 && (
        <FilterChips
          aria-label="Filter by machine"
          value={machine}
          onChange={setMachine}
          options={[
            { value: 'all', label: 'All machines', count: all.length },
            ...machines.map(m => ({ value: m, label: m, count: all.filter(e => e.machineName === m).length })),
            ...(hasServer ? [{ value: SERVER, label: 'Server', count: all.filter(e => !e.machineName).length }] : []),
          ]}
        />
      )}

      <Card flush>
        <DataTable
          caption="Audit events, newest first"
          columns={columns}
          rows={shown}
          rowKey={e => e.id}
          empty={entries === null
            ? <div className="px-5 py-10 text-center text-[13px] text-dim">Loading…</div>
            : all.length === 0
              ? <EmptyState title="No events yet">Uploads, conflicts, sign-ins and settings changes are recorded here.</EmptyState>
              : <EmptyState title="Nothing matches that filter">Clear the search or pick a different machine.</EmptyState>}
        />
      </Card>
    </Page>
  );
}
