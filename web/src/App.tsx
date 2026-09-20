import { useEffect, useRef, useState, useCallback } from 'react';
import {
  api, ApiError, clearSession, dropLegacyPassword, errorText, hasSession,
  migrateLegacyPassword, signIn, signOut,
} from './api';
import type { GameSummary, Machine, Command, Conflict, Settings, AgentHealth, ServerBuildInfo } from './types';
import { NavBar } from './components/NavBar';
import { GamesView } from './components/GamesView';
import { ConfigView } from './components/ConfigView';
import { AuditView } from './components/AuditView';
import { HelpView } from './components/HelpView';
import { WhatsNewView } from './components/WhatsNewView';
import { SignIn } from './components/SignIn';
import { AddGameDialog } from './components/AddGameDialog';
import { hasUnreadNotes, markNotesSeen } from './releaseSeen';

type View = 'games' | 'config' | 'audit' | 'help' | 'whats-new';

interface AppData {
  games: GameSummary[];
  machines: Machine[];
  commands: Command[];
  conflicts: Conflict[];
  settings: Settings;
  health: AgentHealth[];
}

/**
 * The one place a URL becomes a view. Used at startup AND on every `hashchange`, which is the whole
 * point: the listener used to handle only #help and #whats-new, so pressing Back from Configuration
 * to Games changed the address bar and left the page showing Configuration. Any hash the parser does
 * not recognise is Games, which is also what an empty hash means.
 */
function viewFromHash(): View {
  if (location.hash === '#config') return 'config';
  if (location.hash === '#audit') return 'audit';
  if (location.hash.startsWith('#help')) return 'help';
  if (location.hash.startsWith('#whats-new')) return 'whats-new';
  return 'games';
}

