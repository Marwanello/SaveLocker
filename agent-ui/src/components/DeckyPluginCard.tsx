import { useEffect, useState } from 'react'
import { Puzzle, Copy, Check, ChevronDown, ChevronRight } from 'lucide-react'
import { api } from '../api'
import { copyText } from '../clipboard'

/** The one-paste URL for Decky's "Install Plugin from URL". Kept in step with DeckyPlugin.cs. */
const INSTALL_URL =
  'https://github.com/SkorcherX/SaveLocker-Decky/releases/latest/download/SaveLocker.zip'

/**
 * What the optional Decky plugin adds, and how to get it.
 *
 * <p>Gated on the same signal as {@link LaunchSetupCard} — it self-fetches the launch command and
 * renders nothing when there is none, i.e. on Windows, where Decky does not exist.</p>
 *
 * <p><b>Collapsed by default, and that is the point.</b> This sits under the launch-options card,
 * which is the one a user actually needs; expanded it was taller than everything above it combined
 * and pushed the real instructions off a Deck's screen. Collapsed it is one line that says the
 * option exists — which is all it has to do for someone who does not use Decky, i.e. most people.
 * The detail is one click away for anyone it applies to.</p>
 *
 * <p>Deliberately placed after the launch-options card rather than instead of it: the copy-paste
 * path is the supported one and a Deck without Decky loses nothing (Decisions: Decky is an
 * accelerator, never the supported path).</p>
 *
 * <p>It is static text rather than live state: reporting "installed / not installed / v0.2.0" here
 * would need a new route on the agent's own API, and that means regenerating
 * <code>agent-ui/src/api-types.ts</code> against a running dev daemon on :5178 — the port an
 * installed agent already holds. Worth doing; not worth stopping someone's running agent for.
 * <code>savelocker doctor</code> already reports the live state.</p>
 */
export function DeckyPluginCard() {
  const [hasLaunchCommand, setHasLaunchCommand] = useState(false)
  const [expanded, setExpanded] = useState(false)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    api.launchCommand().then(i => setHasLaunchCommand(!!i?.command)).catch(() => {})
  }, [])

  if (!hasLaunchCommand) return null

  const copy = async () => {
    if (await copyText(INSTALL_URL)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  const Chevron = expanded ? ChevronDown : ChevronRight

  return (
    <div style={{
      background: '#1E252A', border: '1px solid #494949', borderRadius: 8,
      padding: '12px 16px', display: 'flex', flexDirection: 'column', gap: expanded ? 10 : 6,
      textAlign: 'left', width: '100%', maxWidth: 560,
    }}>
      {/* The whole header is the toggle — a small chevron alone is a poor target on a Deck. */}
      <button
        onClick={() => setExpanded(v => !v)}
        aria-expanded={expanded}
        style={{
          display: 'flex', alignItems: 'center', gap: 8, width: '100%',
          background: 'transparent', border: 'none', padding: 0,
          cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
        }}
      >
        <Puzzle size={16} strokeWidth={1.9} color="#129271" style={{ flexShrink: 0 }} />
        <span style={{ color: '#ECEFF1', fontSize: 13, fontWeight: 700 }}>Decky plugin</span>
        <span style={{
          padding: '1px 7px', border: '1px solid #556070', color: '#9CA3AF',
          borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em', flexShrink: 0,
        }}>
          OPTIONAL
        </span>
        <span style={{
          marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 3,
          color: '#129271', fontSize: 11.5, fontWeight: 600, flexShrink: 0,
        }}>
          {expanded ? 'Show less' : 'Learn more'}
          <Chevron size={14} strokeWidth={2} color="#129271" />
        </span>
      </button>

      <p style={{ color: '#9CA3AF', fontSize: 12, lineHeight: 1.55, margin: 0 }}>
        Use <strong style={{ color: '#ECEFF1' }}>Decky Loader</strong>? A plugin can set the launch
        options above for you and add sync controls to Game Mode.
        {!expanded && ' Everything here works without it.'}
      </p>

      {expanded && (
        <>
          <ul style={{
            color: '#9CA3AF', fontSize: 12, lineHeight: 1.6, margin: 0, paddingLeft: 18,
            display: 'flex', flexDirection: 'column', gap: 3,
          }}>
            <li>
              <strong style={{ color: '#ECEFF1' }}>Sets launch options for you.</strong> The agent
              cannot — Steam rewrites its own config on exit, so only something running inside Steam
              can. It also repairs a short <code style={{ fontSize: 11 }}>savelocker</code> to the full
              path, and merges with <code style={{ fontSize: 11 }}>mangohud</code> or any arguments you
              already have rather than replacing them.
            </li>
            <li>
              <strong style={{ color: '#ECEFF1' }}>Warns you in Game Mode</strong> when another machine
              has a game checked out — before you launch it and cause a conflict.
            </li>
            <li>
              <strong style={{ color: '#ECEFF1' }}>Push, pull and doctor from the Quick Access panel</strong>,
              per game or all at once, without leaving Game Mode for the desktop.
            </li>
          </ul>

          <p style={{ color: '#9CA3AF', fontSize: 12, lineHeight: 1.55, margin: 0 }}>
            Install it once from Decky &rarr; <strong style={{ color: '#ECEFF1' }}>Install Plugin from URL</strong> with
            the link below. After that this agent keeps it updated by itself — you do not need Decky's
            custom-store setting, which would replace your official store while it is set.
          </p>

          <div style={{ display: 'flex', gap: 6, alignItems: 'stretch' }}>
            <code style={{
              flex: 1, minWidth: 0, background: '#12181C', border: '1px solid #494949', borderRadius: 5,
              padding: '8px 10px', color: '#ECEFF1', fontSize: 11.5, lineHeight: 1.4,
              overflowX: 'auto', whiteSpace: 'nowrap',
              fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
            }}>
              {INSTALL_URL}
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

          <p style={{ color: '#556070', fontSize: 11.5, lineHeight: 1.5, margin: 0 }}>
            Already installed? Nothing to do — <code style={{ fontSize: 11 }}>savelocker doctor</code> reports
            its version and whether it is up to date.
          </p>
        </>
      )}
    </div>
  )
}
