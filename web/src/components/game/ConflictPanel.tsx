import { useEffect, useId, useRef, useState } from 'react';
import { api, errorText } from '../../api';
import type { AgentHealth, Conflict, Game, Version, VersionStats } from '../../types';
import { age, ago, fmtSize, plural, shortId, toMs, when } from '../../format';
import { osForMachine, osLine } from '../../machineOs';
import { toast, toastError } from '../../toast';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { Chip } from '../ui/Chip';
import { OsBadge } from '../ui/OsLogo';

interface Props {
  game: Game;
  conflict: Conflict;
  versions: Version[];
  /** Where each side's OS logo comes from. */
  health: AgentHealth[];
  headId: string | null;
  /** How many other conflicts this game has open — named in the consequence, since they remain. */
  otherConflicts: number;
  /** A deep link's "Resolve": a new non-zero value opens this panel and brings it into view. */
  openSignal: number;
  onRefresh: () => void;
  /** The version list changed (a resolve can re-point Latest and protect snapshots). */
  onChanged: () => Promise<void>;
}

const CHOSEN_TILE = `border-safe-line bg-[linear-gradient(180deg,color-mix(in_oklab,var(--color-safe)_10%,transparent),color-mix(in_oklab,var(--color-safe)_3%,transparent)_60%)]`;

/**
 * plan.md Phase 10.4: an open conflict as a Banner that expands, in place, into the resolve panel.
 * Open, it is the agent's conflict card (agent-ui ConflictCard) seen from the console: the two sides
 * as tiles, each led by its machine's OS logo where the agent shows Local/Cloud, since here both
 * sides are machines. Pick a side, optionally keep the other as a protected backup, then Resolve —
 * the pick is the first step and the sentence under the tiles says what the second will do. Neither
 * side is pre-selected.
 */