export default function App() {
  const [view, setView] = useState<View>(viewFromHash);
  const [data, setData] = useState<AppData | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [build, setBuild] = useState<ServerBuildInfo | undefined>();
  const [unreadNotes, setUnreadNotes] = useState(false);
  const [addGameOpen, setAddGameOpen] = useState(false);

  // Whether the server has an admin password at all — null until /api/admin/status answers. This, not
  // "did a request 401", decides whether the sign-in screen exists: on a server with no password there
  // is nothing to sign in to (and nothing to Lock), and on one with a password the very first visit
  // used to be reported as a WRONG password because the automatic first load carries none.
  const [passwordRequired, setPasswordRequired] = useState<boolean | null>(null);
  // Whether this browser holds a session token. localStorage is not reactive, so it is mirrored here.
  const [signedIn, setSignedIn] = useState(hasSession);
  // What the sign-in screen says beyond its plain prompt. Only ever a REAL outcome: an attempt that was
  // refused, a lockout, or a session that ended — never the state of having not tried yet.
  const [signInNotice, setSignInNotice] = useState<{ text: string; tone: 'error' | 'info' } | null>(null);
  const [signInBusy, setSignInBusy] = useState(false);

  // One-shot: set by a notification's deep link, consumed (and cleared) by GamesView.
  const [pendingGameId, setPendingGameId] = useState<string | null>(null);

  const canLoad = passwordRequired === false || (passwordRequired === true && signedIn);
  const needsSignIn = passwordRequired === true && !signedIn;

  // A response that lands after the credential changed (Lock, sign-in, a 401) belongs to the previous
  // state and must not repaint it: without this, a load in flight when Lock was pressed put the games
  // back on screen behind the sign-in screen.
  const epochRef = useRef(0);
  const loadingRef = useRef(false);
  const reloadQueuedRef = useRef(false);

  // /api/admin/status is unauthenticated, so the version must still show when the admin password is
  // wrong or unset — which is exactly when you are diagnosing.
  const refreshStatus = useCallback(async () => {
    try {
      const s = await api.adminStatus();
      setBuild(s.build);
      setUnreadNotes(hasUnreadNotes(s.build?.version));
      setPasswordRequired(s.passwordRequired);
      setError(e => (e.startsWith("Can't reach") ? '' : e));
    } catch {
      setError("Can't reach the server.");
    }
  }, []);

  const loadOnce = useCallback(async () => {
    const epoch = epochRef.current;
    setLoading(true);
    setError('');
    try {
      const [games, conflicts, machines, commands, settings, health] = await Promise.all([
        api.overview(), api.conflicts(), api.machines(), api.commands(), api.settings(), api.health(),
      ]);
      if (epoch !== epochRef.current) return;
      setData({ games, machines, commands, conflicts, settings, health });
      // Keep this in step with the server: an admin can set or remove the password from Configuration.
      setPasswordRequired(settings.adminPasswordSet);
    } catch (e) {
      if (epoch !== epochRef.current) return;
      if (e instanceof ApiError && e.status === 401) {
        // The credential is no longer good — expired, ended by Lock in another tab, or the password
        // changed — or the server gained a password while this console was open. Drop it and ask again.
        const hadSession = hasSession();
        clearSession();
        setSignedIn(false);
        setData(null);
        setPasswordRequired(true);
        setSignInNotice(hadSession ? { text: 'Your session ended. Sign in again.', tone: 'info' } : null);
      } else {
        setError('Failed to load: ' + errorText(e));
      }
    } finally {
      setLoading(false);
    }
  }, []);

  // Single-flight, but never lossy: a request that arrives while a load is running (Refresh, an Add game,
  // a dismissal) used to be silently dropped, so the screen kept showing the state from before it.
  const load = useCallback(async () => {
    if (loadingRef.current) { reloadQueuedRef.current = true; return; }
    loadingRef.current = true;
    try {
      do {
        reloadQueuedRef.current = false;
        await loadOnce();
      } while (reloadQueuedRef.current);
    } finally {
      loadingRef.current = false;
    }
  }, [loadOnce]);

  // What does the server require? Asked on first contact, and again every few seconds until it answers.
  useEffect(() => { void refreshStatus(); }, [refreshStatus]);
  useEffect(() => {
    if (passwordRequired !== null) return;
    const id = setInterval(() => void refreshStatus(), 5000);
    return () => clearInterval(id);
  }, [passwordRequired, refreshStatus]);

  // Consoles from before sessions kept the admin PASSWORD in localStorage. Swap it for a session once —
  // or, if the server needs no password, just delete it — so the plaintext does not linger.
  useEffect(() => {
    if (passwordRequired === null) return;
    if (passwordRequired === false) { dropLegacyPassword(); return; }
    if (signedIn) return;
    let cancelled = false;
    void migrateLegacyPassword().then(r => {
      if (cancelled || r !== 'migrated') return;
      epochRef.current++;
      setSignedIn(true);
    });
    return () => { cancelled = true; };
  }, [passwordRequired, signedIn]);

  useEffect(() => { if (canLoad) void load(); }, [canLoad, load]);
  useEffect(() => {
    if (!canLoad) return; // nothing to poll while locked out: it would only be refused
    const id = setInterval(() => void load(), 15000);
    return () => clearInterval(id);
  }, [canLoad, load]);

  async function handleSignIn(password: string) {
    setSignInBusy(true);
    setSignInNotice(null);
    const result = await signIn(password);
    setSignInBusy(false);
    if (!result.ok) {
      setSignInNotice({ text: result.message, tone: 'error' });
      return;
    }
    epochRef.current++;
    setSignedIn(hasSession());
    await refreshStatus();
  }

  /** plan.md's "lock button": ends this session on the server and forgets it here. Clearing `data` too
   *  is what actually removes the console from view — a stale games list would otherwise still render
   *  behind the lock as if nothing happened. */
  async function handleLock() {
    epochRef.current++;
    setAddGameOpen(false);
    setData(null);
    setSignInNotice(null);
    setSignedIn(false);
    await signOut();
  }

  function handleOpenGame(gameId: string | null) {
    setPendingGameId(gameId);
    setView('games');
  }

  useEffect(() => {
    function onHash() { setView(viewFromHash()); }
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  useEffect(() => {
    if (view === 'config') location.hash = 'config';
    else if (view === 'audit') location.hash = 'audit';
    else if (view === 'help') { if (!location.hash.startsWith('#help')) location.hash = 'help'; }
    else if (view === 'whats-new') { if (!location.hash.startsWith('#whats-new')) location.hash = 'whats-new'; }
    else location.hash = '';
  }, [view]);

  // Opening the notes clears the dot. Done here rather than in the view so it fires however the
  // view was reached — nav button, version chip, or a #whats-new deep link.
  useEffect(() => {
    if (view === 'whats-new' && build) { markNotesSeen(build.version); setUnreadNotes(false); }
  }, [view, build]);

  async function handleAddGame(name: string, dir: string | null) {
    await api.addGame(name, dir);
    setAddGameOpen(false);
    await load();
  }

  // One reload after the whole batch, and one report of anything that failed — not a reload and an
  // alert per event, which for "Dismiss all" was N sequential round trips and up to N dialogs.
  async function handleDismissProblems(ids: string[]) {
    const failures: string[] = [];
    for (const id of ids) {
      try { await api.dismissEvent(id); } catch (e) { failures.push(errorText(e)); }
    }
    await load();
    if (failures.length === 1 && ids.length === 1) alert('Dismiss failed: ' + failures[0]);
    else if (failures.length > 0) alert(`Could not dismiss ${failures.length} of ${ids.length}: ${failures[0]}`);
  }

  // Errors before warnings: an agent that is not syncing outranks one that synced with a caveat.
  const problems = (data?.health ?? [])
    .flatMap(h => h.openEvents)
    .sort((a, b) =>
      (a.severity === b.severity ? 0 : a.severity === 'Error' ? -1 : 1) ||
      (new Date(b.lastSeen).getTime() - new Date(a.lastSeen).getTime()));

  // Help and What's New are bundled into the build — they need no server data and no password,
  // so they must render even when the console cannot authenticate.
  const isPublicView = view === 'help' || view === 'whats-new';

  return (
    // A fixed viewport height, not a minimum: the games sidebar and the detail panel each own their
    // scrollbar, and they only get one if an ancestor bounds their height. With minHeight the page
    // itself grew and scrolled, so picking a game far down the list left the detail panel offscreen.
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden' }}>
      <NavBar
        view={view}
        onViewChange={v => { setView(v); if (!data && canLoad && v !== 'help' && v !== 'whats-new') void load(); }}
        onRefresh={() => void load()}
        onLock={passwordRequired === true && signedIn ? () => void handleLock() : undefined}
        machines={data?.machines ?? []}
        build={build}
        unreadNotes={unreadNotes}
        problems={problems}
        escalatedConflicts={data?.conflicts.filter(c => c.escalated) ?? []}
        onDismissProblems={handleDismissProblems}
        onOpenGame={handleOpenGame}
      />

      {error && (
        <div style={{ padding: '10px 24px', color: '#f4a60d', fontSize: 13 }}>{error}</div>
      )}

      {needsSignIn && !isPublicView && (
        <SignIn notice={signInNotice} busy={signInBusy} onSubmit={p => void handleSignIn(p)} />
      )}

      {((passwordRequired === null && !error) || (canLoad && loading && !data)) && !isPublicView && (
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#8b9aaa', fontSize: 13 }}>
          Loading…
        </div>
      )}

      {view === 'help' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <HelpView />
        </div>
      )}

      {view === 'whats-new' && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          <WhatsNewView build={build} />
        </div>
      )}

      {data && !isPublicView && !needsSignIn && (
        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
          {view === 'games'
            ? <GamesView
                games={data.games}
                machines={data.machines}
                commands={data.commands}
                conflicts={data.conflicts}
                onRefresh={() => void load()}
                onAddGame={() => setAddGameOpen(true)}
                selectGameId={pendingGameId}
                onSelectGameHandled={() => setPendingGameId(null)}
              />
            : view === 'audit'
            ? <AuditView />
            : <ConfigView
                games={data.games}
                machines={data.machines}
                settings={data.settings}
                health={data.health}
                build={build}
                onRefresh={() => void load()}
              />
          }
        </div>
      )}

      {addGameOpen && <AddGameDialog onClose={() => setAddGameOpen(false)} onSubmit={handleAddGame} />}
    </div>
  );
}
