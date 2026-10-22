import { useEffect, useRef, useState } from 'react';
import { api, errorText } from '../../src/api';
import type {
  AgentHealth, Command, Conflict, GameContent, GameIntent, GameSummary, Machine, MachineGameSource, MachineSavePath, MachineScanCandidate, Version,
} from '../../src/types';
import { toast, toastError } from '../../src/toast';
import { missingPacks } from '../../src/dlc';
import { problemGameIds, standing } from '../../src/components/game/gameState';
import { ArtPicker } from '../../src/components/ArtPicker';
import { GameHead } from '../../src/components/game/GameHead';
import { SourceChip } from '../../src/components/game/SourceChip';
import { ReleaseChip } from '../../src/components/game/ReleaseChip';
import { ConflictPanel } from '../../src/components/game/ConflictPanel';
import { GameStats } from '../../src/components/game/GameStats';
import { VersionsCard } from './VersionsCard';
import { SaveFoldersCard } from '../../src/components/game/SaveFoldersCard';
import { EmulatorSettings, type EmulatorTab } from '../../src/components/game/EmulatorSettings';
import { Button } from '../../src/components/ui/Button';
import { Icon } from '../../src/components/ui/Icon';
import { RulesCard } from './RulesCard';
import { ExcludePatternsCard } from '../../src/components/game/ExcludePatternsCard';
import { RemoteCommandsCard } from '../../src/components/game/RemoteCommandsCard';
import { BalancedColumns } from '../../src/components/ui/BalancedColumns';
import { MergeCard } from '../../src/components/game/MergeCard';
import { G } from '../fixtures';
import { useMock } from '../store';
import { PlaythroughVersionsCard } from '../features/playthroughs';
import { PlaythroughsCard } from '../features/playthroughs';

interface Props {
  summary: GameSummary;
  machines: Machine[];
  commands: Command[];
  conflicts: Conflict[];
  health: AgentHealth[];
  onRefresh: () => void;
  /** Where a deep link asked to land on this page; `seq` makes each request act exactly once. */
  intent?: { value: GameIntent; seq: number } | null;
}

/**
 * One game's page (plan.md Phase 10.3–10.5). Rendered straight into the Page canvas — every section
 * below is a direct child of it, which is what staggers their entrance. It owns only what several
 * sections read: the version list and the machines' folders. Each card owns its own actions.
 */
