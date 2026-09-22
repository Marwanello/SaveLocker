import { useEffect, useState } from 'react'
import { Puzzle, Copy, Check, ChevronDown, ChevronRight, Download } from 'lucide-react'
import { api } from '../api'
import { copyText } from '../clipboard'
import type { PlaynitePluginCardStatus } from '../types'

/**
 * The optional Playnite plugin: what it adds, and a way to get it — either a manual `.pext` download
 * or, unlike the Decky card below, a one-click automatic install (tasks/playnite-plugin/plan.md,
 * Phase 19).
 *
 * <p>Everything comes from <code>/api/playnite-plugin/status</code>. Unlike {@link DeckyPluginCard},
 * which still renders when Decky itself is absent (Decky is a near-universal Deck accelerator worth
 * pointing a Deck user at), this card renders <b>nothing at all</b> when Playnite isn't detected on
 * this machine — Playnite is a much more optional, minority choice on a Windows desktop, and a card
 * explaining a launcher the user doesn't have is clutter, not a nudge.</p>
 *
 * <p>The install button is a deliberate, explicit opt-in — it never fires on its own from a poll or a
 * timer, only from this one click. Playnite's Extensions folder carries none of Decky's root-owned
 * directory constraint, so a first install is technically no different from an update; what stays
 * intentional is the consent boundary, not the mechanism.</p>
 */
