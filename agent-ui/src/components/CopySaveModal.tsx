import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api'
import type { CopyTarget, SaveVersion, TrackedGame } from '../types'
import { formatAgo, formatBytes } from '../format'
import { emulatorIcon } from '../platformIcon'
import { emulatorOf } from '../titles'
import { Button } from './ui/Button'
import { PlatformIcon } from './ui/PlatformIcon'

interface Props {
  game: TrackedGame
  versions: SaveVersion[]
  /** The version to start with: the one whose "Copy to…" was pressed, or the Latest. */
  versionId: string
  /** The tree's Latest, to mark it in the list. */
  headId: string | null
  /** Every tree of the title, for which of them is set up here. */
  trees: TrackedGame[]
  onClose: () => void
  /** The copy is made: the target tree's id and a sentence saying what happened. */
  onCopied: (targetId: string, message: string) => void
}

/**
 * Copy this save to another emulator (tasks/multi-emulator-games Phase 6). The server offers it only between emulators
 * whose formats match, makes the copy a new version of the other tree — its old Latest kept — and tells every device
 * that plays it there to pull. This tree's own save is not touched.
 */
export function CopySaveModal({ game, versions, versionId, headId, trees, onClose, onCopied }: Props) {
  const usable = versions.filter(v => !v.mergedFrom)
  const [picked, setPicked] = useState(versionId)
  const [targets, setTargets] = useState<CopyTarget[] | null>(null)
  const [target, setTarget] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const emulator = emulatorOf(game) ?? game.name

  useEffect(() => {
    let live = true
    setTargets(null)
    api.copyTargets(game.id, picked)
      .then(t => {
        if (!live) return
        setTargets(t)
        setTarget(prev => t.some(x => x.gameId === prev && !x.refusal) ? prev : t.find(x => !x.refusal)?.gameId ?? null)
      })
      .catch(err => { if (live) setError(err instanceof Error ? err.message : 'Could not reach the server.') })
    return () => { live = false }
  }, [game.id, picked])

  const chosen = targets?.find(t => t.gameId === target) ?? null
  const chosenHere = !!trees.find(t => t.id === target)?.path
  const head = chosen?.targetHead ?? null

  async function copy() {
    if (!chosen) return
    setBusy(true)
    setError(null)
    try {
      await api.copyToEmulator(game.id, picked, chosen.gameId)
      const name = chosen.emulator ?? chosen.gameName
      onCopied(chosen.gameId, `Copied ${emulator}'s save to ${name}. It is ${name}'s Latest now` +
        `${chosenHere ? '; this device pulls it on its next sync' : ''}.`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not copy the save.')
      setBusy(false)
    }
  }

  // Portalled to <body>: the page's entrance animation makes each of its children a stacking context, which would
  // trap a fixed overlay under the cards after it.
  return createPortal(
    <div
      role="dialog" aria-modal="true" aria-label={`Copy a ${emulator} save to another emulator`}
      onKeyDown={e => { if (e.key === 'Escape' && !busy) { e.stopPropagation(); onClose() } }}
      style={{
        position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
      }}
    >
      <div className="sl-ask sl-ask--wide">
        <div className="sl-ask__title sl-inline">
          <PlatformIcon id={emulatorIcon(emulator)} size={1.2} />Copy a {emulator} save to another emulator
        </div>
        <div className="sl-ask__body sl-stack" style={{ gap: 12 }}>
          {busy ? (
            <p className="sl-ask__working">Making a new {chosen?.emulator} version from this save on the server…</p>
          ) : (
            <>
              <div className="sl-ask__label">Copy</div>
              <div className="sl-vpick" role="radiogroup" aria-label="Version to copy">
                {usable.slice(0, 6).map(v => (
                  <button key={v.id} type="button" role="radio" aria-checked={v.id === picked} onClick={() => setPicked(v.id)}
                    data-pad-label={`Copy the version from ${formatAgo(v.createdAt)}`}>
                    <i aria-hidden="true" />
                    <span><b>{formatAgo(v.createdAt)}</b> · from {v.machineName}{v.id === headId ? ' · Latest' : ''}</span>
                    <small className="sl-mono">{v.id.slice(0, 4)} · {formatBytes(v.size)}</small>
                  </button>
                ))}
              </div>

              <div className="sl-ask__label">To</div>
              {targets === null && !error && <p>Asking the server…</p>}
              {targets?.length === 0 && <p>No other emulator's tree of this game is on the server.</p>}
              {targets && targets.length > 0 && (
                <div className="sl-plays" role="radiogroup" aria-label="Emulator to copy to">
                  {targets.map(t => {
                    const here = !!trees.find(x => x.id === t.gameId)?.path
                    return (
                      <button key={t.gameId} type="button" role="radio" className="sl-plays__opt"
                        aria-checked={t.gameId === target} disabled={!!t.refusal} onClick={() => setTarget(t.gameId)}
                        data-pad-label={`Copy to ${t.emulator ?? t.gameName}`}>
                        <span className="sl-plays__radio" aria-hidden="true" />
                        <span className="sl-plays__txt">
                          <b className="sl-inline"><PlatformIcon id={emulatorIcon(t.emulator)} size={1} />{t.emulator ?? t.gameName}</b>
                          {t.refusal
                            ? <span className="sl-plays__line--info">{t.refusal}</span>
                            : <span className="sl-plays__line--ok">{t.how}</span>}
                          <span>{here ? 'Set up on this device' : 'Not set up on this device'}
                            {t.targetHead ? ` · latest ${formatAgo(t.targetHead.createdAt)}` : ' · no save yet'}</span>
                        </span>
                      </button>
                    )
                  })}
                </div>
              )}

              {chosen && (
                <>
                  <div className="sl-ask__label">Files</div>
                  <div className="sl-map">
                    {chosen.files.map(f => (
                      <div key={f.from}><span>{f.from}</span><span className="sl-map__arrow">→</span><span className="sl-map__to">{f.to}</span><span>{formatBytes(f.size)}</span></div>
                    ))}
                  </div>
                  <p className="sl-ask__note">
                    {head
                      ? <>{chosen.emulator}'s latest save ({formatAgo(head.createdAt)}, from {head.machineName}) is <b>kept as an older version</b>, so you can restore it. </>
                      : <>{chosen.emulator} has no save on the server yet. </>}
                    The copy becomes {chosen.emulator}'s new Latest, and every device that plays it in {chosen.emulator} pulls it on its next sync.
                    {' '}<b>{emulator}'s save is not changed.</b>
                  </p>
                </>
              )}
              {error && <p className="sl-ask__warn">{error}</p>}
            </>
          )}
        </div>
        <div className="sl-ask__actions">
          <Button variant="quiet" disabled={busy} onClick={onClose} data-pad-label="Cancel">Cancel</Button>
          <Button variant="primary" disabled={busy || !chosen} onClick={() => void copy()} data-pad-label="Copy the save">
            {chosen ? `Copy to ${chosen.emulator ?? chosen.gameName}` : 'Copy'}
          </Button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