export function ConflictPanel({ game, conflict: c, versions, health, headId, otherConflicts, openSignal, onRefresh, onChanged }: Props) {
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [keepBoth, setKeepBoth] = useState(false);
  const [busy, setBusy] = useState(false);
  const [stats, setStats] = useState<Record<string, VersionStats>>({});
  const root = useRef<HTMLDivElement>(null);
  const requested = useRef<Set<string>>(new Set());
  const uid = useId();

  useEffect(() => {
    if (openSignal <= 0) return;
    setOpen(true);
    root.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [openSignal]);

  // File count and newest change for each side, read from its archive — only once the panel is open,
  // since that is the only place they are shown. An archive never changes once uploaded.
  useEffect(() => {
    if (!open) return;
    for (const vid of [c.versionAId, c.versionBId]) {
      if (requested.current.has(vid)) continue;
      requested.current.add(vid);
      api.versionStats(game.id, vid)
        .then(s => setStats(prev => ({ ...prev, [vid]: s })))
        .catch(() => { requested.current.delete(vid); });
    }
  }, [open, c.versionAId, c.versionBId, game.id]);

  const a = versions.find(v => v.id === c.versionAId);
  const b = versions.find(v => v.id === c.versionBId);
  const sides = [c.versionAId, c.versionBId].map(id => ({ id, v: versions.find(v => v.id === id) }));
  const base = a && b && a.parentVersionId && a.parentVersionId === b.parentVersionId ? a.parentVersionId : null;
  // Only with both sides in hand: "the newer" of one known side and one not yet loaded is a guess, and
  // was once the wrong one (see GameDetail's version re-read).
  const newer = a && b ? (toMs(a.createdAt) >= toMs(b.createdAt) ? a : b) : null;
  const whose = (id: string) => versions.find(v => v.id === id)?.machineName ?? shortId(id);

  const title = a && b
    ? `${a.machineName} and ${b.machineName} both wrote` + (base ? ` since ${shortId(base)}` : ' different saves')
    : 'Two machines wrote different saves';
  const detail = [
    `Open ${age(c.createdAt)}.`,
    ...(a && b ? [`${a.machineName} holds ${fmtSize(a.size)} from ${when(a.createdAt)}; ${b.machineName} holds ${fmtSize(b.size)} from ${when(b.createdAt)}.`] : []),
    ...(c.escalated ? ['Unresolved for more than six hours — sync is paused for this game until you choose.'] : []),
    ...(c.count > 1 ? [`${c.count} divergent saves were folded into this one; the newest is offered, the rest stay under Versions.`] : []),
  ].join(' ');

  function consequence(v: Version | undefined, both: boolean) {
    const newerThan = v ? versions.filter(x => toMs(x.createdAt) > toMs(v.createdAt)).length : 0;
    return [
      v ? `${v.machineName}'s save (${fmtSize(v.size)}, ${when(v.createdAt)}) becomes Latest and both machines in this conflict pull it.`
        : 'This save becomes Latest and both machines in this conflict pull it.',
      newerThan > 0 ? `${plural(newerThan, 'newer save')} stop${newerThan === 1 ? 's' : ''} being what machines pull — nothing is deleted.` : '',
      both ? 'Both snapshots are protected from pruning until you unprotect them under Versions.' : 'The other stays under Versions.',
      otherConflicts > 0 ? `${plural(otherConflicts, 'other conflict')} on this game remain${otherConflicts === 1 ? 's' : ''}.` : '',
    ].filter(Boolean).join(' ');
  }

  function collapse() {
    setOpen(false);
    setSelected(null);
    setKeepBoth(false);
  }

  async function resolve() {
    if (!selected) return;
    const who = whose(selected);
    setBusy(true);
    try {
      await api.resolveConflict(c.id, selected, keepBoth);
      toast(`Kept the ${who} save${keepBoth ? ' and the other as a backup' : ''}. Both machines will pull it.`, 4000);
      collapse();
      onRefresh();
      await onChanged();
    } catch (e) {
      toastError('Could not resolve the conflict: ' + errorText(e));
    } finally {
      setBusy(false);
    }
  }

  async function adoptNewestWins() {
    try {
      await api.setConflictPolicy(game.id, 'NewestWins');
      toast(`${game.name} now resolves future conflicts by keeping the newest save.`, 4000);
      onRefresh();
    } catch (e) { toastError('Could not change the policy: ' + errorText(e)); }
  }

  if (!open) {
    return (
      <div ref={root}>
        <Banner
          title={title}
          action={<>
            <a href="#help/conflicts" className="text-xs text-fg underline underline-offset-2 rounded
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">
              Why did this happen?
            </a>
            <Button variant="primary" onClick={() => setOpen(true)}>Resolve</Button>
          </>}
        >
          {detail}
        </Banner>
      </div>
    );
  }

  return (
    <div ref={root}>
      <Card
        title={<span className="inline-flex items-center gap-2.5">Which save is your real progress? <Chip tone="warn">Conflict</Chip></span>}
        headerRight={<Button size="sm" onClick={collapse}>Cancel</Button>}
        className="!border-accent-line"
      >
        <p className="text-[12.5px] leading-relaxed text-dim max-w-[64ch]">
          {a && b ? `${a.machineName} and ${b.machineName}` : 'Two machines'} both changed
          {base ? ` since version ${shortId(base)}` : ' since the last sync'}. Pick which one to keep — the other is
          never deleted, just set aside.
        </p>
        {c.escalated && (
          <p className="text-[11.5px] font-semibold text-accent-ink mt-1.5">
            Overdue — unresolved for more than six hours, so sync is paused for this game until you choose.
          </p>
        )}
        {c.count > 1 && (
          <p className="text-[11.5px] text-dim mt-1.5">
            {c.count} divergent saves were folded into this conflict — the newest is offered below.
          </p>
        )}

        <div className="grid grid-cols-1 md:grid-cols-2 gap-3 mt-4">
          {sides.map(({ id, v }) => {
            const who = v?.machineName ?? shortId(id);
            const os = osForMachine(health, v?.machineId);
            const st = stats[id];
            const chosen = selected === id;
            return (
              <button
                key={id}
                type="button"
                aria-pressed={chosen}
                aria-label={`Keep the ${who} save`}
                aria-describedby={`${uid}-${id}`}
                disabled={busy}
                onClick={() => setSelected(id)}
                className={`flex flex-col items-stretch gap-2.5 text-left rounded-[12px] border px-[15px] py-3.5 cursor-pointer
                  transition-[border-color,background-color] duration-150 ease-[var(--ease)] disabled:cursor-default
                  ${chosen ? CHOSEN_TILE : 'border-line bg-tile hover:border-dim'}
                  focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2`}
              >
                <span className="flex items-center gap-2.5 min-w-0">
                  <OsBadge os={os} tone={chosen ? 'safe' : 'default'} />
                  <span className="text-[13.5px] font-bold text-fg truncate">{who}</span>
                  {id === headId && <Chip className="ml-auto">Latest</Chip>}
                </span>

                <span id={`${uid}-${id}`} className="flex flex-col gap-1">
                  <span className="flex items-baseline gap-2 text-[17px] font-bold tracking-[-0.02em] tabular-nums text-fg"
                    title={v ? when(v.createdAt) : undefined}>
                    {v ? ago(v.createdAt) : '—'}
                    {newer?.id === id && (
                      <span className="text-[9.5px] font-bold tracking-[0.05em] uppercase text-safe-ink bg-safe-soft rounded-full px-1.5 py-px">
                        newer
                      </span>
                    )}
                  </span>
                  <span className="text-[11.5px] text-dim tabular-nums">
                    {[st ? plural(st.fileCount, 'file') : null, v ? fmtSize(v.size) : null, v ? `uploaded ${when(v.createdAt)}` : null]
                      .filter(Boolean).join(' · ')}
                  </span>
                  {/* The upload time says when a machine SENT it; this says when the game last WROTE
                      to it — the better guess at which side holds more play (conflict Tier 1). */}
                  {st?.newestFileWriteUtc && (
                    <span className="text-[11.5px] text-dim tabular-nums"
                      title="When the newest file in this save was last written, by its machine's clock">
                      newest change {when(st.newestFileWriteUtc)}
                    </span>
                  )}
                  <span className="text-[11px] text-dim leading-snug">
                    {osLine(os) ?? 'Operating system not reported yet'} · version {shortId(id)}
                  </span>
                </span>

                <span aria-hidden="true"
                  className={`mt-0.5 self-start rounded-md border px-3 py-[5px] text-[11.5px] font-semibold
                    transition-colors duration-150 ease-[var(--ease)]
                    ${chosen ? 'bg-accent border-accent text-on-accent' : 'bg-raise border-line text-dim'}`}>
                  {chosen ? 'Keeping this' : 'Keep this'}
                </span>
              </button>
            );
          })}
        </div>

        <p aria-live="polite" className="text-xs leading-relaxed text-dim mt-3 min-h-[18px]">
          {selected ? consequence(versions.find(v => v.id === selected), keepBoth) : ''}
        </p>

        <div className="mt-3 pt-3.5 border-t border-dashed border-line flex items-center justify-between gap-3 flex-wrap">
          <label className="flex items-center gap-2 text-xs text-dim cursor-pointer">
            <input
              type="checkbox"
              checked={keepBoth}
              disabled={busy}
              onChange={e => setKeepBoth(e.target.checked)}
              className="w-[13px] h-[13px] accent-[var(--color-watch-ink)] cursor-pointer
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
            />
            Also keep the other one as a protected backup
          </label>
          <Button variant="primary" disabled={!selected || busy} onClick={() => void resolve()}>
            {selected ? `Resolve with ${whose(selected)}` : 'Resolve'}
          </Button>
        </div>

        {(game.conflictPolicy ?? 'Manual') === 'Manual' && (
          <p className="text-xs text-dim mt-3.5">
            Playing solo?{' '}
            <button type="button" onClick={() => void adoptNewestWins()}
              className="text-fg underline underline-offset-2 bg-transparent border-0 p-0 cursor-pointer text-xs
                focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">
              Use Newest wins
            </button>{' '}
            and future conflicts on this game resolve themselves.
          </p>
        )}
      </Card>
    </div>
  );
}
