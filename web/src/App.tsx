import { useEffect, useRef, useState, useCallback } from 'react';
import { api, getPassword, setPassword } from './api';
import type { GameSummary, Machine, Command, Conflict, Settings, AgentHealth, ServerBuildInfo } from './types';
import { NavBar } from './components/NavBar';
import { GamesView } from './components/GamesView';
import { ConfigView } from './components/ConfigView';
import { AuditView } from './components/AuditView';
import { HelpView } from './components/HelpView';
import { WhatsNewView } from './components/WhatsNewView';
import { SignIn } from './components/SignIn';
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
  // 401 is distinct from a network/server error: it means the credential itself is wrong, which
  // is what SignIn exists to fix — a generic error banner would just leave the same wrong password
  // sitting in localStorage forever.
  const [authFailed, setAuthFailed] = useState(false);
  // One-shot: set by a notification's deep link, consumed (and cleared) by GamesView.
  const [pendingGameId, setPendingGameId] = useState<string | null>(null);

  const loadingRef = useRef(false);

  // Separate from load(): /api/admin/status is unauthenticated, so the version must still show
  // when the admin password is wrong or unset — which is exactly when you are diagnosing.
  useEffect(() => {
    api.adminStatus()
      .then(s => {
        setBuild(s.build);
        setUnreadNotes(hasUnreadNotes(s.build?.version));
      })
      .catch(() => { /* unreachable server is already surfaced by load() */ });
  }, []);

  const load = useCallback(async () => {
    if (loadingRef.current) return;
    loadingRef.current = true;
    setLoading(true);
    setError('');
    try {
      const [games, conflicts, machines, commands, settings, health] = await Promise.all([
        api.overview(), api.conflicts(), api.machines(), api.commands(), api.settings(), api.health(),
      ]);
      setData({ games, machines, commands, conflicts, settings, health });
      setAuthFailed(false);
    } catch (e) {
      const msg = (e as Error).message;
      if (msg.startsWith('401')) setAuthFailed(true);
      else setError('Failed to load: ' + msg);
    } finally {
      setLoading(false);
      loadingRef.current = false;
    }
  }, []);

  function handleSignIn(password: string) {
    setPassword(password);
    setAuthFailed(false);
    void load();
  }

  /** plan.md's "lock button": forgets the credential and drops back to SignIn. Clearing `data` too
   *  is what actually flips `needsSignIn` below — a stale games list would otherwise still render
   *  behind the lock as if nothing happened. */
  function handleLock() {
    setPassword('');
    setAuthFailed(false);
    setData(null);
  }

  function handleOpenGame(gameId: string | null) {
    setPendingGameId(gameId);
    setView('games');
  }

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    function onHash() { setView(viewFromHash()); }
    window.addEventListener('hashchange', onHash);
    return () => window.removeEventListener('hashchange', onHash);
  }, []);

  useEffect(() => {
    const id = setInterval(load, 15000);
    return () => clearInterval(id);
  }, [load]);

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

  async function handleAddGame() {
    const name = prompt('New game name (defines it on the server; agents map their local save dir):');
    if (!name?.trim()) return;
    const dir = prompt(
      'Suggested save folder (optional). E.g. C:\\Users\\me\\AppData\\Roaming\\Game. Leave blank to skip:',
      ''
    );
    try {
      await api.addGame(name.trim(), dir?.trim() || null);
      await load();
    } catch (e) { alert('Add game failed: ' + (e as Error).message); }
  }

  async function handleDismissProblem(id: string) {
    try { await api.dismissEvent(id); await load(); }
    catch (e) { alert('Dismiss failed: ' + (e as Error).message); }
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
  // Not gated on `loading`: an unauthenticated first load may still succeed (the server can have
  // no password set at all), so showing SignIn immediately when nothing is stored yet — rather
  // than waiting out that request — avoids a Sign-in → Loading… → Sign-in flash while it's in
  // flight, and `data` populating flips this off on its own once the load actually succeeds.
  const needsSignIn = (!getPassword() && !data) || authFailed;

  return (
    // A fixed viewport height, not a minimum: the games sidebar and the detail panel each own their
    // scrollbar, and they only get one if an ancestor bounds their height. With minHeight the page
    // itself grew and scrolled, so picking a game far down the list left the detail panel offscreen.
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden' }}>
      <NavBar
        view={view}
        onViewChange={v => { setView(v); if (!data && v !== 'help' && v !== 'whats-new') load(); }}
        onRefresh={load}
        onLock={handleLock}
        machines={data?.machines ?? []}
        build={build}
        unreadNotes={unreadNotes}
        problems={problems}
        escalatedConflicts={data?.conflicts.filter(c => c.escalated) ?? []}
        onDismissProblem={handleDismissProblem}
        onOpenGame={handleOpenGame}
      />

      {error && (
        <div style={{ padding: '10px 24px', color: '#f4a60d', fontSize: 13 }}>{error}</div>
      )}

      {needsSignIn && !isPublicView && (
        <SignIn wrongPassword={authFailed} onSubmit={handleSignIn} />
      )}

      {loading && !data && !needsSignIn && !isPublicView && (
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#556070', fontSize: 13 }}>
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
                onRefresh={load}
                onAddGame={handleAddGame}
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
                onRefresh={load}
              />
          }
        </div>
      )}
    </div>
  );
}
