import { useState, useEffect } from 'react';
import { HelpMarkdown } from './HelpMarkdown';
import { releases, releaseFor } from '../releases/index';
import type { Release } from '../releases/index';
import { api } from '../api';
import type { AgentHealth, AgentInstallerStatus, ServerBuildInfo } from '../types';
import { compareVersions, normalizeVersion, parseVersion } from '../versionSkew';
import { Page } from './ui/Page';
import { PageHead } from './ui/PageHead';
import { Card } from './ui/Card';
import { Chip } from './ui/Chip';
import { Banner } from './ui/Banner';
import { Button } from './ui/Button';
import { DataTable } from './ui/DataTable';
import type { Column } from './ui/DataTable';

function getVersionFromHash(): string | null {
  const m = location.hash.match(/^#whats-new\/(.+)$/);
  return m ? m[1] : null;
}

/** A release's one-line headline: its opening bold sentence, else its first paragraph. */
function headline(r: Release): string {
  const body = r.content.split('\n').filter(l => l.trim() && !l.startsWith('#') && !/^_Released/.test(l));
  const first = body.join(' ');
  const bold = /^\*\*(.+?)\*\*/.exec(first);
  const text = (bold ? bold[1] : first).replace(/[*_`]/g, '');
  return text.length > 160 ? text.slice(0, 157) + '…' : text;
}

const newer = (a: string | null | undefined, b: string | null | undefined) => {
  const pa = parseVersion(a), pb = parseVersion(b);
  return !!pa && !!pb && compareVersions(pa, pb) > 0;
};

const fmtDate = (d: string) => new Date(d + 'T00:00:00Z').toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });

const SLOT: Record<string, string> = { Windows: 'win-x64', Linux: 'linux-x64' };

interface Props {
  build?: ServerBuildInfo;
  /** The fleet, when signed in. What's new is readable while locked; the Agent versions card is not. */
  health?: AgentHealth[];
}

/**
 * plan.md Phase 12.3: the three newest releases in full, the whole release history as a table under
 * them (Phase 2's split-off item), and which version each agent runs against what the server hosts.
 * "Available" needs the newest tag the server has read from GitHub (`ServerBuildInfo.latestRelease`);
 * a release the notes don't know about yet (a console older than it) is named by version alone.
 */
export function WhatsNewView({ build, health }: Props) {
  const running = releaseFor(build?.version);
  const latest = build?.latestRelease ?? null;
  const updateAvailable = !!latest && newer(latest, build?.version?.split('+')[0]);
  const [selected, setSelected] = useState<string | null>(getVersionFromHash());
  const [hosted, setHosted] = useState<Record<string, AgentInstallerStatus | null>>({});

  useEffect(() => {
    function onHash() { setSelected(getVersionFromHash()); }
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  const signedIn = health !== undefined;
  useEffect(() => {
    if (!signedIn) return;
    let cancelled = false;
    void Promise.all((['win-x64', 'linux-x64'] as const).map(p =>
      api.installerStatus(p).then(s => [p, s] as const).catch(() => [p, null] as const)))
      .then(rows => { if (!cancelled) setHosted(Object.fromEntries(rows)); });
    return () => { cancelled = true; };
  }, [signedIn]);

  // Selected from the history table (or a #whats-new/<v> link): shown in full above everything else.
  const top = releases.slice(0, 3);
  const picked = selected && !top.some(r => r.version === selected) ? releases.find(r => r.version === selected) : undefined;

  function chipsFor(v: string) {
    return <>
      {running?.version === v && <Chip tone="ok">Running</Chip>}
      {latest === v && updateAvailable && <Chip tone="warn">Available</Chip>}
    </>;
  }

  const history: Column<Release>[] = [
    { head: 'Version', cell: r => <button type="button" onClick={() => { location.hash = `whats-new/${r.version}`; }}
        className="font-mono font-semibold text-fg underline underline-offset-2 bg-transparent border-0 p-0 cursor-pointer rounded
          focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">v{r.version}</button>, kind: 'm' },
    { head: 'Date', cell: r => fmtDate(r.date), kind: 'm' },
    { head: 'Headline', cell: r => <>{headline(r)} {running?.version === r.version && <Chip tone="ok">Running</Chip>}</>, kind: 'wrap' },
  ];

  type AgentRow = AgentHealth;
  const agents: Column<AgentRow>[] = [
    { head: 'Machine', cell: h => h.machineName, kind: ['k', 'm'] },
    { head: 'Agent', cell: h => h.agentVersion ? normalizeVersion(h.agentVersion) : '—', kind: 'm' },
    {
      head: 'Installer hosted', kind: 'm',
      cell: h => {
        const slot = h.platform ? SLOT[h.platform] : undefined;
        const s = slot ? hosted[slot] : undefined;
        return slot ? (s ? `${slot} ${s.version}` : `${slot} — none`) : '—';
      },
    },
    {
      head: 'State',
      cell: h => {
        const slot = h.platform ? SLOT[h.platform] : undefined;
        const s = slot ? hosted[slot] : undefined;
        if (h.stagedVersion) return <Chip tone="warn" >Update staged · {h.stagedVersion}</Chip>;
        if (!h.agentVersion) return <Chip>Never reported</Chip>;
        if (!s) return <Chip>No installer hosted</Chip>;
        return newer(s.version, normalizeVersion(h.agentVersion)) ? <Chip tone="warn">Behind</Chip> : <Chip tone="ok">Current</Chip>;
      },
    },
  ];

  return (
    <Page>
      <PageHead
        title="What’s new"
        sub={<>
          console on {build ? (build.version === 'dev' ? 'a dev build' : `v${build.version}`) : '…'}
          {updateAvailable && ` · v${latest} available`}
        </>}
        actions={updateAvailable
          ? <>
              <Chip tone="warn">Update available</Chip>
              {releases.some(r => r.version === latest) &&
                <Button onClick={() => { location.hash = `whats-new/${latest}`; }}>Read {latest} notes</Button>}
            </>
          : latest ? <Chip tone="ok">Up to date</Chip> : undefined}
      />

      {/* A dev build is between releases, so the notes below are NOT a description of the running code. */}
      {build && !build.isRelease && build.version !== 'dev' && (
        <Banner tone="watch" title={`This console is running ${build.version}, a development build`}>
          It was made after v{running?.version ?? releases[0].version} was released. Changes made since then are not in these notes.
        </Banner>
      )}

      {picked && (
        <Card title={`v${picked.version} · ${fmtDate(picked.date)}`}
          headerRight={<>{chipsFor(picked.version)}<Button size="sm" variant="quiet" onClick={() => { location.hash = 'whats-new'; }}>Close</Button></>}>
          <div className="help-content prose-notes"><HelpMarkdown>{picked.content}</HelpMarkdown></div>
        </Card>
      )}

      <Card flush>
        {top.map(r => (
          <div key={r.version} id={`rel-${r.version}`}
            className="flex gap-4 p-4 border-b border-row last:border-b-0 flex-wrap md:flex-nowrap">
            <div className="w-[92px] shrink-0 font-mono text-xs text-fg">
              v{r.version}
              <small className="block text-faint text-[10px] mt-1">{fmtDate(r.date)}</small>
              <div className="mt-2 flex flex-col items-start gap-1.5">{chipsFor(r.version)}</div>
            </div>
            <div className="min-w-0 flex-1 help-content prose-notes">
              <HelpMarkdown>{r.content}</HelpMarkdown>
            </div>
          </div>
        ))}
      </Card>

      <div className={`grid gap-4 ${signedIn ? 'lg:grid-cols-2' : ''}`}>
        <Card title="Release history"
          headerRight={<span className="font-mono text-[11px] text-faint">{releases.length} releases · since {fmtDate(releases[releases.length - 1].date)}</span>}
          flush>
          <div className="max-h-[330px] overflow-auto">
            <DataTable caption="Every release, newest first" columns={history} rows={releases} rowKey={r => r.version} />
          </div>
        </Card>

        {signedIn && (
          <Card title="Agent versions" flush>
            <DataTable caption="Agent version per machine" columns={agents} rows={health ?? []} rowKey={h => h.machineId}
              empty={<div className="px-4 py-6 text-[12.5px] text-dim">No machines yet.</div>} />
            <p className="px-4 py-3 text-[12.5px] text-dim border-t border-line">
              Agents update themselves from the installer the server hosts, so the console is always first on a new version.
            </p>
          </Card>
        )}
      </div>
    </Page>
  );
}
