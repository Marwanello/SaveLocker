import { useEffect, useRef, useState } from 'react';
import { api, errorText } from '../../api';
import type { Conflict, Game, Version, VersionStats } from '../../types';
import { age, asUtc, fmtSize, plural, shortId, when } from '../../format';
import { toast, toastError } from '../../toast';
import { Banner } from '../ui/Banner';
import { Button } from '../ui/Button';
import { Card } from '../ui/Card';
import { InlineConfirm } from '../ui/InlineConfirm';

interface Props {
  game: Game;
  conflict: Conflict;
  versions: Version[];
  headId: string | null;
  /** How many other conflicts this game has open — named in the consequence, since they remain. */
  otherConflicts: number;
  /** A deep link's "Resolve": a new non-zero value opens this panel and brings it into view. */
  openSignal: number;
  onRefresh: () => void;
  /** The version list changed (a resolve can re-point Latest and protect snapshots). */
  onChanged: () => Promise<void>;
}

const ms = (t: string) => new Date(asUtc(t)).getTime();

/**
 * plan.md Phase 10.4: an open conflict as a Banner that expands, in place, into the resolve panel —
 * the two sides as the conflict card has always shown them (machine, time, size, files), Keep this
 * save / Keep both, and the "set a policy" hint. One per open conflict; neither side is pre-selected.
 */
export function ConflictPanel({ game, conflict: c, versions, headId, otherConflicts, openSignal, onRefresh, onChanged }: Props) {
  const [open, setOpen] = useState(false);
  const [stats, setStats] = useState<Record<string, VersionStats>>({});
  const root = useRef<HTMLDivElement>(null);
  const requested = useRef<Set<string>>(new Set());

  useEffect(() => {
    if (openSignal <= 0) return;
    setOpen(true);
    root.current?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [openSignal]);

  // File count / newest change for each side, read from its archive — only once the panel is open,
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
  const newer = a && b ? (ms(a.createdAt) >= ms(b.createdAt) ? a : b) : null;

  const title = a && b
    ? `${a.machineName} and ${b.machineName} both wrote` + (base ? ` since ${shortId(base)}` : ' different saves')
    : 'Two machines wrote different saves';
  const detail = [
    `Open ${age(c.createdAt)}.`,
    ...(a && b ? [`${a.machineName} holds ${fmtSize(a.size)} from ${when(a.createdAt)}; ${b.machineName} holds ${fmtSize(b.size)} from ${when(b.createdAt)}.`] : []),
    ...(c.escalated ? ['Unresolved for more than six hours — sync is paused for this game until you choose.'] : []),
    ...(c.count > 1 ? [`${c.count} divergent saves were folded into this one; the newest is offered, the rest stay under Versions.`] : []),
  ].join(' ');

  function consequence(v: Version | undefined, keepBoth: boolean) {
    const newerThan = v ? versions.filter(x => ms(x.createdAt) > ms(v.createdAt)).length : 0;
    return [
      v ? `${v.machineName}'s save (${fmtSize(v.size)}, ${when(v.createdAt)}) becomes Latest and both machines in this conflict pull it.`
        : 'This save becomes Latest and both machines in this conflict pull it.',
      newerThan > 0 ? `${plural(newerThan, 'newer save')} stop${newerThan === 1 ? 's' : ''} being what machines pull — nothing is deleted.` : '',
      keepBoth ? 'Both snapshots are protected from pruning until you unprotect them under Versions.' : '',
      otherConflicts > 0 ? `${plural(otherConflicts, 'other conflict')} on this game remain${otherConflicts === 1 ? 's' : ''}.` : '',
    ].filter(Boolean).join(' ');
  }

  async function resolve(versionId: string, keepBoth: boolean) {
    const v = versions.find(x => x.id === versionId);
    try {
      await api.resolveConflict(c.id, versionId, keepBoth);
      toast(`Kept ${v ? `the ${v.machineName} save` : 'that save'}${keepBoth ? ' and the other as a backup' : ''}. Both machines will pull it.`, 4000);
      setOpen(false);
      onRefresh();
      await onChanged();
    } catch (e) { toastError('Could not resolve the conflict: ' + errorText(e)); }
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
        title="Resolve — which save is your real progress?"
        headerRight={<Button size="sm" onClick={() => setOpen(false)}>Cancel</Button>}
        className="!border-accent-line"
      >
        <p className="text-[13px] text-dim mb-3.5">
          The one you keep becomes Latest and every machine in this conflict pulls it. The other stays in the
          version list — nothing is deleted here.
        </p>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {sides.map(({ id, v }) => {
            const st = stats[id];
            const who = v?.machineName ?? shortId(id);
            return (
              <div key={id} className="border border-line rounded-[14px] p-[15px] bg-tile flex flex-col">
                <h5 className="text-[10px] tracking-[0.12em] uppercase text-faint font-normal">
                  {who} · {shortId(id)}{id === headId ? ' · current Latest' : ''}
                </h5>
                <div className="text-[17px] font-bold tracking-[-0.03em] mt-2 tabular-nums">
                  {v ? `${fmtSize(v.size)} · ${when(v.createdAt)}` : '—'}
                </div>
                <div className="text-xs text-dim mt-1 min-h-[18px] tabular-nums">
                  {st ? `${plural(st.fileCount, 'file')}${st.newestFileWriteUtc ? ` · newest change ${when(st.newestFileWriteUtc)}` : ''}` : ''}
                </div>
                <div className="mt-3.5">
                  <InlineConfirm
                    label={`Keep the ${who} save`}
                    size="default"
                    tone="primary"
                    className="w-full"
                    consequence={consequence(v, false)}
                    confirmLabel={`Keep the ${who} save`}
                    onConfirm={() => resolve(id, false)}
                  />
                </div>
              </div>
            );
          })}
        </div>
        {newer && (
          <div className="flex gap-2.5 mt-3.5 items-center flex-wrap">
            <InlineConfirm
              label={`Keep both — ${newer.machineName}'s as Latest`}
              size="default"
              tone="default"
              consequence={consequence(newer, true)}
              confirmLabel="Keep both saves"
              onConfirm={() => resolve(newer.id, true)}
            />
            <span className="text-xs text-dim">The newer save becomes Latest; the other is kept as a protected backup.</span>
          </div>
        )}
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
