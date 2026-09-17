import { useState } from 'react';
import { Button } from './ui/Button';

interface Props {
  /** True when the last attempt was refused (wrong password), vs. simply never having tried. */
  wrongPassword?: boolean;
  onSubmit: (password: string) => void;
}

/**
 * plan.md Phase 2: "Remove the password field from the header... SignIn.tsx renders whenever no
 * credential is held or the server returns 401." Ships as option (a) from implementation.md — the
 * existing `X-Admin-Password` kept in localStorage, this screen is just where it's typed. A real
 * session endpoint (option b) is filed as a follow-up, not built here.
 */
export function SignIn({ wrongPassword = false, onSubmit }: Props) {
  const [password, setPassword] = useState('');

  function submit() {
    onSubmit(password.trim());
  }

  return (
    <div className="flex-1 flex items-center justify-center animate-rise">
      <div className="w-[320px] flex flex-col gap-4 items-center">
        <div className="w-11 h-11 rounded-full bg-accent-soft border border-accent-line flex items-center justify-center text-accent text-lg" aria-hidden>
          🔒
        </div>
        <div className="text-center">
          <h1 className="text-lg font-bold text-fg">SaveLocker</h1>
          <p className="text-[13px] text-dim mt-1">
            {wrongPassword ? 'Wrong password. Try again.' : 'Enter the admin password to continue.'}
          </p>
        </div>
        <input
          autoFocus
          type="password"
          value={password}
          onChange={e => setPassword(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && submit()}
          placeholder="Password"
          className="w-full px-3 py-2 bg-panel text-fg border border-line rounded-md text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-accent"
        />
        <Button variant="primary" className="w-full text-center" onClick={submit}>
          Connect
        </Button>
        <p className="text-[11px] text-faint text-center">
          If the server has no password set, leave this blank.
        </p>
      </div>
    </div>
  );
}
