import { useState, useEffect, useRef, useCallback } from 'react';
import { api, setPassword } from '../api';
import type { GameSummary, Machine, Settings, AgentInstallerStatus, AgentPlatform, Enrollment, EffectiveServerUrl, AgentHealth, ServerBuildInfo } from '../types';
import { fleetSkew, isNewerThanConsole, isTestBuild, normalizeVersion } from '../versionSkew';

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

  const [autoFetchHoursInput, setAutoFetchHoursInput] = useState(() => String(settings.autoFetchHours));
  const [savingAutoFetchHours, setSavingAutoFetchHours] = useState(false);

  useEffect(() => { setAutoFetchHoursInput(String(settings.autoFetchHours)); }, [settings.autoFetchHours]);

  async function handleSaveAutoFetchHours() {
    const raw = autoFetchHoursInput.trim();
    const hours = raw === '' ? 0 : Number(raw);
    if (!Number.isFinite(hours) || hours < 0) {
      alert('Enter a non-negative number of hours. Set 0 to disable automatic fetching.');
      return;
    }
    setSavingAutoFetchHours(true);
    try {
      await api.setAutoFetchHours(hours);
      await onRefresh();
    } catch (e) { alert('Could not save auto-fetch schedule: ' + (e as Error).message); }
    finally { setSavingAutoFetchHours(false); }
  }
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
      <div style={card}>
        <div style={cardHeader}>
          <span style={{ fontSize: 13, fontWeight: 600, color: '#ECEFF1' }}>Agent updates</span>
          <span style={{ fontSize: 11.5, color: '#9CA3AF' }}>hosted packages</span>
        </div>
        <div style={{ padding: '16px 18px', display: 'flex', flexDirection: 'column', gap: 14 }}>

          {INSTALLER_SLOTS.map((slot, i) => (
            <InstallerSlotPanel key={slot.platform} slot={slot} first={i === 0} />
          ))}

          {/* Automatic GitHub fetch */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8, borderTop: '1px solid #2A3238', paddingTop: 14 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
              <span style={{ fontSize: 13, color: '#ECEFF1' }}>Automatic GitHub fetch:</span>
              {settings.autoFetchHours > 0 ? (
                <span style={{ padding: '2px 7px', background: '#129271', color: '#fff', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>
                  every {settings.autoFetchHours} h
                </span>
              ) : (
                <span style={{ padding: '2px 7px', border: '1px solid #556070', color: '#556070', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>disabled</span>
              )}
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <input
                type="number"
                min={0}
                step={0.5}
                value={autoFetchHoursInput}
                onChange={e => setAutoFetchHoursInput(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && handleSaveAutoFetchHours()}
                aria-label="Automatic GitHub fetch interval in hours"
                style={{ width: 140, padding: '7px 10px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, fontFamily: "'JetBrains Mono', monospace" }}
              />
              <span style={{ fontSize: 12, color: '#9CA3AF' }}>hours</span>
              <button
                onClick={handleSaveAutoFetchHours}
                disabled={savingAutoFetchHours}
                style={{ padding: '6px 14px', background: savingAutoFetchHours ? '#2A3238' : '#129271', color: savingAutoFetchHours ? '#556070' : '#fff', border: 'none', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: savingAutoFetchHours ? 'default' : 'pointer', whiteSpace: 'nowrap' }}
              >
                {savingAutoFetchHours ? 'Saving…' : 'Save schedule'}
              </button>
            </div>
            <p style={{ fontSize: 11, color: '#9CA3AF', margin: 0 }}>
              Set 0 to disable. When enabled, the server checks GitHub immediately, then at this interval; changes apply within a minute.
            </p>
          </div>

        </div>
      </div>

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
                  const problems = h?.openEvents.length ?? 0;

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
                          <span style={{ marginLeft: 8, padding: '1px 6px', borderRadius: 3, fontSize: 10, fontWeight: 700, color: '#e5534b', border: '1px solid #e5534b' }}>
                            {problems} problem{problems > 1 ? 's' : ''}
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

/**
 * One hosted package per platform. The slots are independent all the way down — separate
 * storage, separate GitHub asset, separate version — so each panel owns its own state rather than
 * the card holding one installer and a selector. An admin routinely has a newer Windows installer
 * than Linux tarball (or no tarball at all, on a release that predates it), and the card has to be
 * able to show that rather than imply the fleet is on one version.
 */
interface InstallerSlot {
  platform: AgentPlatform;
  label: string;
  /** `accept` for the file input. Advisory — the server is what actually refuses a wrong file. */
  accept: string;
  fileHint: string;
  /** Pulls the version out of a release asset's filename, so the admin rarely types it. */
  parseVersion: (name: string) => string;
}

const INSTALLER_SLOTS: InstallerSlot[] = [
  {
    platform: 'win-x64',
    label: 'Windows',
    accept: '.exe',
    fileHint: 'SaveLocker-Agent-Setup-x.y.z.exe',
    parseVersion: name => name.match(/Setup-(.+?)\.exe$/i)?.[1] ?? '',
  },
  {
    platform: 'linux-x64',
    label: 'Linux / Steam Deck',
    accept: '.gz,.tar.gz',
    fileHint: 'savelocker-x.y.z-linux-x64.tar.gz',
    parseVersion: name => name.match(/^savelocker-(.+?)-linux-x64\.tar\.gz$/i)?.[1] ?? '',
  },
  // Not an agent, and the only slot whose asset comes from another repository. It is here because a
  // Deck's Linux agent installs it, from this channel, on the same AutoUpdate switch — so an admin
  // asking "what is my fleet being offered?" reads all three in one place. The zip carries no
  // version in its name (Decky's release artifact is always SaveLocker.zip), so this is the one slot
  // where the Version field has to be typed — or filled by the GitHub fetch, which reads the tag.
  {
    platform: 'decky-plugin',
    label: 'Decky plugin',
    accept: '.zip',
    fileHint: 'SaveLocker.zip — type the version, it is not in the filename',
    parseVersion: name => name.match(/^SaveLocker-?(\d[\d.]*)\.zip$/i)?.[1] ?? '',
  },
];

function InstallerSlotPanel({ slot, first }: { slot: InstallerSlot; first: boolean }) {
  const [status, setStatus] = useState<AgentInstallerStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [fetching, setFetching] = useState(false);
  const [versionOverride, setVersionOverride] = useState('');
  const fileInputRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try { setStatus(await api.installerStatus(slot.platform)); }
    catch { /* non-fatal */ }
    finally { setLoading(false); }
  }, [slot.platform]);

  useEffect(() => { load(); }, [load]);

  async function handleUpload() {
    const file = fileInputRef.current?.files?.[0];
    if (!file) { alert(`Choose a ${slot.label} package first.`); return; }
    const ver = versionOverride.trim() || slot.parseVersion(file.name);
    if (!ver) { alert('Could not parse version from filename. Enter it in the Version field.'); return; }
    setUploading(true);
    try {
      const fd = new FormData();
      fd.append('file', file);
      await api.uploadInstaller(fd, ver, slot.platform);
      setVersionOverride('');
      if (fileInputRef.current) fileInputRef.current.value = '';
      await load();
    } catch (e) { alert('Upload failed: ' + (e as Error).message); }
    finally { setUploading(false); }
  }

  async function handleDelete() {
    if (!confirm(`Remove the hosted ${slot.label} package? Those agents will no longer be offered an update until a new one is uploaded.`)) return;
    try { await api.deleteInstaller(slot.platform); await load(); }
    catch (e) { alert('Delete failed: ' + (e as Error).message); }
  }

  async function handleFetchGitHub() {
    setFetching(true);
    try {
      const info = await api.fetchInstallerFromGitHub(slot.platform);
      await load();
      alert(`Fetched v${info.version} (${info.fileName}) from GitHub.`);
    } catch (e) { alert('Fetch from GitHub failed: ' + (e as Error).message); }
    finally { setFetching(false); }
  }

  return (
    <div style={{
      display: 'flex', flexDirection: 'column', gap: 10,
      borderTop: first ? undefined : '1px solid #2A3238',
      paddingTop: first ? 0 : 14,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 13, color: '#ECEFF1', fontWeight: 600, minWidth: 122 }}>{slot.label}</span>
        {loading ? (
          <span style={{ fontSize: 12, color: '#556070' }}>Loading…</span>
        ) : status ? (
          <>
            <span style={{ padding: '2px 7px', background: '#129271', color: '#fff', borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em' }}>v{status.version}</span>
            <span style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 11, color: '#9CA3AF' }}>{status.fileName}</span>
            <span style={{ fontSize: 11, color: '#556070' }}>·</span>
            <span style={{ fontSize: 11, color: '#556070' }}>{(status.sizeBytes / (1024 * 1024)).toFixed(1)} MB</span>
            <span style={{ fontSize: 11, color: '#556070' }}>· uploaded {new Date(asUtc(status.uploadedAt)).toLocaleDateString()}</span>
            <a
              href={`/api/agent/installer/download?platform=${slot.platform}`}
              style={{ fontSize: 11, color: '#129271', textDecoration: 'none' }}
              target="_blank" rel="noreferrer"
            >
              Download ↓
            </a>
            <button
              onClick={handleDelete}
              style={{ padding: '2px 10px', border: '1px solid #f4a60d', color: '#f4a60d', background: 'transparent', borderRadius: 4, fontSize: 11, cursor: 'pointer' }}
            >
              Delete
            </button>
          </>
        ) : (
          <span style={{ padding: '2px 7px', border: '1px solid #556070', color: '#556070', borderRadius: 4, fontSize: 10, fontWeight: 600 }}>none — these agents won't be offered updates</span>
        )}
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <input
          ref={fileInputRef}
          type="file"
          accept={slot.accept}
          aria-label={`${slot.label} agent package`}
          onChange={e => {
            const parsed = slot.parseVersion(e.target.files?.[0]?.name ?? '');
            if (parsed) setVersionOverride(parsed);
          }}
          style={{ flex: 1, minWidth: 200, padding: '5px 0', color: '#9CA3AF', fontSize: 12, background: 'transparent', border: 'none' }}
        />
        <input
          type="text"
          value={versionOverride}
          onChange={e => setVersionOverride(e.target.value)}
          placeholder="Version (e.g. 0.2.0)"
          aria-label={`${slot.label} package version`}
          style={{ width: 140, padding: '7px 10px', background: 'transparent', color: '#ECEFF1', border: '1px solid #494949', borderRadius: 5, fontSize: 12, fontFamily: "'JetBrains Mono', monospace" }}
        />
        <button
          onClick={handleUpload}
          disabled={uploading}
          style={{ padding: '6px 14px', background: uploading ? '#2A3238' : '#129271', color: uploading ? '#556070' : '#fff', border: 'none', borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: uploading ? 'default' : 'pointer', whiteSpace: 'nowrap' }}
        >
          {uploading ? 'Uploading…' : 'Upload'}
        </button>
        <button
          onClick={handleFetchGitHub}
          disabled={fetching}
          style={{ padding: '6px 14px', background: 'transparent', color: fetching ? '#556070' : '#129271', border: `1px solid ${fetching ? '#2A3238' : '#129271'}`, borderRadius: 5, fontSize: 12, fontWeight: 600, cursor: fetching ? 'default' : 'pointer', whiteSpace: 'nowrap' }}
        >
          {fetching ? 'Fetching…' : 'Fetch from GitHub'}
        </button>
      </div>
      <p style={{ fontSize: 11, color: '#9CA3AF', marginTop: -4 }}>
        <code style={{ fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{slot.fileHint}</code>
        {' '}from the release workflow — upload it, or pull it straight from the latest GitHub Release.
        The version is read from the filename. Connected {slot.label} agents are offered it at their next check-in.
      </p>
    </div>
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
