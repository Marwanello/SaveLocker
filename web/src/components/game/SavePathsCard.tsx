import { useEffect, useRef, useState } from 'react';
import { api } from '../../api';
import type { Game, GameIntent, Machine, MachineSavePath, MachineScanCandidate } from '../../types';
import { toTemplate } from '../../savePathTemplate';
import { card, cardHeader, sectionLabel, thStyle, tdStyle, tdMono, rowSep, ghostBtn } from './legacyStyles';

interface Props {
  game: Game;
  machines: Machine[];
  /** A deep link's "Set folder" lands here; `seq` makes each request act exactly once. */
  intent?: { value: GameIntent; seq: number } | null;
  onRefresh: () => void;
}

/** Each machine's stored save folder for this game — or its scan's unconfirmed guess — with edit,
 *  apply and "use as template". */
export function SavePathsCard({ game, machines, intent, onRefresh }: Props) {
  const [pathsLoaded, setPathsLoaded] = useState(false);
  const [machinePaths, setMachinePaths] = useState<MachineSavePath[]>([]);
  const [pathCandidates, setPathCandidates] = useState<MachineScanCandidate[]>([]);
  const [editingPathFor, setEditingPathFor] = useState<string | null>(null);
  const [pathDraft, setPathDraft] = useState('');

  useEffect(() => {
    api.getGamePaths(game.id).then(setMachinePaths).catch(() => {}).finally(() => setPathsLoaded(true));
    api.getGamePathCandidates(game.id).then(setPathCandidates).catch(() => {});
    setEditingPathFor(null);
  }, [game.id]);

  // A notification's "Set folder": that machine's row opens for editing (the field focuses itself),
  // once the stored paths are in so it starts from what is stored — or from the scan's guess.
  const appliedIntent = useRef(0);
  useEffect(() => {
    if (!intent || intent.seq === appliedIntent.current || intent.value.kind !== 'folder' || !pathsLoaded) return;
    appliedIntent.current = intent.seq;
    const m = intent.value.machineId;
    setPathDraft(machinePaths.find(p => p.machineId === m)?.savePath ?? pathCandidates.find(c => c.machineId === m)?.suggestedPath ?? '');
    setEditingPathFor(m);
  }, [intent, pathsLoaded, machinePaths, pathCandidates]);

  /**
   * Promote one machine's concrete path to the game's template, so every OTHER machine expands it
   * for itself instead of inheriting a path that means nothing on their filesystem.
   */
  async function handleUseAsTemplate(savePath: string, machineName: string) {
    const template = toTemplate(savePath);
    if (!template) {
      alert(
        `Can't turn this into a template:\n\n${savePath}\n\n` +
        `It isn't under a known folder (Documents, AppData, Public, Saved Games, or a Proton prefix), ` +
        `so there's no equivalent to point another machine at.`
      );
      return;
    }
    if (!confirm(
      `Set this game's save path template from ${machineName}?\n\n${template}\n\n` +
      `Each machine expands this against its own folders — on a Steam Deck, inside that game's ` +
      `Proton prefix. Machines that already have a path of their own keep it.`
    )) return;

    try { await api.setSaveDir(game.id, template); onRefresh(); }
    catch (e) { alert('Could not set the template: ' + (e as Error).message); }
  }

  async function reloadPaths() {
    setMachinePaths(await api.getGamePaths(game.id));
    // A stored path retires its candidate server-side, so refresh both together or the row keeps
    // offering a guess for a machine that is now mapped.
    setPathCandidates(await api.getGamePathCandidates(game.id).catch(() => []));
  }

  /** Write a path for one machine. Blank clears it. */
  async function saveMachinePath(machineId: string, path: string) {
    try {
      if (path.trim() === '') await api.clearMachinePath(game.id, machineId);
      else await api.setMachinePath(game.id, machineId, path.trim());
      setEditingPathFor(null);
      await reloadPaths();
    } catch (e) { alert('Could not update path: ' + (e as Error).message); }
  }

  return (
    <div style={card}>
      <div style={cardHeader}><span style={sectionLabel}>Save paths per machine</span></div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ background: 'var(--color-raise)' }}>
            <th style={thStyle}>Machine</th>
            <th style={thStyle}>Save folder</th>
            <th style={{ ...thStyle, width: 80 }}></th>
          </tr>
        </thead>
        <tbody>
          {machines.length === 0
            ? <tr><td colSpan={3} style={{ padding: '20px 18px', color: 'var(--color-dim)', fontSize: 13 }}>No machines registered.</td></tr>
            : machines.map(m => {
                const stored = machinePaths.find(p => p.machineId === m.id);
                const candidate = pathCandidates.find(c => c.machineId === m.id);
                const editing = editingPathFor === m.id;
                return (
                  <tr key={m.id} style={rowSep}>
                    <td style={tdStyle}>{m.name}</td>
                    <td style={tdMono}>
                      {editing ? (
                        // The label names the machine explicitly. The old prompt() said "Save
                        // folder for X" in a modal detached from the table, which made it easy to
                        // type a Deck path into a Windows machine's row.
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                          <label style={{ color: 'var(--color-dim)', fontSize: 11 }}>
                            Save path on <span style={{ color: 'var(--color-fg)', fontWeight: 600 }}>{m.name}</span>
                          </label>
                          <input
                            autoFocus
                            value={pathDraft}
                            onChange={e => setPathDraft(e.target.value)}
                            onKeyDown={e => {
                              if (e.key === 'Enter') void saveMachinePath(m.id, pathDraft);
                              if (e.key === 'Escape') setEditingPathFor(null);
                            }}
                            placeholder="Leave blank to clear the stored path"
                            style={{
                              background: 'var(--color-raise)', border: '1px solid var(--color-line)', borderRadius: 4,
                              padding: '6px 8px', color: 'var(--color-fg)', fontSize: 12,
                              fontFamily: 'ui-monospace, Consolas, monospace', outline: 'none',
                            }}
                          />
                        </div>
                      ) : stored ? (
                        stored.savePath
                      ) : candidate ? (
                        // The agent found this but has NOT adopted it — a human confirms here.
                        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                          <span style={{ color: 'var(--color-dim)', fontStyle: 'italic' }}>not set</span>
                          <span style={{ color: 'var(--color-dim)', fontSize: 11, fontStyle: 'normal' }}>
                            {m.name}'s scan found:
                          </span>
                          <span style={{ color: 'var(--color-fg)', fontSize: 12, wordBreak: 'break-all' }}>
                            {candidate.suggestedPath}
                          </span>
                        </div>
                      ) : (
                        <span style={{ color: 'var(--color-dim)', fontStyle: 'italic' }}>not set</span>
                      )}
                    </td>
                    <td style={{ padding: '11px 18px', whiteSpace: 'nowrap' }}>
                      {editing ? (
                        <div style={{ display: 'flex', gap: 5 }}>
                          <button
                            style={{ padding: '4px 10px', border: 'none', color: 'var(--color-on-accent)', background: 'var(--color-accent)', borderRadius: 4, fontSize: 11, cursor: 'pointer', fontWeight: 500 }}
                            onClick={() => void saveMachinePath(m.id, pathDraft)}
                          >Save</button>
                          <button style={ghostBtn()} onClick={() => setEditingPathFor(null)}>Cancel</button>
                        </div>
                      ) : (
                        <div style={{ display: 'flex', gap: 5 }}>
                          {!stored && candidate && (
                            <button
                              style={{ padding: '4px 10px', border: 'none', color: 'var(--color-on-accent)', background: 'var(--color-accent)', borderRadius: 4, fontSize: 11, cursor: 'pointer', fontWeight: 500 }}
                              onClick={() => void saveMachinePath(m.id, candidate.suggestedPath)}
                            >Apply</button>
                          )}
                          {stored && toTemplate(stored.savePath) && (
                            <button
                              style={ghostBtn()}
                              title={`Describe this location generically so other machines can find their own copy:\n${toTemplate(stored.savePath)}`}
                              onClick={() => void handleUseAsTemplate(stored.savePath, m.name)}
                            >Use as template</button>
                          )}
                          <button
                            style={ghostBtn()}
                            onClick={() => {
                              setPathDraft(stored?.savePath ?? candidate?.suggestedPath ?? '');
                              setEditingPathFor(m.id);
                            }}
                          >{stored ? 'Edit' : 'Set'}</button>
                        </div>
                      )}
                    </td>
                  </tr>
                );
              })
          }
        </tbody>
      </table>
    </div>
  );
}
