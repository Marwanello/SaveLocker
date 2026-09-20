import { useEffect, useRef, useState } from 'react';
import { Button } from './ui/Button';

interface Props {
  onClose: () => void;
  onSubmit: (name: string, suggestedSaveDir: string | null) => Promise<void>;
}

const INPUT = 'w-full px-3 py-2 bg-panel text-fg border border-line rounded-md text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent';

export function AddGameDialog({ onClose, onSubmit }: Props) {
  const [name, setName] = useState('');
  const [dir, setDir] = useState('');
  const [nameError, setNameError] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const nameRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onClose]);

  async function submit() {
    if (busy) return;
    if (!name.trim()) { setNameError(true); nameRef.current?.focus(); return; }
    setBusy(true);
    setError('');
    try {
      await onSubmit(name.trim(), dir.trim() || null);
    } catch (e) {
      setError("Couldn't add the game. " + (e as Error).message);
      setBusy(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/45"
      onMouseDown={e => { if (e.target === e.currentTarget && !busy) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="add-game-title"
        className="w-full max-w-[460px] bg-panel border border-line rounded-xl p-5 flex flex-col gap-4 animate-rise"
      >
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 id="add-game-title" className="text-lg font-bold text-fg">Add game</h2>
            <p className="text-[13px] text-dim mt-1">
              Defines the game on the server. Each machine maps its own local save folder afterwards.
            </p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            disabled={busy}
            className="text-dim hover:text-fg text-lg leading-none px-1 cursor-pointer bg-transparent border-0"
          >
            ×
          </button>
        </div>

        <div>
          <label htmlFor="add-game-name" className="block text-[13px] font-semibold text-fg mb-1.5">Game name</label>
          <input
            id="add-game-name"
            ref={nameRef}
            autoFocus
            autoComplete="off"
            value={name}
            onChange={e => { setName(e.target.value); setNameError(false); }}
            onKeyDown={e => e.key === 'Enter' && submit()}
            placeholder="Hollow Knight"
            aria-invalid={nameError}
            className={INPUT}
          />
          {nameError && <p className="text-[12px] text-accent-ink mt-1.5">Enter a game name</p>}
        </div>

        <div>
          <div className="flex items-baseline justify-between mb-1.5">
            <label htmlFor="add-game-dir" className="text-[13px] font-semibold text-fg">Suggested save folder</label>
            <span className="text-[11px] text-faint">Optional</span>
          </div>
          <input
            id="add-game-dir"
            autoComplete="off"
            value={dir}
            onChange={e => setDir(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && submit()}
            placeholder="C:\Users\me\AppData\Roaming\Hollow Knight"
            className={`${INPUT} font-mono text-[13px]`}
          />
          <p className="text-[11px] text-faint mt-1.5">
            Used as a fallback for machines that have no stored path. You can change it later from the game page.
          </p>
        </div>

        {error && (
          <p role="alert" className="text-[13px] text-accent-ink bg-accent-soft border border-accent-line rounded-md px-3 py-2">{error}</p>
        )}

        <div className="flex justify-end gap-2">
          <Button onClick={onClose} disabled={busy}>Cancel</Button>
          <Button variant="primary" onClick={submit} disabled={busy}>{busy ? 'Adding…' : 'Add game'}</Button>
        </div>
      </div>
    </div>
  );
}
