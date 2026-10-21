import { useEffect, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { api, errorText } from '../../api';
import type { GameSummary, TitleCandidate } from '../../types';
import { ago, fmtSize } from '../../format';
import { emulatorIcon } from '../../platformIcon';
import { toast, toastError } from '../../toast';
import { Button } from '../ui/Button';
import { Dot } from '../ui/Dot';
import { Icon } from '../ui/Icon';
import { InlineConfirm } from '../ui/InlineConfirm';
import { PlatformIcon } from '../ui/PlatformIcon';
import { Select } from '../ui/Select';
import { standing } from './gameState';
import { emulatorOf, treeLabel } from './titles';

/** A title's emulators as small icons, a two-letter badge where an emulator has no icon. */
export function EmulatorBadges({ trees }: { trees: GameSummary[] }) {
  const names = [...new Set(trees.map(t => emulatorOf(t) ?? 'Game'))];
  return (
    <span className="inline-flex items-center gap-1 shrink-0" title={names.join(' · ')}>
      {names.map(n => emulatorIcon(n)
        ? <PlatformIcon key={n} id={emulatorIcon(n)} size={1.05} className="text-dim" />
        : <span key={n} className="inline-grid place-items-center h-[15px] min-w-[17px] px-[3px] rounded-[4px] bg-tile border border-line font-mono text-[8.5px] text-dim">
            {n.replace(/[^A-Za-z0-9]/g, '').slice(0, 2)}
          </span>)}
    </span>
  );
}

interface TabsProps {
  trees: GameSummary[];
  current: string;
  problems: Set<string>;
  onPick: (gameId: string) => void;
  onRefresh: () => void;
}

/**
 * One tab per emulator tree of a title (tasks/multi-emulator-games Phase 3, mockup A). Each tab shows the emulator, its
 * latest save and its own status dot, so a conflict in a closed tab still shows. Arrow keys move between tabs. At the
 * end: Group with… (Phase 4) and Split out for the open tree.
 */
export function EmulatorTabs({ trees, current, problems, onPick, onRefresh }: TabsProps) {
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const index = Math.max(0, trees.findIndex(t => t.game.id === current));
  const open = trees[index];

  function onKey(e: KeyboardEvent<HTMLButtonElement>) {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft' && e.key !== 'Home' && e.key !== 'End') return;
    e.preventDefault();
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? trees.length - 1
      : (index + (e.key === 'ArrowRight' ? 1 : -1) + trees.length) % trees.length;
    onPick(trees[next].game.id);
    refs.current[next]?.focus();
  }

  async function split() {
    try {
      await api.splitTitle(open.game.id);
      toast(`${open.game.name} is its own entry again. No versions moved.`, 4500);
      onRefresh();
    } catch (e) { toastError('Could not split it out: ' + errorText(e)); }
  }

  return (
    <div className="flex items-end gap-1.5 border-b border-line flex-wrap" role="tablist" aria-label="Emulators">
      {trees.map((t, i) => {
        const on = i === index;
        const st = standing(t, problems);
        const name = emulatorOf(t);
        return (
          <button
            key={t.game.id}
            ref={el => { refs.current[i] = el; }}
            type="button"
            role="tab"
            aria-selected={on}
            tabIndex={on ? 0 : -1}
            onClick={() => onPick(t.game.id)}
            onKeyDown={onKey}
            title={t.game.name}
            className={`flex items-center gap-2.5 -mb-px px-3.5 pt-2 pb-2.5 rounded-t-[10px] border-b-2 text-left cursor-pointer
              transition-colors duration-150 ease-[var(--ease)]
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-[-2px]
              ${on ? 'border-accent text-fg' : 'border-transparent text-dim hover:text-fg hover:bg-raise'}`}
          >
            {emulatorIcon(name) ? <PlatformIcon id={emulatorIcon(name)} size={1.35} /> : <EmulatorBadges trees={[t]} />}
            <span className="flex flex-col min-w-0">
              <span className="text-[13px] font-semibold tracking-[-0.01em]">{treeLabel(t, trees)}</span>
              <span className="text-[10.5px] text-faint tabular-nums">
                {t.head ? `latest ${ago(t.head.createdAt)}` : 'no saves yet'} · {fmtSize(t.totalStorageBytes)}
              </span>
            </span>
            <Dot tone={st.tone} label={st.label} />
          </button>
        );
      })}
      <span className="ml-auto flex items-center gap-1.5 pb-1.5">
        <GroupWith gameId={open.game.id} onRefresh={onRefresh} />
        <InlineConfirm
          label={`Split ${emulatorOf(open) ?? 'this'} out`}
          tone="default"
          triggerVariant="quiet"
          size="sm"
          consequence={<>{open.game.name} becomes its own entry again, and stays one. Its versions, folders and rules don’t change.</>}
          confirmLabel="Split out"
          onConfirm={split}
        />
      </span>
    </div>
  );
}

/** "＋ Group with…": a same-console game saved with another emulator, joined to this one's entry. No version moves. */
export function GroupWith({ gameId, onRefresh, label = 'Group with…' }: { gameId: string; onRefresh: () => void; label?: string }) {
  const [open, setOpen] = useState(false);
  const [candidates, setCandidates] = useState<TitleCandidate[] | null>(null);

  useEffect(() => {
    if (!open) return;
    let live = true;
    api.titleCandidates(gameId).then(c => { if (live) setCandidates(c); })
      .catch(e => toastError('Could not list games: ' + errorText(e)));
    return () => { live = false; };
  }, [open, gameId]);

  async function join(id: string) {
    const picked = candidates?.find(c => c.gameId === id);
    try {
      await api.joinTitle(id, gameId);
      toast(`${picked?.name ?? 'The game'} is now one of this game’s emulators. Each keeps its own versions.`, 4500);
      setOpen(false);
      onRefresh();
    } catch (e) { toastError('Could not group them: ' + errorText(e)); }
  }

  if (!open) return <Button size="sm" variant="quiet" onClick={() => setOpen(true)}><Icon name="plus" size={12} strokeWidth={2.4} />{label}</Button>;
  // Same-name games first: the likely pick.
  const sorted = [...(candidates ?? [])].sort((a, b) => Number(b.sameName) - Number(a.sameName));
  return (
    <span className="inline-flex items-center gap-1.5">
      <Select
        label="Group with"
        placeholder={candidates === null ? 'Loading…' : sorted.length ? 'Pick a game' : 'No other game to group with'}
        searchable
        align="end"
        disabled={!sorted.length}
        value={null}
        options={sorted.map(c => ({ value: c.gameId, label: c.name, sub: [c.emulator, c.region].filter(Boolean).join(' · '),
          icon: <PlatformIcon id={emulatorIcon(c.emulator)} size={1} /> }))}
        onChange={id => void join(id)}
      />
      <Button size="sm" variant="quiet" onClick={() => setOpen(false)}>Cancel</Button>
    </span>
  );
}
