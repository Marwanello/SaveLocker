import { useState, useEffect, useCallback } from 'react'
import { RefreshCw, FolderOpen, FolderSearch, Cloud } from 'lucide-react'
import type { Candidate } from '../types'
import { api } from '../api'
import { useFolderPicker } from '../useFolderPicker'
import { PathBrowserModal } from './PathBrowserModal'

interface Props {
  onEnrolled: () => void
}

const BTN_BASE: React.CSSProperties = {
  display: 'flex', alignItems: 'center', gap: 5,
  padding: '5px 11px', background: 'transparent',
  border: '1px solid #494949', borderRadius: 4,
  color: '#ECEFF1', fontSize: 12, cursor: 'pointer',
  fontFamily: 'inherit',
}

export function AddGamesView({ onEnrolled }: Props) {
  const [candidates, setCandidates] = useState<Candidate[]>([])
  const [checked, setChecked] = useState<Set<number>>(new Set())
  const [hideSteamCloud, setHideSteamCloud] = useState(false)
  const [scanning, setScanning] = useState(false)
  const [enrolling, setEnrolling] = useState(false)
  const [status, setStatus] = useState('')
  const picker = useFolderPicker()

  const scan = useCallback(async (force = false) => {
    setScanning(true)
    setStatus('Scanning…')
    try {
      const result = force ? await api.rescan() : await api.candidates()
      setCandidates(result)
      setStatus(`Found ${result.length} candidate(s).`)
    } catch (e) {
      setStatus('Scan failed: ' + (e as Error).message)
    } finally {
      setScanning(false)
    }
  }, [])

  useEffect(() => { void scan(false) }, [scan])

  const toggle = (id: number) => {
    setChecked(prev => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })
  }

  // The native dialog (Windows tray) also writes the candidate cache server-side; on a headless
  // Deck it returns null and the browser opens inside the game's own Proton prefix. Either way the
  // chosen path is persisted with api.candidateFolder and reflected in local state.
  const pickFolderFor = (c: Candidate) => picker.pick({
    name: c.name,
    start: () => c.path || c.prefixPath || null,
    nativePick: () => api.candidateFolderPick(c.id),
    apply: async (path) => {
      await api.candidateFolder(c.id, path)
      setCandidates(prev => prev.map(x => x.id === c.id ? { ...x, path } : x))
    },
  })

  const setSaveFolder = () => {
    const ids = [...checked]
    if (ids.length !== 1) return
    const c = candidates.find(x => x.id === ids[0])
    if (c) void pickFolderFor(c)
  }

  // Enrolling a game with no save folder is what produced the silent Deck failures — it lands a
  // tracked game the archiver cannot back up. Named here so the block is actionable, not just off.
  const missing = [...checked]
    .map(id => candidates.find(c => c.id === id))
    .filter((c): c is Candidate => !!c && !c.path)

  const enroll = async () => {
    if (checked.size === 0 || missing.length > 0) return
    setEnrolling(true)
    setStatus('Enrolling…')
    try {
      const result = await api.enroll([...checked])
      setStatus(
        `Enrolled ${result.enrolled} game(s).` +
        (result.skipped > 0 ? ` Skipped ${result.skipped} already tracked.` : '')
      )
      setChecked(new Set())
      onEnrolled()
      await scan(false)
    } catch (e) {
      setStatus('Enroll failed: ' + (e as Error).message)
    } finally {
      setEnrolling(false)
    }
  }

  const busy = scanning || enrolling
  const visible = hideSteamCloud ? candidates.filter(c => !c.hasSteamCloud) : candidates
  const enrollBlocked = missing.length > 0
  const footerStatus = status || (
    enrollBlocked
      ? `Set a save folder for: ${missing.map(c => c.name).join(', ')}`
      : `Found ${visible.length} candidate(s).`
  )

  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', flexDirection: 'column',
      gap: 11, padding: '16px 20px', overflow: 'hidden',
    }}>
      <p style={{ color: '#9CA3AF', fontSize: 12, lineHeight: 1.65, flexShrink: 0 }}>
        Tick games to sync. Games without a known save folder need one set before enrolling.
      </p>

      {/* Toolbar */}
      <div style={{ display: 'flex', gap: 6, flexShrink: 0, flexWrap: 'wrap' }}>
        <button style={BTN_BASE} disabled={busy} onClick={() => void scan(true)}>
          <RefreshCw size={13} strokeWidth={1.75} color="#9CA3AF" />
          <span>Rescan</span>
        </button>
        <button
          style={{ ...BTN_BASE, opacity: checked.size !== 1 ? 0.45 : 1 }}
          disabled={busy || checked.size !== 1}
          onClick={() => setSaveFolder()}
        >
          <FolderOpen size={13} strokeWidth={1.75} color="#9CA3AF" />
          <span>Set save folder…</span>
        </button>
        <button
          onClick={() => setHideSteamCloud(h => !h)}
          style={{
            display: 'flex', alignItems: 'center', gap: 5, padding: '5px 11px',
            background: hideSteamCloud ? 'rgba(18,146,113,0.1)' : 'transparent',
            border: `1px solid ${hideSteamCloud ? '#129271' : '#494949'}`,
            borderRadius: 4,
            color: hideSteamCloud ? '#129271' : '#9CA3AF',
            fontSize: 12, cursor: 'pointer', fontFamily: 'inherit',
          }}
        >
          <Cloud size={13} strokeWidth={1.75} color={hideSteamCloud ? '#129271' : '#9CA3AF'} />
          <span>Hide Steam Cloud</span>
        </button>
      </div>

      {/* Game list */}
      <div style={{
        background: '#1E252A', border: '1px solid #494949', borderRadius: 6,
        overflowY: 'auto', flex: 1, minHeight: 0,
      }}>
        {visible.map(c => (
          <div
            key={c.id}
            style={{
              display: 'flex', alignItems: 'flex-start',
              padding: '10px 13px',
              borderBottom: '1px solid rgba(73,73,73,0.4)',
              gap: 10,
            }}
          >
            <input
              type="checkbox"
              checked={checked.has(c.id)}
              onChange={() => toggle(c.id)}
              style={{ marginTop: 2 }}
            />
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                <span style={{ color: '#ECEFF1', fontSize: 13, fontWeight: 500 }}>{c.name}</span>
                <span style={{
                  color: '#9CA3AF', fontSize: 10,
                  background: 'rgba(255,255,255,0.05)',
                  border: '1px solid rgba(255,255,255,0.09)',
                  padding: '1px 6px', borderRadius: 3,
                  fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
                }}>
                  {c.source}
                </span>
                {c.hasSteamCloud && (
                  <span style={{
                    color: '#60a5fa', fontSize: 10,
                    background: 'rgba(96,165,250,0.08)',
                    border: '1px solid rgba(96,165,250,0.22)',
                    padding: '1px 6px', borderRadius: 3,
                  }}>
                    Steam Cloud
                  </span>
                )}
              </div>
              {c.path ? (
                <div style={{
                  color: '#9CA3AF', fontSize: 10, marginTop: 3,
                  overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                  fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
                }}>
                  {c.path}
                </div>
              ) : (
                // The per-row button is what a Deck user actually hits — the toolbar button needs
                // a tick first, and this appears exactly on the rows that block enrollment.
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 3 }}>
                  <span style={{ color: '#f4a60d', fontSize: 11 }}>No save folder set</span>
                  <button
                    onClick={() => void pickFolderFor(c)}
                    style={{
                      display: 'flex', alignItems: 'center', gap: 5,
                      padding: '4px 9px', background: 'transparent',
                      border: '1px solid #129271', borderRadius: 4,
                      color: '#129271', fontSize: 11, fontWeight: 600,
                      cursor: 'pointer', fontFamily: 'inherit',
                    }}
                  >
                    <FolderSearch size={12} strokeWidth={1.75} />
                    <span>Set save folder</span>
                  </button>
                </div>
              )}
            </div>
          </div>
        ))}
      </div>

      {/* Footer */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <span style={{ color: enrollBlocked && !status ? '#f4a60d' : '#9CA3AF', fontSize: 12 }}>
          {footerStatus}
        </span>
        <button
          onClick={() => void enroll()}
          disabled={busy || checked.size === 0 || enrollBlocked}
          style={{
            padding: '7px 18px', background: '#129271', border: 'none', borderRadius: 5,
            color: '#fff', fontSize: 13, fontWeight: 600,
            cursor: checked.size > 0 && !busy && !enrollBlocked ? 'pointer' : 'default',
            fontFamily: 'inherit', letterSpacing: '0.01em',
            opacity: checked.size === 0 || busy || enrollBlocked ? 0.5 : 1,
          }}
        >
          Enroll selected
        </button>
      </div>

      {picker.browsing && (
        <PathBrowserModal
          gameName={picker.browsing.name}
          initialPath={picker.browsing.start}
          onConfirm={path => void picker.confirmBrowsed(path)}
          onCancel={picker.cancel}
        />
      )}
    </div>
  )
}
