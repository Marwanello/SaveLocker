import { useEffect, useState } from 'react';
import { api } from '../../api';
import type { GameSummary, Machine, Command, Conflict, Version, GameIntent } from '../../types';
import { GameHeaderCard } from './GameHeaderCard';
import { ConflictCards } from './ConflictCards';
import { InitialSyncCard } from './InitialSyncCard';
import { ExcludePatternsCard } from './ExcludePatternsCard';
import { MachinesCard } from './MachinesCard';
import { SavePathsCard } from './SavePathsCard';
import { RemoteCommandsCard } from './RemoteCommandsCard';
import { VersionsCard } from './VersionsCard';

interface Props {
  summary: GameSummary;
  machines: Machine[];
  commands: Command[];
  conflicts: Conflict[];
  onRefresh: () => void;
  /** Where a deep link asked to land on this page; `seq` makes each request act exactly once. */
  intent?: { value: GameIntent; seq: number } | null;
}

/** One game's page. It owns only what several cards read — the version list and the conflict-policy
 *  draft — and lays the cards out; each card owns its own state and actions. */
export function GameDetail({ summary, machines, commands, conflicts, onRefresh, intent }: Props) {
  const [versions, setVersions] = useState<Version[]>([]);
  const [loadingVersions, setLoadingVersions] = useState(true);
  const [policyDraft, setPolicyDraft] = useState<string>(summary.game.conflictPolicy ?? 'Manual');
  const [preferredMachineDraft, setPreferredMachineDraft] = useState<string | null>(summary.game.preferredMachineId ?? null);
  const [policyForGameId, setPolicyForGameId] = useState(summary.game.id);

  const { game, head } = summary;

  // Reset the policy draft when switching games (not on every poll).
  if (policyForGameId !== game.id) {
    setPolicyForGameId(game.id);
    setPolicyDraft(game.conflictPolicy ?? 'Manual');
    setPreferredMachineDraft(game.preferredMachineId ?? null);
  }

  const headId = head?.id ?? null;
  // filter, not find. Taking the first match silently hid every other conflict on the game, and the
  // server used to return them oldest-first — so the console reliably showed the LEAST useful one
  // while the save actually being played sat in a conflict the UI never rendered.
  const gameConflicts = conflicts.filter(c => c.gameId === game.id);
  const gameCmds = commands.filter(c => c.gameId === game.id).slice(0, 8);

  // Latest version per machine (for Machines table "Last upload" column)
  const latestByMachine: Record<string, Version> = {};
  for (const v of versions) {
    // A version whose uploader has been deleted keeps its name but has no machine to key on —
    // it is history, not a live contributor.
    if (!v.machineId) continue;
    if (!latestByMachine[v.machineId]) latestByMachine[v.machineId] = v;
  }

  // Initial-sync wizard: show when multiple machines have versions
  const contributors = Object.values(latestByMachine);

  useEffect(() => {
    setLoadingVersions(true);
    api.versions(game.id).then(vs => { setVersions(vs); setLoadingVersions(false); });
  }, [game.id]);

  async function reloadVersions() {
    setVersions(await api.versions(game.id));
  }

  async function handleSetLatest(versionId: string) {
    // Says what actually happens now: a pull is queued for every machine that syncs this game, and
    // it is unforced, so a machine holding unsynced local work reports blocked rather than losing it.
    if (!confirm(
      'Set this version as Latest?\n\n' +
      'A pull is queued for every machine that syncs this game. Any machine with local changes it ' +
      'has not pushed yet will report the pull as blocked instead of overwriting them.\n\n' +
      'If this version is one of the options in an open conflict, that conflict is marked resolved ' +
      'in its favour.'
    )) return;
    try {
      await api.setLatest(game.id, versionId);
      const vs = await api.versions(game.id);
      setVersions(vs);
      onRefresh();
    } catch (e) { alert('Set as Latest failed: ' + (e as Error).message); }
  }

  function handleSetNewestWins() {
    setPolicyDraft('NewestWins');
    void api.setConflictPolicy(game.id, 'NewestWins').then(onRefresh);
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <GameHeaderCard
        summary={summary}
        machines={machines}
        versionCount={versions.length}
        policyDraft={policyDraft}
        setPolicyDraft={setPolicyDraft}
        preferredMachineDraft={preferredMachineDraft}
        setPreferredMachineDraft={setPreferredMachineDraft}
        onRefresh={onRefresh}
      />

      <ConflictCards
        game={game}
        headId={headId}
        conflicts={gameConflicts}
        machines={machines}
        versions={versions}
        onSetNewestWins={handleSetNewestWins}
        onRefresh={onRefresh}
      />

      {contributors.length > 1 && (
        <InitialSyncCard contributors={contributors} headId={headId} onSetLatest={id => void handleSetLatest(id)} />
      )}

      <ExcludePatternsCard game={game} onRefresh={onRefresh} />

      <MachinesCard game={game} machines={machines} latestByMachine={latestByMachine} onRefresh={onRefresh} />

      <SavePathsCard game={game} machines={machines} intent={intent} onRefresh={onRefresh} />

      {gameCmds.length > 0 && <RemoteCommandsCard commands={gameCmds} />}

      <VersionsCard
        game={game}
        headId={headId}
        versions={versions}
        loading={loadingVersions}
        reloadVersions={reloadVersions}
        onSetLatest={id => void handleSetLatest(id)}
        onRefresh={onRefresh}
      />
    </div>
  );
}
