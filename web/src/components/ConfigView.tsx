import { useState, useEffect } from 'react';
import { api, setPassword } from '../api';
import type { GameSummary, Machine, Settings, Enrollment, EffectiveServerUrl, AgentHealth, ServerBuildInfo } from '../types';
import { fleetSkew, isNewerThanConsole, isTestBuild, normalizeVersion } from '../versionSkew';
import { AgentUpdatesCard } from './AgentUpdatesCard';

interface Props {
  games: GameSummary[];
  machines: Machine[];
  settings: Settings;
  health: AgentHealth[];
  build?: ServerBuildInfo;
  onRefresh: () => void;
}

const asUtc = (t: string) => /[Z+]/.test(t.slice(-6)) ? t : t + 'Z';
const when = (t: string | null | undefined) => t ? new Date(asUtc(t)).toLocaleString() : '—';

export function ConfigView({ games, machines, settings, health, build, onRefresh }: Props) {
  const healthByMachine = new Map(health.map(h => [h.machineId, h]));
  const skew = fleetSkew(build?.version, health);
  const [copiedBuild, setCopiedBuild] = useState(false);
  const [sgdbInput, setSgdbInput] = useState('');
  const [savingKey, setSavingKey] = useState(false);
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  // Per-game retention inputs: gameId -> string (empty = use default)
  const [retentionInputs, setRetentionInputs] = useState<Record<string, string>>(
    () => Object.fromEntries(games.map(s => [s.game.id, s.game.retainVersions?.toString() ?? '']))
  );

  /**
   * The input is cleared and the view refreshed only after the server confirms the key was stored.
   * It used to do both unconditionally: a rejected key wiped what you had just pasted and the
   * panel refreshed into "configured", so a typo looked exactly like success — while the previously
   * working key had already been overwritten. The server verifies before storing now, and answers
   * 4xx on rejection, which `api.saveSgdbKey` turns into a throw carrying the server's explanation.
   */
  async function handleSaveKey() {
    const v = sgdbInput.trim();
    if (!v) { alert('Paste a SteamGridDB API key first.'); return; }
    setSavingKey(true);
    try {
      const res = await api.saveSgdbKey(v);
      setSgdbInput('');
      alert(res.message || 'Saved.');
      onRefresh();
    } catch (e) {
      // Input deliberately retained: the paste is the thing worth keeping when this fails.
      alert('Key not saved: ' + (e as Error).message);
    } finally { setSavingKey(false); }
  }

  async function handleClearKey() {
    if (!confirm('Clear the SteamGridDB API key? Artwork refresh will stop working until a key is set.')) return;
    try { await api.saveSgdbKey(null); onRefresh(); } catch (e) { alert('Could not clear key: ' + (e as Error).message); }
  }

  async function handleSetPassword() {
    if (!newPassword) { alert('Enter a new password.'); return; }
    if (newPassword !== confirmPassword) { alert('Passwords do not match.'); return; }
    try {
      const res = await api.setAdminPassword(newPassword);
      setPassword(newPassword);
      setNewPassword('');
      setConfirmPassword('');
      alert(res.message);
      onRefresh();
    } catch (e) { alert('Could not set password: ' + (e as Error).message); }
  }

  async function handleClearPassword() {
    if (!confirm('Remove the admin password? The dashboard will be accessible to anyone on your network.')) return;
    try {
      await api.setAdminPassword(null);
      setPassword('');
      onRefresh();
    } catch (e) { alert('Could not clear password: ' + (e as Error).message); }
  }

  async function handleSaveRetention(gameId: string, gameName: string) {
    const raw = retentionInputs[gameId]?.trim();
    const value = raw === '' ? null : parseInt(raw, 10);
    if (value !== null && (isNaN(value) || value < 0)) { alert('Enter a positive number, or leave blank to use the server default.'); return; }
    try {
      await api.setRetention(gameId, value);
      onRefresh();
    } catch (e) { alert(`Could not update retention for ${gameName}: ` + (e as Error).message); }
  }

  async function handleDeleteMachine(machineId: string, name: string) {
    if (!confirm(`Delete machine "${name}"? Its API key stops working immediately. Saved versions it uploaded are kept as history.`)) return;
    try { await api.deleteMachine(machineId); onRefresh(); } catch (e) { alert('Delete machine failed: ' + (e as Error).message); }
  }

  // ── Enrollment ──
  const [enrollments, setEnrollments] = useState<Enrollment[]>([]);
  const [enrollName, setEnrollName] = useState('');
  const [enrollTtl, setEnrollTtl] = useState('15');
  const [enrollServerUrl, setEnrollServerUrl] = useState('');
  const [minting, setMinting] = useState(false);

  async function loadEnrollments() {
    try { setEnrollments(await api.enrollments()); }
    catch { /* non-fatal */ }
  }

  useEffect(() => { loadEnrollments(); }, []);

  // The URL the policy file will actually carry. Worth showing unprompted: the failure it prevents
  // is silent — an enrollment file that works perfectly for the admin standing at the server and
  // sends the Deck looking for itself.
  const [effectiveUrl, setEffectiveUrl] = useState<EffectiveServerUrl | null>(null);
  useEffect(() => { api.effectiveServerUrl().then(setEffectiveUrl).catch(() => setEffectiveUrl(null)); }, []);
  // Only an INFERRED loopback address blocks minting. One that was configured or typed is a
  // deliberate same-box setup (agent and server on one machine) and is perfectly valid.
  const blockedByLoopback = effectiveUrl?.isLoopback === true && !effectiveUrl.fromConfig;

  async function handleMintEnrollment() {
    const ttl = parseInt(enrollTtl, 10);
    if (!Number.isFinite(ttl) || ttl < 1) { alert('Enter an expiry in minutes (at least 1).'); return; }
    setMinting(true);
    try {
      const res = await api.createEnrollment({
        machineName: enrollName.trim() || null,
        ttlMinutes: ttl,
        serverUrl: enrollServerUrl.trim() || null,
        gameIds: null, // every enabled game — the agent's reconcile would adopt them all anyway
      });

      // The raw token is in this response and nowhere else: the server stored only its hash. If the
      // user does not get the file now, the token is unrecoverable and they must mint another.
      const blob = new Blob([JSON.stringify(res.policy, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `savelocker-enroll-${res.policy.machineName || 'machine'}.json`;
      a.click();
      URL.revokeObjectURL(url);

      setEnrollName('');
      await loadEnrollments();
    } catch (e) { alert('Could not create the enrollment file: ' + (e as Error).message); }
    finally { setMinting(false); }
  }

  async function handleRevokeEnrollment(id: string) {
    if (!confirm('Revoke this enrollment token? An agent still holding the file will not be able to use it.')) return;
    try { await api.revokeEnrollment(id); await loadEnrollments(); }
    catch (e) { alert('Revoke failed: ' + (e as Error).message); }
  }

  function enrollmentState(e: Enrollment): { text: string; color: string } {
    if (e.redeemedAt) return { text: `used by ${e.redeemedByMachineName ?? 'a machine'}`, color: '#556070' };
    if (new Date(asUtc(e.expiresAt)) <= new Date()) return { text: 'expired', color: '#f4a60d' };
    return { text: `valid until ${when(e.expiresAt)}`, color: '#129271' };
  }

  const card = { background: '#1E252A', border: '1px solid #494949', borderRadius: 8, overflow: 'hidden' } as const;
  const cardHeader = { padding: '11px 18px', borderBottom: '1px solid #494949', display: 'flex', alignItems: 'center', justifyContent: 'space-between' } as const;
  const thStyle = { padding: '8px 18px', textAlign: 'left' as const, fontSize: 11, color: '#556070', fontWeight: 500 };
  const tdStyle = { padding: '11px 18px', fontSize: 13, fontWeight: 500 };
  const tdMono = { padding: '11px 18px', fontSize: 11, color: '#8b9aaa', fontFamily: "'JetBrains Mono', monospace" };
  const rowSep = { borderTop: '1px solid #252e35' };

  return (
    <main className="page-scroll" style={{ padding: '20px 24px', maxWidth: 900, margin: '0 auto', width: '100%', display: 'flex', flexDirection: 'column', gap: 16 }}>

      {/* Page heading */}
      <div style={{ padding: '4px 0 8px' }}>
        <h1 style={{ fontSize: 22, fontWeight: 700, letterSpacing: '-0.4px', color: '#ECEFF1' }}>Configuration</h1>
        <p style={{ fontSize: 12, color: '#9CA3AF', lineHeight: 1.6, marginTop: 4 }}>SaveLocker · Self-hosted cloud save manager</p>
      </div>

      {/* ── Server Settings Card ── */}
      <div style={card}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Server settings</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>SteamGridDB artwork</span>
        </div>

        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 14 }}>

          {/* Current key status */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
            <span style={{ fontSize: 13, color: '#ECEFF1' }}>SteamGridDB API key:</span>
            {settings.steamGridDbConfigured ? (
              <>
                <span style={{ padding: '2px 7px', background: '#129271', color: '#fff', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>configured</span>
                <span style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 12, color: '#ECEFF1', letterSpacing: '0.04em' }}>{settings.steamGridDbKeyMasked || ''}</span>
                {settings.steamGridDbFromConfig && (
                  <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>(from config file — saving here overrides it)</span>
                )}
              </>
            ) : (
              <span style={{ padding: '2px 7px', border: '1px solid #f4a60d', color: '#f4a60d', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>not set</span>
            )}
          </div>

          {/* Input + actions */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <input
              type="text"
              value={sgdbInput}
              onChange={e => setSgdbInput(e.target.value)}
              placeholder="Paste SteamGridDB API key"
              style={{ flex: 1, minWidth: 220, padding: '7px 10px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, fontFamily: "'Inter', sans-serif", transition: 'border-color 0.15s' }}
            />
            <button
              onClick={handleSaveKey}
              disabled={savingKey}
              style={{ padding: '6px 14px', background: '#129271', color: '#fff', border: 'none', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: savingKey ? 'not-allowed' : 'pointer', opacity: savingKey ? 0.5 : 1, whiteSpace: 'nowrap' }}
            >
              {savingKey ? 'Verifying…' : 'Save key'}
            </button>
            {settings.steamGridDbConfigured && (
              <button
                onClick={handleClearKey}
                style={{ padding: '6px 12px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, cursor: 'pointer' }}
              >
                Clear
              </button>
            )}
          </div>
          <p style={{ fontSize: 11, color: '#9CA3AF', marginTop: -6 }}>
            Free key: <a href="https://www.steamgriddb.com" target="_blank" rel="noreferrer" style={{ color: '#129271' }}>steamgriddb.com</a> → user menu → Preferences → API.
          </p>

        </div>
      </div>

      {/* ── Admin Password ── */}
      <div style={card}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Admin password</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>dashboard access control</span>
        </div>
        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 14 }}>

          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <span style={{ fontSize: 13, color: '#ECEFF1' }}>Status:</span>
            {settings.adminPasswordSet ? (
              <span style={{ padding: '2px 7px', background: '#129271', color: '#fff', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>protected</span>
            ) : (
              <span style={{ padding: '2px 7px', border: '1px solid #f4a60d', color: '#f4a60d', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>open — no password set</span>
            )}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <input
              type="password"
              value={newPassword}
              onChange={e => setNewPassword(e.target.value)}
              placeholder={settings.adminPasswordSet ? 'New password' : 'Set password'}
              style={{ flex: 1, minWidth: 160, padding: '7px 10px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, fontFamily: "'Inter', sans-serif" }}
            />
            <input
              type="password"
              value={confirmPassword}
              onChange={e => setConfirmPassword(e.target.value)}
              placeholder="Confirm password"
              onKeyDown={e => e.key === 'Enter' && handleSetPassword()}
              style={{ flex: 1, minWidth: 160, padding: '7px 10px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, fontFamily: "'Inter', sans-serif" }}
            />
            <button
              onClick={handleSetPassword}
              style={{ padding: '6px 14px', background: '#129271', color: '#fff', border: 'none', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: 'pointer', whiteSpace: 'nowrap' }}
            >
              {settings.adminPasswordSet ? 'Change password' : 'Set password'}
            </button>
            {settings.adminPasswordSet && (
              <button
                onClick={handleClearPassword}
                style={{ padding: '6px 12px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, cursor: 'pointer' }}
              >
                Remove
              </button>
            )}
          </div>
          <p style={{ fontSize: 11, color: '#9CA3AF', marginTop: -6 }}>
            Protects the dashboard from casual access on your local network. Enter your password in the nav bar to connect.
          </p>

        </div>
      </div>

      {/* ── Agent Updates ── */}
      <AgentUpdatesCard settings={settings} onScheduleChanged={onRefresh} />

      {/* ── Save retention ── */}
      <div style={card}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Save retention</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>versions stored per game</span>
        </div>
        <div style={{ padding: '10px 18px 4px', fontSize: 11, color: '#556070' }}>
          Leave blank to use the server default (10). Set to 0 for unlimited. Changes take effect on the next upload.
        </div>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#222d34', borderBottom: '1px solid #494949' }}>
              <th style={thStyle}>Game</th>
              <th style={thStyle}>Storage used</th>
              <th style={{ ...thStyle, width: 160 }}>Keep versions</th>
              <th style={{ ...thStyle, width: 80 }}></th>
            </tr>
          </thead>
          <tbody>
            {games.length === 0
              ? <tr><td colSpan={4} style={{ padding: '20px 18px', color: '#556070', fontSize: 13 }}>No games tracked yet.</td></tr>
              : games
                  .slice()
                  .sort((a, b) => b.totalStorageBytes - a.totalStorageBytes)
                  .map(s => (
                    <tr key={s.game.id} style={rowSep}>
                      <td style={tdStyle}>{s.game.name}</td>
                      <td style={tdMono}>{(s.totalStorageBytes / (1024 * 1024)).toFixed(2)} MB</td>
                      <td style={{ padding: '8px 18px' }}>
                        <input
                          type="number"
                          min={0}
                          value={retentionInputs[s.game.id] ?? ''}
                          onChange={e => setRetentionInputs(prev => ({ ...prev, [s.game.id]: e.target.value }))}
                          placeholder="default (10)"
                          style={{ width: '100%', padding: '5px 8px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 4, fontSize: 12, fontFamily: "'JetBrains Mono', monospace" }}
                        />
                      </td>
                      <td style={{ padding: '8px 18px' }}>
                        <button
                          onClick={() => handleSaveRetention(s.game.id, s.game.name)}
                          style={{ padding: '4px 12px', background: '#129271', color: '#fff', border: 'none', borderRadius: 4, fontSize: 11, fontWeight: 600, cursor: 'pointer' }}
                        >
                          Save
                        </button>
                      </td>
                    </tr>
                  ))
            }
          </tbody>
        </table>
      </div>

      {/* ── Enroll a machine ── */}
      <div style={{ ...card, marginBottom: 24 }}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Enroll a machine</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>single-use file — set up an agent without pasting an API key</span>
        </div>

        <div style={{ padding: '14px 18px', borderBottom: '1px solid #252e35' }}>
          <p style={{ margin: '0 0 12px', fontSize: 12.5, color: '#8b9aaa', lineHeight: 1.5 }}>
            Downloads a file carrying a short-lived, single-use token — never an API key. Copy it to the
            new machine and run <code style={{ fontFamily: "'JetBrains Mono', monospace", color: '#ECEFF1' }}>savelocker enroll --file &lt;file&gt;</code>.
            The agent trades the token for its own key, pins this server, and picks up every enabled game.
            The file is downloaded once and cannot be shown again.
          </p>

          {effectiveUrl && (
            <p style={{
              margin: '0 0 12px', fontSize: 12, lineHeight: 1.5,
              color: effectiveUrl.isLoopback ? '#f4a60d' : '#8b9aaa',
            }}>
              {blockedByLoopback ? (
                <>
                  <strong>This console was reached at {effectiveUrl.url}</strong>, which no other machine
                  can use — an enrollment file naming it would send the new machine looking for itself.
                  Reopen the console at this server's LAN address, set <code style={{ fontFamily: "'JetBrains Mono', monospace" }}>Server:PublicBaseUrl</code>,
                  or type the address below. Creating a file is blocked until then.
                </>
              ) : (
                <>
                  The file will tell the agent to sync with{' '}
                  <code style={{ fontFamily: "'JetBrains Mono', monospace", color: '#ECEFF1' }}>{effectiveUrl.url}</code>
                  {effectiveUrl.fromConfig ? ' (from Server:PublicBaseUrl).' : '.'} Override it below if
                  that is not how this machine reaches the server.
                </>
              )}
            </p>
          )}

          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: '1 1 180px' }}>
              <span style={{ fontSize: 11, color: '#556070' }}>Machine name (optional — binds the file to it)</span>
              <input
                value={enrollName}
                onChange={e => setEnrollName(e.target.value)}
                placeholder="steamdeck"
                style={{ padding: '6px 9px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 4, fontSize: 12.5 }}
              />
            </label>

            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, width: 110 }}>
              <span style={{ fontSize: 11, color: '#556070' }}>Expires (min)</span>
              <input
                type="number"
                min={1}
                value={enrollTtl}
                onChange={e => setEnrollTtl(e.target.value)}
                style={{ padding: '6px 9px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 4, fontSize: 12.5, fontFamily: "'JetBrains Mono', monospace" }}
              />
            </label>

            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, flex: '1 1 220px' }}>
              <span style={{ fontSize: 11, color: '#556070' }}>Server URL the agent should use (optional)</span>
              <input
                value={enrollServerUrl}
                onChange={e => setEnrollServerUrl(e.target.value)}
                placeholder={effectiveUrl?.url ?? window.location.origin}
                style={{ padding: '6px 9px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 4, fontSize: 12.5, fontFamily: "'JetBrains Mono', monospace" }}
              />
            </label>

            {/* One expression drives both the disabled state and the way it looks — a control that
                is dead but still renders bright green with a pointer cursor reads as a broken app
                rather than a blocked action. */}
            {(() => {
              const blocked = minting || (blockedByLoopback && !enrollServerUrl.trim());
              return (
                <button
                  onClick={handleMintEnrollment}
                  disabled={blocked}
                  title={blockedByLoopback && !enrollServerUrl.trim()
                    ? 'Enter the address agents should use, or reopen the console at this server\'s LAN address.'
                    : undefined}
                  style={{ padding: '7px 14px', background: '#129271', color: '#fff', border: 'none', borderRadius: 4, fontSize: 12, fontWeight: 600, cursor: blocked ? 'not-allowed' : 'pointer', opacity: blocked ? 0.5 : 1 }}
                >
                  {minting ? 'Creating…' : 'Create enrollment file'}
                </button>
              );
            })()}
          </div>

          {/* window.location.origin was wrong here: in dev that is Vite's port, and behind any
              front end it is the browser's view rather than the server's. The effective URL comes
              from the server, which is the thing that actually writes it into the file. */}
          <p style={{ margin: '10px 0 0', fontSize: 11.5, color: '#556070', lineHeight: 1.5 }}>
            Set the server URL when the new machine reaches this server at a different address than
            you did — otherwise leave it blank.
          </p>
        </div>

        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#222d34', borderBottom: '1px solid #494949' }}>
              <th style={thStyle}>For machine</th>
              <th style={thStyle}>Created</th>
              <th style={thStyle}>State</th>
              <th style={{ ...thStyle, textAlign: 'right' }}></th>
            </tr>
          </thead>
          <tbody>
            {enrollments.length === 0
              ? <tr><td colSpan={4} style={{ padding: '20px 18px', color: '#556070', fontSize: 13 }}>No enrollment files created.</td></tr>
              : enrollments.map(e => {
                  const state = enrollmentState(e);
                  return (
                    <tr key={e.id} style={rowSep}>
                      <td style={tdStyle}>{e.machineName ?? <span style={{ color: '#556070' }}>any machine</span>}</td>
                      <td style={tdMono}>{when(e.createdAt)}</td>
                      <td style={{ ...tdMono, color: state.color }}>{state.text}</td>
                      <td style={{ padding: '11px 18px', textAlign: 'right' }}>
                        <button
                          onClick={() => handleRevokeEnrollment(e.id)}
                          style={{ padding: '4px 10px', border: '1px solid #f4a60d', color: '#f4a60d', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }}
                        >
                          {e.redeemedAt ? 'Remove' : 'Revoke'}
                        </button>
                      </td>
                    </tr>
                  );
                })
            }
          </tbody>
        </table>
      </div>

      {/* ── Console build ──
          Deliberately immediately above Machines: the fleet's agent versions are in that table, so
          console and agents are compared without leaving the page. */}
      <div style={{ ...card, marginBottom: 24 }}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Console</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>what this server and dashboard are running</span>
        </div>
        <div style={{ padding: '14px 18px', display: 'flex', alignItems: 'flex-start', gap: 28, flexWrap: 'wrap' }}>
          <BuildField label="Version" value={build ? (build.version === 'dev' ? 'dev' : `v${build.version}`) : '…'} wide />
          <BuildField label="Commit" value={build?.commit || '—'} />
          <BuildField
            label="Built"
            value={build?.builtAt ? new Date(build.builtAt).toLocaleString() : '—'}
            wide
          />

          <div style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 8 }}>
            {build && !build.isRelease && (
              <span
                title="This build is not a tagged release."
                style={{ padding: '2px 8px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#f4a60d', border: '1px solid #f4a60d' }}
              >
                DEV BUILD
              </span>
            )}
            <button
              onClick={() => {
                const text = [
                  `SaveLocker console ${build?.version ?? 'unknown'}`,
                  build?.commit ? `commit ${build.commit}` : null,
                  build?.builtAt ? `built ${build.builtAt}` : null,
                ].filter(Boolean).join('\n');
                navigator.clipboard?.writeText(text);
                setCopiedBuild(true);
                setTimeout(() => setCopiedBuild(false), 1500);
              }}
              title="Copy the build identity — paste it into a bug report"
              style={{ padding: '4px 11px', background: 'transparent', color: '#8b9aaa', border: '1px solid #494949', borderRadius: 4, fontSize: 11, cursor: 'pointer' }}
            >
              {copiedBuild ? '✓ Copied' : 'Copy'}
            </button>
            <a
              href="#whats-new"
              style={{ padding: '4px 11px', color: '#129271', border: '1px solid #129271', borderRadius: 4, fontSize: 11, textDecoration: 'none' }}
            >
              Release notes
            </a>
          </div>
        </div>

        {/* Version skew. Absent when the fleet agrees with the console — the same rule the problem
            badge follows: a healthy fleet should be quiet. */}
        {(skew.aheadOfConsole.length > 0 || skew.mixedVersions.length > 0) && (
          <div style={{ padding: '0 18px 14px', display: 'flex', flexDirection: 'column', gap: 8 }}>
            {skew.aheadOfConsole.length > 0 && (
              <div style={{ padding: '9px 12px', borderRadius: 5, border: '1px solid #f4a60d', color: '#f4a60d', fontSize: 12, lineHeight: 1.5 }}>
                <strong>{skew.aheadOfConsole.join(', ')}</strong>{' '}
                {skew.aheadOfConsole.length > 1 ? 'are running agents' : 'is running an agent'} newer
                than this console. A newer agent can expect endpoints or behaviour this server does
                not have, and the result is an opaque HTTP error rather than anything that says
                "version skew". Pull the latest server image.
              </div>
            )}
            {skew.mixedVersions.length > 0 && (
              <div style={{ padding: '9px 12px', borderRadius: 5, border: '1px solid #494949', color: '#8b9aaa', fontSize: 12, lineHeight: 1.5 }}>
                The fleet is running <strong>{skew.mixedVersions.length} different agent versions</strong>{' '}
                ({skew.mixedVersions.map(v => `v${v}`).join(', ')}). Agents that differ can disagree
                about exclude globs and save paths, which shows up as repeated sync conflicts rather
                than as a version problem. Keeping them identical avoids it.
              </div>
            )}
          </div>
        )}
      </div>

      {/* ── Machines / API Keys ── */}
      <div style={{ ...card, marginBottom: 24 }}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Machines</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>agent health, versions, and keys</span>
        </div>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: '#222d34', borderBottom: '1px solid #494949' }}>
              <th style={thStyle}>Machine</th>
              <th style={thStyle}>Agent</th>
              <th style={thStyle}>Status</th>
              <th style={thStyle}>Last sync</th>
              <th style={thStyle}>Games</th>
              <th style={{ ...thStyle, textAlign: 'right' }}></th>
            </tr>
          </thead>
          <tbody>
            {machines.length === 0
              ? <tr><td colSpan={6} style={{ padding: '20px 18px', color: '#556070', fontSize: 13 }}>No machines registered.</td></tr>
              : machines.map(m => {
                  const h = healthByMachine.get(m.id);
                  // Info events are routine confirmations (an update landed, a plugin refreshed) —
                  // not something needing a human's attention, so they don't count toward "problems".
                  const problems = h?.openEvents.filter(e => e.severity !== 'Info').length ?? 0;
                  const infoEvents = h?.openEvents.filter(e => e.severity === 'Info') ?? [];

                  // Never reported at all is its own state, and a meaningful one: a machine that was
                  // enrolled but whose agent has never run looks nothing like one that went offline.
                  // The health API returns a row for EVERY machine on purpose, so `!h` was never
                  // true and this branch was unreachable — a freshly enrolled agent rendered as
                  // "offline since —", which reads as a machine that has stopped rather than one
                  // that has not started. The absent heartbeat is the thing to test.
                  const status = !h || !h.lastHeartbeat
                    ? { text: 'never reported', color: '#556070' }
                    : h.online
                      ? { text: 'online', color: '#129271' }
                      : { text: `offline since ${when(h.lastHeartbeat)}`, color: '#f4a60d' };

                  return (
                    <tr key={m.id} style={rowSep}>
                      <td style={tdStyle}>
                        {m.name}
                        {problems > 0 && (
                          <span
                            title={h?.openEvents.filter(e => e.severity !== 'Info').map(e => e.message).join('\n')}
                            style={{ marginLeft: 8, padding: '1px 6px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#e5534b', border: '1px solid #e5534b', whiteSpace: 'nowrap', display: 'inline-block' }}
                          >
                            {problems} problem{problems > 1 ? 's' : ''}
                          </span>
                        )}
                        {infoEvents.length > 0 && (
                          <span
                            title={infoEvents.map(e => e.message).join('\n')}
                            style={{ marginLeft: 6, padding: '1px 6px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#4a9eff', border: '1px solid #4a9eff', whiteSpace: 'nowrap', display: 'inline-block' }}
                          >
                            {infoEvents.length} update{infoEvents.length > 1 ? 's' : ''}
                          </span>
                        )}
                        {(h?.offlineQueueDepth ?? 0) > 0 && (
                          <span style={{ marginLeft: 6, padding: '1px 6px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#f4a60d', border: '1px solid #f4a60d' }}>
                            {h!.offlineQueueDepth} queued
                          </span>
                        )}
                      </td>
                      <td style={tdMono}>
                        {h?.agentVersion ? `v${normalizeVersion(h.agentVersion)}` : '—'}
                        {h?.platform ? <span style={{ color: '#556070' }}> · {h.platform}</span> : null}
                        {isTestBuild(h?.agentVersion) && (
                          <span
                            title="A throwaway build from CI, not a release. Stamped so it cannot be mistaken for one."
                            style={{ marginLeft: 6, padding: '1px 6px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#8b9aaa', border: '1px solid #556070' }}
                          >
                            TEST BUILD
                          </span>
                        )}
                        {isNewerThanConsole(h?.agentVersion, build?.version) && (
                          <span
                            title="This agent is newer than the console. It may expect endpoints or behaviour this server does not have — upgrade the server container."
                            style={{ marginLeft: 6, padding: '1px 6px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#f4a60d', border: '1px solid #f4a60d' }}
                          >
                            NEWER THAN CONSOLE
                          </span>
                        )}
                      </td>
                      <td style={{ ...tdMono, color: status.color }}>{status.text}</td>
                      <td style={tdMono}>{when(h?.lastSyncTime)}</td>
                      <td style={tdMono}>
                        {h ? `${h.trackedGames}` : '—'}
                        {(h?.unmappedGames ?? 0) > 0 && (
                          <span style={{ color: '#f4a60d' }}> ({h!.unmappedGames} unmapped)</span>
                        )}
                      </td>
                      <td style={{ padding: '11px 18px', textAlign: 'right' }}>
                        <button
                          onClick={() => handleDeleteMachine(m.id, m.name)}
                          style={{ padding: '4px 10px', border: '1px solid #f4a60d', color: '#f4a60d', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }}
                        >
                          Delete
                        </button>
                      </td>
                    </tr>
                  );
                })
            }
          </tbody>
        </table>
      </div>

    </main>
  );
}


function BuildField({ label, value, wide = false }: { label: string; value: string; wide?: boolean }) {
  return (
    <div style={{ minWidth: wide ? 190 : 110 }}>
      <div style={{ fontSize: 10, fontWeight: 700, color: '#129271', textTransform: 'uppercase', letterSpacing: '0.12em' }}>
        {label}
      </div>
      <div style={{ marginTop: 4, fontSize: 12.5, color: '#ECEFF1', fontFamily: "'JetBrains Mono', monospace", wordBreak: 'break-all' }}>
        {value}
      </div>
    </div>
  );
}
