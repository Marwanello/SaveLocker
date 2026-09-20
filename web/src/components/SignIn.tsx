import { useState } from 'react';
import type { FormEvent } from 'react';
import { Button } from './ui/Button';

interface Props {
  /** Why this screen is showing anything beyond the plain prompt: a refused password, a lockout, a
   *  session that ended. Absent on a first visit — nobody has failed at anything yet. */
  notice?: { text: string; tone: 'error' | 'info' } | null;
  busy?: boolean;
  onSubmit: (password: string) => void;
}

/**
 * plan.md Phase 2: "Remove the password field from the header... SignIn.tsx renders whenever no
 * credential is held or the server returns 401." Signing in exchanges the password for a revocable
 * session token (see api.ts) — the password itself is sent once and never kept.
 *
 * Not trimmed: the password is set verbatim (ConfigView), so whitespace at either end is part of it.
 * Trimming here as well would make such a password impossible to sign in with.
 */
export function SignIn({ notice = null, busy = false, onSubmit }: Props) {
  const [password, setPassword] = useState('');

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!busy) onSubmit(password);
  }

  return (
    <div className="flex-1 flex items-center justify-center animate-rise">
      <form onSubmit={submit} className="w-[320px] flex flex-col gap-4 items-center">
        <div className="w-11 h-11 rounded-full bg-accent-soft border border-accent-line flex items-center justify-center text-accent text-lg" aria-hidden>
          🔒
        </div>
        <div className="text-center">
          <h1 className="text-lg font-bold text-fg">SaveLocker</h1>
          <p className="text-[13px] text-dim mt-1">Enter the admin password to continue.</p>
        </div>
        <input
          autoFocus
          type="password"
          name="password"
          autoComplete="current-password"
          aria-label="Admin password"
          aria-invalid={notice?.tone === 'error'}
          value={password}
          onChange={e => setPassword(e.target.value)}
          placeholder="Password"
          className="w-full px-3 py-2 bg-panel text-fg border border-line rounded-md text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent"
        />
        <Button type="submit" variant="primary" className="w-full text-center" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </Button>
        {notice && (
          <p
            role={notice.tone === 'error' ? 'alert' : 'status'}
            className={`w-full text-center text-[13px] rounded-md px-3 py-2 border ${
              notice.tone === 'error'
                ? 'text-accent-ink bg-accent-soft border-accent-line'
                : 'text-dim bg-raise border-line'
            }`}
          >
            {notice.text}
          </p>
        )}
      </form>
    </div>
  );
}