export function PlaynitePluginCard() {
  const [status, setStatus] = useState<PlaynitePluginCardStatus | null>(null)
  const [expanded, setExpanded] = useState(false)
  const [copied, setCopied] = useState(false)
  const [installing, setInstalling] = useState(false)
  const [installMessage, setInstallMessage] = useState<string | null>(null)

  const refresh = () => api.playnitePluginCardStatus().then(setStatus).catch(() => {})

  useEffect(() => { refresh() }, [])

  if (!status?.applicable) return null

  const installed = status.pluginInstalled
  const version = status.pluginVersion

  const copy = async () => {
    if (await copyText(status.installUrl)) {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }

  const install = async () => {
    setInstalling(true)
    setInstallMessage(null)
    try {
      const result = await api.installPlaynitePlugin()
      setInstallMessage(result.message)
      await refresh()
    } catch (err) {
      setInstallMessage(err instanceof Error ? err.message : 'Install failed.')
    } finally {
      setInstalling(false)
    }
  }

  const Chevron = expanded ? ChevronDown : ChevronRight

  const summary = installed
    ? <>The plugin is set up. It gates launches on unresolved conflicts and adds a "Link to
        SaveLocker" popup for matching games — this agent keeps it updated.</>
    : <>A Playnite plugin can block launching a game with an unresolved conflict, and offers a
        one-click link for games it can't match automatically.</>

  return (
    <div style={{
      background: 'var(--color-panel)', border: '1px solid var(--color-line)', borderRadius: 8,
      padding: '12px 16px', display: 'flex', flexDirection: 'column', gap: expanded ? 10 : 6,
      textAlign: 'left', width: '100%', maxWidth: 560,
    }}>
      <button
        onClick={() => setExpanded(v => !v)}
        aria-expanded={expanded}
        style={{
          display: 'flex', alignItems: 'center', gap: 8, width: '100%',
          background: 'transparent', border: 'none', padding: 0,
          cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
        }}
      >
        <Puzzle size={16} strokeWidth={1.9} color="var(--color-fg)" style={{ flexShrink: 0 }} />
        <span style={{ color: 'var(--color-fg)', fontSize: 13, fontWeight: 700 }}>Playnite plugin</span>
        {installed ? (
          <span style={{
            padding: '1px 7px', background: 'var(--color-safe-soft)', color: 'var(--color-safe-ink)',
            borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em', flexShrink: 0,
          }}>
            {version ? `INSTALLED v${version}` : 'INSTALLED'}
          </span>
        ) : (
          <span style={{
            padding: '1px 7px', border: '1px solid var(--color-line)', color: 'var(--color-dim)',
            borderRadius: 4, fontSize: 10, fontWeight: 600, letterSpacing: '0.04em', flexShrink: 0,
          }}>
            OPTIONAL
          </span>
        )}
        <span style={{
          marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: 3,
          color: 'var(--color-safe-ink)', fontSize: 11.5, fontWeight: 600, flexShrink: 0,
        }}>
          {expanded ? 'Show less' : 'Learn more'}
          <Chevron size={14} strokeWidth={2} color="var(--color-safe-ink)" />
        </span>
      </button>

      <p style={{ color: 'var(--color-dim)', fontSize: 12, lineHeight: 1.55, margin: 0 }}>
        {summary}
        {!expanded && !installed && ' Everything here works without it.'}
      </p>

      {expanded && (
        <>
          <ul style={{
            color: 'var(--color-dim)', fontSize: 12, lineHeight: 1.6, margin: 0, paddingLeft: 18,
            display: 'flex', flexDirection: 'column', gap: 3,
          }}>
            <li>
              <strong style={{ color: 'var(--color-fg)' }}>Blocks a launch</strong> only when this game has a
              genuinely unresolved conflict — never for lock contention or a network hiccup, which
              still just warn.
            </li>
            <li>
              <strong style={{ color: 'var(--color-fg)' }}>"Link to SaveLocker"</strong> — a one-click match
              for a game the automatic name-matching chain couldn't place on its own.
            </li>
            <li>
              <strong style={{ color: 'var(--color-fg)' }}>Status and sync menu items</strong> for the game
              currently selected, without leaving Playnite.
            </li>
          </ul>

          {installed ? (
            <p style={{ color: 'var(--color-dim)', fontSize: 12, lineHeight: 1.55, margin: 0 }}>
              Nothing to do — this agent replaces the plugin's files when a newer version is
              published; restart Playnite afterward to pick it up.
              {status.latestVersion && status.latestVersion !== version && (
                <> A newer version (v{status.latestVersion}) is already waiting.</>
              )}
            </p>
          ) : (
            <>
              <p style={{ color: 'var(--color-dim)', fontSize: 12, lineHeight: 1.55, margin: 0 }}>
                Install it automatically below, or download the <code style={{ fontSize: 11 }}>.pext</code> and
                double-click it to install by hand. Either way this agent keeps it updated afterward.
              </p>

              <div style={{ display: 'flex', gap: 6, alignItems: 'stretch', flexWrap: 'wrap' }}>
                <button
                  onClick={() => void install()}
                  disabled={installing}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0,
                    padding: '7px 14px', background: installing ? 'var(--color-raise)' : 'var(--color-accent)',
                    border: 'none', borderRadius: 5,
                    color: 'var(--color-on-accent)', fontSize: 12, fontWeight: 600,
                    cursor: installing ? 'default' : 'pointer', fontFamily: 'inherit',
                    opacity: installing ? 0.75 : 1,
                  }}
                >
                  <Download size={13} strokeWidth={2} />
                  <span>{installing ? 'Installing…' : 'Install automatically'}</span>
                </button>

                <code style={{
                  flex: 1, minWidth: 0, background: 'var(--color-raise)', border: '1px solid var(--color-line)', borderRadius: 5,
                  padding: '8px 10px', color: 'var(--color-fg)', fontSize: 11.5, lineHeight: 1.4,
                  overflowX: 'auto', whiteSpace: 'nowrap',
                  fontFamily: "ui-monospace, 'Cascadia Code', Consolas, monospace",
                }}>
                  {status.installUrl}
                </code>
                <button
                  onClick={() => void copy()}
                  style={{
                    display: 'flex', alignItems: 'center', gap: 5, flexShrink: 0,
                    padding: '0 12px', background: 'transparent',
                    border: `1px solid ${copied ? 'var(--color-line)' : 'var(--color-line)'}`, borderRadius: 5,
                    color: copied ? 'var(--color-fg)' : 'var(--color-fg)', fontSize: 12, fontWeight: 600,
                    cursor: 'pointer', fontFamily: 'inherit',
                  }}
                >
                  {copied
                    ? <><Check size={13} strokeWidth={2} /><span>Copied</span></>
                    : <><Copy size={13} strokeWidth={1.9} /><span>Copy link</span></>}
                </button>
              </div>

              {installMessage && (
                <p style={{ color: 'var(--color-dim)', fontSize: 11.5, lineHeight: 1.5, margin: 0 }}>
                  {installMessage}
                </p>
              )}
            </>
          )}
        </>
      )}
    </div>
  )
}
