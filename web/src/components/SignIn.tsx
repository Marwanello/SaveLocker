import { useState } from 'react';
import type { FormEvent } from 'react';
import type { ServerBuildInfo } from '../types';
import { Button } from './ui/Button';
import { Mark } from './ui/Mark';

interface Props {
  /** Why this screen is showing anything beyond the plain prompt: a refused password, a lockout, a
   *  session that ended. Absent on a first visit — nobody has failed at anything yet. */
  notice?: { text: string; tone: 'error' | 'info' } | null;
  busy?: boolean;
  build?: ServerBuildInfo;
  onSubmit: (password: string, remember: boolean) => void;
}

/**
 * plan.md Phase 12.4: the prototype's lock screen. Signing in exchanges the password for a revocable
 * session token (see api.ts) — the password itself is sent once and never kept. **Remember this browser**
 * decides where that token lives: localStorage (until the server's 30-day limit) or sessionStorage (gone
 * with the browser). The prototype's live fleet chips are deliberately absent: this screen is
 * unauthenticated, and fleet state is not for a stranger on the LAN.
 *
 * Not trimmed: the password is set verbatim (Configuration), so whitespace at either end is part of it.
 */
export function SignIn({ notice = null, busy = false, build, onSubmit }: Props) {
  const [password, setPassword] = useState('');
  const [remember, setRemember] = useState(true);
  const wrong = notice?.tone === 'error';

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!busy) onSubmit(password, remember);
  }

  return (
    <div className="signin-wash flex-1 min-h-0 overflow-y-auto grid grid-cols-1 md:grid-cols-[minmax(0,1fr)_300px] animate-rise">
      <form onSubmit={submit} className="self-center justify-self-center w-full max-w-[420px] px-[34px] py-10">
        <div className="mb-[22px]"><Mark size={46} /></div>
        <h1 className="text-[29px] font-bold tracking-[-0.035em] text-fg">Unlock SaveLocker</h1>
        <p className="text-[13.5px] text-dim mt-2.5 mb-[26px] max-w-[44ch]">
          This console is protected by the admin password set on the server. There are no accounts and
          nothing to register — one password, kept by you.
        </p>

        <label className="block mb-3.5">
          <span className="block text-[10px] tracking-[0.13em] uppercase text-faint mb-[7px]">Password</span>
          <input
            autoFocus
            type="password"
            name="password"
            autoComplete="current-password"
            aria-invalid={wrong}
            aria-describedby={notice ? 'signin-notice' : undefined}
            value={password}
            onChange={e => setPassword(e.target.value)}
            placeholder="Enter the admin password"
            className={`w-full bg-panel text-fg border rounded-xl px-[15px] py-[13px] text-sm
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2
              ${wrong ? 'border-accent' : 'border-line'}`}
          />
        </label>
        {notice && (
          <p id="signin-notice" role={wrong ? 'alert' : 'status'}
            className={`text-[12.5px] -mt-1.5 mb-3.5 max-w-[44ch] ${wrong ? 'text-accent-ink' : 'text-dim'}`}>
            {notice.text}
          </p>
        )}

        <label className="flex items-center gap-[9px] text-[12.5px] text-dim mb-5 cursor-pointer">
          <input type="checkbox" checked={remember} onChange={e => setRemember(e.target.checked)}
            className="w-[15px] h-[15px] accent-[var(--color-accent)]
              focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2" />
          Remember this browser for 30 days
        </label>

        <Button type="submit" variant="primary" className="w-full" disabled={busy}>
          {busy ? 'Unlocking…' : 'Unlock'}
        </Button>

        <div className="flex justify-between font-mono text-[10.5px] text-faint mt-[18px] pt-3.5 border-t border-line">
          <span>{location.host}</span>
          <span>{build ? (build.version === 'dev' ? 'dev' : `v${build.version}`) : ''}</span>
        </div>
        <p className="text-[11.5px] text-faint mt-3 leading-[1.6]">
          Forgot it? Stop the server, delete the <code className="font-mono text-[10.5px] bg-tile border border-line rounded-[5px] px-[5px] py-px">Admin:PasswordHash</code> row
          from the <code className="font-mono text-[10.5px] bg-tile border border-line rounded-[5px] px-[5px] py-px">Settings</code> table
          (or unset <code className="font-mono text-[10.5px] bg-tile border border-line rounded-[5px] px-[5px] py-px">Admin__PasswordHash</code>)
          and start it again — <a href="#help/troubleshooting" className="text-dim underline underline-offset-2 rounded
            focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent focus-visible:outline-offset-2">Troubleshooting</a> has
          the steps.
        </p>
      </form>

      <aside className="hidden md:block border-l border-line bg-panel px-[26px] py-[34px]">
        <h2 className="text-[11.5px] font-semibold tracking-[0.1em] uppercase text-faint">While it is locked</h2>
        <ul className="mt-4 pl-[17px] list-disc text-dim text-[12.5px] flex flex-col gap-[11px]">
          <li>Agents keep syncing. The password guards the console, not the machines.</li>
          <li>Enrollment files already issued stay valid until they expire.</li>
          <li>Open conflicts still wait — you will see them the moment you unlock.</li>
        </ul>
      </aside>
    </div>
  );
}
