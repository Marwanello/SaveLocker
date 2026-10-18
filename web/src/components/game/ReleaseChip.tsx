import { PlatformIcon } from '../ui/PlatformIcon';
import { consoleIcon } from '../../platformIcon';
import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import type { Game, MachineGameSource } from '../../types';
import { regionLabel } from './gameState';

const languageNames = (() => {
  try { return new Intl.DisplayNames(['en'], { type: 'language' }); } catch { return null; }
})();

/** Redump's codes ("En", "Fr") as names, the code itself when the browser has none. */
function languages(codes: string[]) {
  return codes.map(c => { try { return languageNames?.of(c.toLowerCase()) ?? c; } catch { return c; } }).join(', ');
}

const canHover = () => typeof window !== 'undefined' && !!window.matchMedia?.('(hover: hover)').matches;

interface Props {
  game: Game;
  sources: MachineGameSource[];
}

/**
 * Which release of a console game this is: its region as a chip beside the source ("USA"), or "Details" for a
 * save Redump does not know. Hover shows the details, a click keeps them open; Esc or a click elsewhere
 * closes them. The region is not in the name: two releases of one game share it, and this is what tells them
 * apart.
 */
export function ReleaseChip({ game, sources }: Props) {
  const release = game.release;
  const [open, setOpen] = useState<'hover' | 'pinned' | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (open !== 'pinned') return;
    const onPointer = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(null); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(null); buttonRef.current?.focus(); } };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onPointer); document.removeEventListener('keydown', onKey); };
  }, [open]);

  if (!release) return null;
  const region = regionLabel(release);
  const emulators = [...new Set(sources.filter(s => s.source.kind === 'emulator').map(s => s.source.detail))];
  const rows: [string, ReactNode, boolean?][] = [
    ['Region', release.region ?? 'Unknown'],
    ['Game ID', release.gameIds?.length ? release.gameIds.join(' · ') : 'None in the save', !!release.gameIds?.length],
    ['Console', release.console],
  ];
  if (emulators.length) rows.push(['Emulator', emulators.join(', ')]);
  if (release.languages?.length) rows.push(['Languages', languages(release.languages)]);
  if (game.includeGlobs?.length) rows.push(['Save', game.includeGlobs[0], true]);
  if (sources.length) rows.push(['Machines', sources.map(s => s.machineName).join(', ')]);

  return (
    <div
      className="relative inline-flex"
      ref={rootRef}
      onMouseEnter={() => { if (canHover()) setOpen(o => o ?? 'hover'); }}
      onMouseLeave={() => setOpen(o => (o === 'hover' ? null : o))}
    >
      <button
        ref={buttonRef}
        type="button"
        onClick={() => setOpen(o => (o === 'pinned' ? null : 'pinned'))}
        aria-haspopup="dialog"
        aria-expanded={open !== null}
        aria-label={region ? `Region ${release.region}: show the game's details` : "Show the game's details"}
        className="inline-flex items-center gap-1.5 text-[12.5px] px-2.5 py-1 rounded-full border border-line bg-raise text-fg
          hover:bg-hover cursor-pointer transition-colors duration-150 ease-[var(--ease)]
          focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2"
      >
        <PlatformIcon id={consoleIcon(release.console)} size={1.1} className="text-dim" />
        {region ?? 'Details'}
        <span aria-hidden className="w-1 h-1 rounded-full bg-accent" />
      </button>

      {open && (
        <div
          role="dialog"
          aria-label="Game details"
          className="animate-drop absolute left-0 top-[calc(100%+8px)] w-[340px] max-w-[92vw] bg-panel border border-line rounded-[14px] shadow-2xl z-30 overflow-hidden"
        >
          <header className="px-3.5 py-2.5 border-b border-line text-[13px] font-semibold text-fg [text-wrap:balance]">{game.name}</header>
          <dl className="grid grid-cols-[90px_minmax(0,1fr)] gap-x-3 gap-y-1.5 px-3.5 py-3 m-0 text-[12.5px]">
            {rows.map(([k, v, mono]) => (
              <div key={k} className="contents">
                <dt className="text-faint">{k}</dt>
                <dd className={`m-0 min-w-0 break-words text-fg ${mono ? 'font-mono text-[11.5px]' : ''}`}>{v}</dd>
              </div>
            ))}
          </dl>
          <footer className="px-3.5 py-2.5 border-t border-line text-[11.5px] text-dim flex flex-col gap-1">
            {release.region
              ? <>
                  <span>Another region’s release has another game ID and cannot read these saves, so it is kept as its own game.</span>
                  <span className="text-faint">Title from {release.namedBy ?? 'the save'}, found by the game ID in the save.</span>
                </>
              : <span>{release.namedBy
                  ? `Title from ${release.namedBy}, which lists no region for it.`
                  : 'This save carries no game ID Redump knows, so its name was read from inside the save.'}</span>}
          </footer>
        </div>
      )}
    </div>
  );
}