export function GameDetail({ summary, machines, commands, conflicts, health, onRefresh, intent }: Props) {
  const { game, head } = summary;
  // Mockup: the planned cards on their demo game, for the feature on show.
  const feature = useMock(s => s.feature);
  const mockVariant = useMock(s => s.variant);
  // D and E grow the Versions card into playthroughs and their versions.
  const ptVersions = feature === 'pt-console' && game.id === G.elden && (mockVariant === 'D' || mockVariant === 'E');
  const [versions, setVersions] = useState<Version[]>([]);
  const [loadingVersions, setLoadingVersions] = useState(true);
  const [paths, setPaths] = useState<MachineSavePath[]>([]);
  const [candidates, setCandidates] = useState<MachineScanCandidate[]>([]);
  const [sources, setSources] = useState<MachineGameSource[]>([]);
  const [pathsLoaded, setPathsLoaded] = useState(false);
  const [artOpen, setArtOpen] = useState(false);
  const penRef = useRef<HTMLButtonElement>(null);
  // Emulator settings, open on one of its sections; null is the game's own page.
  const [emuTab, setEmuTab] = useState<EmulatorTab | null>(null);
  const [content, setContent] = useState<GameContent | null>(null);

  useEffect(() => {
    setLoadingVersions(true);
    api.versions(game.id).then(setVersions).catch(e => toastError('Could not load versions: ' + errorText(e)))
      .finally(() => setLoadingVersions(false));
    api.getGamePaths(game.id).then(setPaths).catch(() => {}).finally(() => setPathsLoaded(true));
    api.getGamePathCandidates(game.id).then(setCandidates).catch(() => {});
    setSources([]);
    api.getGameSources(game.id).then(setSources).catch(() => {});
    setEmuTab(null);
  }, [game.id]);

  // Only Xenia has emulator settings so far. A game that runs in it either has its profile recorded or a machine found it
  // through Xenia.
  const xenia = !!game.emulatorProfile?.startsWith('xenia:')
    || sources.some(s => s.source.kind === 'emulator' && /xenia/i.test(s.source.detail));

  // For the count on the Emulator settings button: machines missing some of the packs the latest save needs.
  useEffect(() => {
    if (!xenia) { setContent(null); return; }
    let live = true;
    api.getGameContent(game.id).then(c => { if (live) setContent(c); }).catch(() => { if (live) setContent(null); });
    return () => { live = false; };
  }, [game.id, xenia, versions.length]);
  const headContent = content?.headContent ?? [];
  const short = (content?.machines ?? []).filter(m => missingPacks(headContent, m.packages).length > 0).length;

  async function reloadVersions() { setVersions(await api.versions(game.id)); }

  // The list above is read once per game, but versions keep arriving while the page is open — and a
  // conflict names two of them. Found live: a diverged push landed after the page opened, the panel
  // could not find that side, and "Keep both" offered the OLDER save as "the newer". Anything a new
  // upload changes (the head, the stored total, the open conflicts' sides) re-reads it.
  const conflictSides = conflicts.filter(c => c.gameId === game.id).map(c => `${c.versionAId}:${c.versionBId}`).join(',');
  const versionsKey = `${head?.id ?? ''}|${summary.totalStorageBytes}|${conflictSides}`;
  const seenKey = useRef(versionsKey);
  useEffect(() => {
    if (seenKey.current === versionsKey) return;
    seenKey.current = versionsKey;
    api.versions(game.id).then(setVersions).catch(() => { /* the next change or poll tries again */ });
  }, [versionsKey, game.id]);
  async function reloadPaths() {
    setPaths(await api.getGamePaths(game.id));
    // A stored path retires its candidate server-side, so refresh both together or the row keeps
    // offering a guess for a machine that is now mapped.
    setCandidates(await api.getGamePathCandidates(game.id).catch(() => []));
    setSources(await api.getGameSources(game.id).catch(() => []));
  }

  const headId = head?.id ?? null;
  // filter, not find: every open conflict gets its own panel — taking the first one hid the rest.
  const gameConflicts = conflicts.filter(c => c.gameId === game.id);
  const gameCmds = commands.filter(c => c.gameId === game.id).slice(0, 8);

  // Each machine's newest version of this game. A version whose uploader was deleted keeps its name
  // but has no machine to key on — it is history, not a live contributor.
  const latestByMachine: Record<string, Version> = {};
  for (const v of versions) if (v.machineId && !latestByMachine[v.machineId]) latestByMachine[v.machineId] = v;

  // The machines that have this game: they uploaded it, or hold (or scanned) a folder for it.
  const withGame = machines.filter(m =>
    latestByMachine[m.id] || paths.some(p => p.machineId === m.id) || candidates.some(c => c.machineId === m.id));

  async function setLatest(v: Version) {
    try {
      await api.setLatest(game.id, v.id);
      await reloadVersions();
      onRefresh();
      toast(`Set ${v.machineName}'s save as Latest. Every machine that syncs it was told to pull.`, 4000);
    } catch (e) { toastError('Could not set Latest: ' + errorText(e)); }
  }

  // A "Resolve" link names one conflict — or, from an agent's event, only the game, which means its first
  // open conflict AT THAT MOMENT. Pinned per request: deriving "the first" on every render handed the
  // signal on to the next conflict as soon as the linked one was resolved, and that one sprang open too.
  const [resolveTarget, setResolveTarget] = useState<{ seq: number; conflictId: string | null } | null>(null);
  if (intent?.value.kind === 'resolve' && intent.seq !== resolveTarget?.seq) {
    setResolveTarget({ seq: intent.seq, conflictId: intent.value.conflictId ?? gameConflicts[0]?.id ?? null });
  }

  if (xenia && emuTab) {
    return <EmulatorSettings game={game} tab={emuTab} onTab={setEmuTab} onBack={() => setEmuTab(null)} refreshKey={versions.length} />;
  }

  return (
    <>
      <GameHead
        summary={summary}
        standing={standing(summary, problemGameIds(health))}
        versionCount={versions.length}
        machines={withGame.length > 0 ? withGame : machines}
        source={sources.length > 0 || game.release || game.emulatorProfile
          ? <span className="flex flex-wrap items-center gap-1.5">
              {sources.length > 0 && <SourceChip sources={sources} machines={withGame} paths={paths} />}
              <ReleaseChip game={game} sources={sources} />
              {game.emulatorProfile?.startsWith('xenia:') && (
                <span
                  className="inline-flex items-center gap-1 rounded-full border border-line bg-raise px-2 py-0.5 text-[11px] text-dim"
                  title="The Xbox 360 profile this game's saves are under. Every machine syncs it there, and Xenia is set to this profile when the game starts."
                >
                  Xbox 360 profile <span className="font-mono">{game.emulatorProfile.slice(6)}</span>
                </span>
              )}
            </span>
          : undefined}
        allMachines={machines}
        health={health}
        extra={xenia && (
          <Button onClick={() => setEmuTab(short > 0 ? 'dlc' : 'profile')} title="Xbox 360 profile at launch, DLC and hand-overs">
            <Icon name="sliders-horizontal" size={13} />Emulator settings
            {short > 0 && (
              <span className="min-w-[17px] h-[17px] px-1 rounded-full grid place-items-center text-[10px] font-bold tabular-nums bg-watch text-on-accent"
                aria-label={`${short} machine${short === 1 ? '' : 's'} missing DLC`}>{short}</span>
            )}
          </Button>
        )}
        artOpen={artOpen}
        onToggleArt={() => setArtOpen(o => !o)}
        penRef={penRef}
        onRefresh={onRefresh}
      />

      {artOpen && (
        <ArtPicker game={game} onChanged={onRefresh} onClose={() => { setArtOpen(false); penRef.current?.focus(); }} />
      )}

      {gameConflicts.map(c => (
        <ConflictPanel
          key={c.id}
          game={game}
          conflict={c}
          versions={versions}
          health={health}
          headId={headId}
          otherConflicts={gameConflicts.length - 1}
          openSignal={resolveTarget?.conflictId === c.id ? resolveTarget.seq : 0}
          onRefresh={onRefresh}
          onChanged={reloadVersions}
        />
      ))}

      <GameStats summary={summary} versionCount={versions.length} machinesWithGame={withGame} onRefresh={onRefresh} />

      {feature === 'pt-console' && game.id === G.elden && !ptVersions && <PlaythroughsCard />}
      {ptVersions && <PlaythroughVersionsCard />}

      <BalancedColumns items={[
        ...(ptVersions ? [] : [{
          key: 'versions',
          pin: 0 as const,
          node: (
            <VersionsCard
              key={game.id}
              game={game}
              headId={headId}
              versions={versions}
              conflicts={gameConflicts}
              loading={loadingVersions}
              reloadVersions={reloadVersions}
              onSetLatest={setLatest}
              onRefresh={onRefresh}
            />
          ),
        }]),
        {
          key: 'folders',
          node: (
            <SaveFoldersCard
              game={game}
              machines={machines}
              paths={paths}
              candidates={candidates}
              pathsLoaded={pathsLoaded}
              latestByMachine={latestByMachine}
              reloadPaths={reloadPaths}
              intent={intent}
              onRefresh={onRefresh}
              health={health}
              head={head}
              sources={sources}
            />
          ),
        },
        { key: 'rules', node: <RulesCard game={game} machines={machines} health={health} onRefresh={onRefresh} /> },
        { key: 'exclude', node: <ExcludePatternsCard game={game} onRefresh={onRefresh} /> },
        { key: 'merge', node: <MergeCard game={game} onRefresh={onRefresh} /> },
      ]} />

      <RemoteCommandsCard commands={gameCmds} />
    </>
  );
}
