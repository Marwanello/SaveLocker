import { useEffect, useState } from 'react'
import { Gamepad2, Copy, Check, AlertTriangle } from 'lucide-react'
import { api } from '../api'
import { copyText } from '../clipboard'

/**
 * The Steam launch-options command with a Copy button. Self-fetches and renders nothing when the
 * host has no command to offer (Windows — the tray sets up sync through the installer), so callers
 * can drop it in unconditionally.
 *
 * It is only load-bearing for the FIRST game on a device: the command is identical for every game,
 * so once one is set up the user copies it from that game's launch options into the next.
 */
export function LaunchSetupCard() {
  const [info, setInfo] = useState<{ command: string | null; note: string | null } | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => { api.launchCommand().then(setInfo).catch(() => {}) }, [])

  if (!info || !info.command) return null
  const command = info.command

  const copy = async () => {
    if (await copyText(command)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  return (
    <div style={{
      background: '#1E252A', border: '1px solid #494949', borderRadius: 8,
      padding: '14px 16px', display: 'flex', flexDirection: 'column', gap: 10,
      textAlign: 'left', width: '100%', maxWidth: 560,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <Gamepad2 size={16} strokeWidth={1.9} color="#129271" />
        <span style={{ color: '#ECEFF1', fontSize: 13, fontWeight: 700 }}>Steam launch setup</span>
      </div>

      <p style={{ color: '#9CA3AF', fontSize: 12, lineHeight: 1.55, margin: 0 }}>
        Paste this into a game's <strong style={{ color: '#ECEFF1' }}>Properties → Launch Options</strong> in
        Steam. It is the <strong style={{ color: '#ECEFF1' }}>same command for every game</strong> — set one up,
        then copy it from that game's launch options into the next. You only need this card for the first game.
      </p>

      <div style={{ display: 'flex', gap: 6, alignItems: 'stretch' }}>
        <code style={{
          flex: 1, minWidth: 0, background: '#12181C', border: '1px solid #494949', borderRadius: 5,
          padding: '8px 10px', color: '#ECEFF1', fontSize: 11.5, lineHeight: 1.4,
          overflowX: 'auto', whiteSpace: 'nowrap',
          fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
        }}>
          {command}
        </code>
        <button
          onClick={() => void copy()}
          style={{
            display: 'flex', alignItems: 'center', gap: 5, flexShrink: 0,
            padding: '0 12px', background: 'transparent',
            border: `1px solid ${copied ? '#129271' : '#494949'}`, borderRadius: 5,
            color: copied ? '#129271' : '#ECEFF1', fontSize: 12, fontWeight: 600,
            cursor: 'pointer', fontFamily: 'inherit',
          }}
        >
          {copied
            ? <><Check size={13} strokeWidth={2} /><span>Copied</span></>
            : <><Copy size={13} strokeWidth={1.9} /><span>Copy</span></>}
        </button>
      </div>

      <div style={{
        display: 'flex', gap: 8, alignItems: 'flex-start',
        background: 'rgba(244,166,13,0.1)', border: '1px solid rgba(244,166,13,0.35)',
        borderRadius: 6, padding: '9px 11px',
      }}>
        <AlertTriangle size={14} strokeWidth={2} color="#f4a60d" style={{ flexShrink: 0, marginTop: 1 }} />
        <div style={{ color: '#ECEFF1', fontSize: 11.5, lineHeight: 1.55 }}>
          Use the <strong style={{ color: '#f4a60d' }}>full path above</strong> — Game Mode does not put
          <code style={{ fontSize: 11 }}> ~/.local/bin </code> on PATH, so a short command silently fails to
          launch. For a <strong style={{ color: '#f4a60d' }}>non-Steam shortcut</strong>, also tick
          <strong> "Force the use of a specific Steam Play compatibility tool"</strong> in its properties, or
          Proton never creates a prefix and there is nothing to sync.
        </div>
      </div>

      {info.note && (
        <p style={{ color: '#f4a60d', fontSize: 11.5, lineHeight: 1.5, margin: 0 }}>{info.note}</p>
      )}
    </div>
  )
}
