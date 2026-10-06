import { useEffect, useRef, useState } from 'react';
import type { GameSource, Machine, MachineGameSource, MachineSavePath } from '../../types';
import { Icon } from '../ui/Icon';
import type { IconName } from '../ui/Icon';

/** The first level as people read it. A kind this console does not know shows as itself. */
const KINDS: Record<string, { label: string; icon: IconName }> = {
  emulator: { label: 'Emulator', icon: 'gamepad-2' },
  steam: { label: 'Steam', icon: 'library' },
  heroic: { label: 'Heroic', icon: 'shield' },
  playnite: { label: 'Playnite', icon: 'square-play' },
  'save-folder': { label: 'Save-folder scan', icon: 'folder-search' },
  manual: { label: 'Added by hand', icon: 'pencil' },
};

const kindOf = (s: GameSource) => KINDS[s.kind] ?? { label: s.kind, icon: 'circle-dashed' as IconName };
const sameSource = (a: GameSource, b: GameSource) => a.kind === b.kind && a.detail === b.detail;

function SourceText({ source }: { source: GameSource }) {
  const kind = kindOf(source);
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap">
      <Icon name={kind.icon} size={14} strokeWidth={1.8} className="text-dim shrink-0" />
      <span className="text-dim">{kind.label}</span>
      <span className="text-faint" aria-hidden>›</span>
      <span className="font-semibold text-fg">{source.detail}</span>
    </span>
  );
}

function Tags({ tags }: { tags: string[] | null | undefined }) {
  if (!tags?.length) return null;
  return (
    <span className="flex gap-1.5 flex-wrap mt-1.5">
      {tags.map(t => <span key={t} className="text-[11px] text-dim border border-line rounded-md px-1.5 py-px">{t}</span>)}
    </span>
  );
}

interface Props {
  sources: MachineGameSource[];
  /** The machines that have this game, so one without a recorded source still gets a row. */
  machines: Machine[];
  paths: MachineSavePath[];
}

/**
 * How each machine found the game — per machine, since two rarely find it the same way. One chip under
 * the name: the source itself when every machine agrees, "N sources" when they differ. It opens the
 * list, one row per machine.
 */
export function SourceChip({ sources, machines, paths }: Props) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(false); buttonRef.current?.focus(); } };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey); };
  }, [open]);

  if (sources.length === 0) return null;

  const distinct = sources.filter((s, i) => sources.findIndex(o => sameSource(o.source, s.source)) === i);
  const rows = [
    ...sources.map(s => ({ id: s.machineId, name: s.machineName, source: s.source as GameSource | null })),
    ...machines.filter(m => !sources.some(s => s.machineId === m.id)).map(m => ({ id: m.id, name: m.name, source: null })),
  ];

  return (
    <div className="relative inline-flex" ref={rootRef}>
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen(o => !o)}
        aria-haspopup="dialog"
        aria-expanded={open}
        title="How each machine found this game"
        className="inline-flex items-center gap-1.5 text-[12.5px] pl-2.5 pr-3 py-1 rounded-full border border-line bg-raise
          hover:bg-hover cursor-pointer transition-colors duration-150 ease-[var(--ease)]
          focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
      >
        {distinct.length === 1
          ? <SourceText source={distinct[0].source} />
          : <span className="inline-flex items-center gap-1.5">
              <Icon name={kindOf(distinct[0].source).icon} size={14} strokeWidth={1.8} className="text-dim" />
              <span className="font-semibold text-fg">{distinct.length} sources</span>
            </span>}
        <Icon name="chevron-down" size={13} className="text-faint" />
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="How each machine found this game"
          className="animate-drop absolute left-0 top-[calc(100%+8px)] w-[380px] max-w-[92vw] bg-panel border border-line rounded-[14px] shadow-2xl z-30 overflow-hidden"
        >
          <header className="px-3.5 py-2.5 border-b border-line text-[11px] tracking-[.12em] uppercase text-faint">Found on each machine</header>
          {rows.map(r => (
            <div key={r.id} className="grid grid-cols-[110px_1fr] gap-3 px-3.5 py-2.5 border-b border-line last:border-b-0 text-[12.5px]">
              <span className="font-semibold text-fg truncate" title={r.name}>{r.name}</span>
              <span className="min-w-0">
                {r.source
                  ? <><SourceText source={r.source} /><Tags tags={r.source.tags} /></>
                  : <span className="italic text-faint">
                      {paths.some(p => p.machineId === r.id && p.pathKey === 'main') ? 'Not recorded yet' : 'Not set up on this machine'}
                    </span>}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
