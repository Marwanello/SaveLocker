import { useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { api } from '../api'
import { formatBytes } from '../format'
import { useActivityRecent } from '../useActivity'
import type { GamePlaythroughs, StartFile, SwitchPlaythroughResult, TrackedGame } from '../types'
import { Button } from './ui/Button'

/** The overlay every playthrough dialog sits in. Portalled to <body>: the page's entrance animation makes each of its children a
 *  stacking context, which would trap a fixed overlay under the cards after it. */
function Overlay({ label, onEscape, children }: { label: string; onEscape?: () => void; children: ReactNode }) {
  return createPortal(
    <div
      role="dialog" aria-modal="true" aria-label={label}
      onKeyDown={e => { if (e.key === 'Escape' && onEscape) { e.stopPropagation(); onEscape() } }}
      style={{
        position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
      }}
    >
      {children}
    </div>,
    document.body,
  )
}

/** The steps the agent logged for this game since the dialog opened — the switch's progress, as it happens. */
function useSteps(gameName: string, since: number): string[] {
  const recent = useActivityRecent()
  return useMemo(() => (recent ?? [])
    .filter(e => new Date(e.timestampUtc.endsWith('Z') ? e.timestampUtc : e.timestampUtc + 'Z').getTime() >= since - 1500)
    .map(e => e.message)
    .filter(m => m.startsWith(`[${gameName}]`))
    .map(m => m.slice(gameName.length + 3)), [recent, gameName, since])
}

interface RunProps {
  game: TrackedGame
  title: string
  /** What the dialog does while it shows progress; resolves with the result sentence. */
  run: () => Promise<SwitchPlaythroughResult>
  onDone: (result: SwitchPlaythroughResult) => void
}

/**
 * A switch with a real dialog (the explicit-dialog rule for async actions): what it is doing as it does it, then what became of
 * it — never a toast that scrolls past. The result stays until it is dismissed, and a refusal says why and that nothing changed.
 */
export function PlaythroughProgress({ game, title, run, onDone }: RunProps) {
  const started = useRef(Date.now())
  const [result, setResult] = useState<SwitchPlaythroughResult | null>(null)
  const steps = useSteps(game.name, started.current)
  const once = useRef(false)

  useEffect(() => {
    if (once.current) return
    once.current = true
    run()
      .then(setResult)
      .catch(err => setResult({ ok: false, message: err instanceof Error ? err.message : 'The switch failed.', offline: false }))
  // The dialog runs its action exactly once, on open.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <Overlay label={title} onEscape={result ? () => onDone(result) : undefined}>
      <div className="sl-ask">
        <div className="sl-ask__title">{title}</div>
        <div className="sl-ask__body sl-stack" style={{ gap: 10 }}>
          {!result ? (
            <>
              <p className="sl-ask__working">Working… this device keeps its current playthrough until the switch is done.</p>
              {steps.length > 0 && <ol className="sl-ask__steps">{steps.slice(-6).map((s, i) => <li key={i}>{s}</li>)}</ol>}
            </>
          ) : (
            <>
              <p className={result.ok ? 'sl-ask__working' : 'sl-ask__warn'}>{result.message}</p>
              {result.ok && result.offline && (
                <p className="sl-ask__note">The server could not be reached, so the save came from the copy this device keeps. It may be older than the server's.</p>
              )}
              {!result.ok && <p>Nothing was lost: this device is still on its own playthrough.</p>}
            </>
          )}
        </div>
        <div className="sl-ask__actions">
          <Button variant="primary" disabled={!result} onClick={() => result && onDone(result)} data-pad-label="Close">
            {result ? 'Done' : 'Working…'}
          </Button>
        </div>
      </div>
    </Overlay>
  )
}

interface NewProps {
  game: TrackedGame
  data: GamePlaythroughs
  /** Fork this version instead of a playthrough's latest ("New playthrough from this version"). */
  fromVersion?: { id: string; label: string } | null
  onClose: () => void
  onDone: (result: SwitchPlaythroughResult) => void
}

/**
 * New playthrough: a name, then one of two starts. <b>Current save</b> forks a playthrough's latest save (nothing is uploaded
 * again — the new line points at the version it came from). <b>Start fresh</b> empties this device's save folder once the
 * current save is safe on the server, keeping the files ticked. "Play it on this device now" switches to it at once.
 */
export function NewPlaythroughDialog({ game, data, fromVersion, onClose, onDone }: NewProps) {
  const live = data.playthroughs.filter(p => !p.archivedAt)
  const [name, setName] = useState('')
  const [fresh, setFresh] = useState(false)
  const [from, setFrom] = useState<string | null>(data.selectedId ?? live[0]?.id ?? null)
  const [playHere, setPlayHere] = useState(true)
  const [files, setFiles] = useState<StartFile[] | null>(null)
  const [keep, setKeep] = useState<Set<string>>(new Set())
  const [running, setRunning] = useState(false)
  const taken = data.playthroughs.some(p => p.name.trim().toLowerCase() === name.trim().toLowerCase())
  const valid = name.trim().length > 0 && name.trim().length <= 60 && !taken

  useEffect(() => {
    if (!fresh || files) return
    let alive = true
    api.startFiles(game.id)
      .then(f => { if (alive) { setFiles(f); setKeep(new Set(f.filter(x => x.keepByDefault).map(x => x.path))) } })
      .catch(() => { if (alive) setFiles([]) })
    return () => { alive = false }
  }, [fresh, files, game.id])

  if (running) {
    return (
      <PlaythroughProgress
        game={game} title={`New playthrough · ${name.trim()}`} onDone={onDone}
        run={() => api.newPlaythrough(game.id, {
          name: name.trim(),
          fromPlaythroughId: fresh || fromVersion ? null : from,
          startFresh: fresh && !fromVersion,
          keepFiles: fresh && !fromVersion ? [...keep] : null,
          playHere,
          fromVersionId: fromVersion?.id ?? null,
        })}
      />
    )
  }

  return (
    <Overlay label="New playthrough" onEscape={onClose}>
      <div className="sl-ask sl-ask--wide">
        <div className="sl-ask__title">New playthrough of {game.name}</div>
        <div className="sl-ask__body sl-stack" style={{ gap: 12 }}>
          <div className="sl-field">
            <label htmlFor="pt-name">Name</label>
            <input id="pt-name" className="sl-input" autoFocus maxLength={60} value={name} placeholder="Hard mode, Sara's island…"
              onChange={e => setName(e.target.value)} />
            {taken && <small className="sl-ask__warn">This game already has a playthrough called that.</small>}
          </div>

          {fromVersion ? (
            <p className="sl-ask__note">It starts from <b>{fromVersion.label}</b>. Nothing is uploaded again.</p>
          ) : (
            <>
              <div className="sl-ask__label">Start from</div>
              <div className="sl-plays" role="radiogroup" aria-label="How the playthrough starts">
                <button type="button" role="radio" className="sl-plays__opt" aria-checked={!fresh} onClick={() => setFresh(false)}
                  data-pad-label="Start from the current save">
                  <span className="sl-plays__radio" aria-hidden="true" />
                  <span className="sl-plays__txt">
                    <b>Current save</b>
                    <span>A copy of a playthrough's latest save. Nothing is uploaded again.</span>
                  </span>
                </button>
                <button type="button" role="radio" className="sl-plays__opt" aria-checked={fresh} onClick={() => setFresh(true)}
                  data-pad-label="Start fresh">
                  <span className="sl-plays__radio" aria-hidden="true" />
                  <span className="sl-plays__txt">
                    <b>Start fresh</b>
                    <span>An empty save folder, keeping only the files you tick.</span>
                  </span>
                </button>
              </div>

              {!fresh && live.length > 1 && (
                <div className="sl-vpick" role="radiogroup" aria-label="Copy the latest save of">
                  {live.map(p => (
                    <button key={p.id} type="button" role="radio" aria-checked={p.id === from} onClick={() => setFrom(p.id)}
                      data-pad-label={`Copy ${p.name}`} disabled={!p.head}>
                      <i aria-hidden="true" />
                      <span><b>{p.name}</b>{p.id === data.selectedId ? ' · played here' : ''}{p.head ? '' : ' · no save yet'}</span>
                    </button>
                  ))}
                </div>
              )}

              {fresh && (
                <>
                  <p>
                    Your current save of {game.name} goes to the server first, in {data.selectedName ?? 'its playthrough'}. Then this
                    device's save folder is emptied, except what you keep:
                  </p>
                  {files === null && <p>Looking at the save folder…</p>}
                  {files?.length === 0 && <p>The save folder has no files to keep.</p>}
                  {files && files.length > 0 && (
                    <div className="sl-vpick" aria-label="Files to keep">
                      {files.map(f => (
                        <label key={f.path} className="sl-keeprow">
                          <input type="checkbox" checked={keep.has(f.path)} data-pad-label={`Keep ${f.path}`}
                            onChange={() => setKeep(prev => { const n = new Set(prev); if (n.has(f.path)) n.delete(f.path); else n.add(f.path); return n })} />
                          <span className="sl-mono">{f.path}</span>
                          <small>{formatBytes(f.size)}</small>
                        </label>
                      ))}
                    </div>
                  )}
                </>
              )}
            </>
          )}

          <label className="sl-keeprow">
            <input type="checkbox" checked={playHere} onChange={e => setPlayHere(e.target.checked)} data-pad-label="Play it on this device now" />
            <span>Play it on this device now</span>
          </label>
        </div>
        <div className="sl-ask__actions">
          <Button variant="quiet" onClick={onClose} data-pad-label="Cancel">Cancel</Button>
          <Button variant="primary" disabled={!valid || (!fresh && !fromVersion && !from)} onClick={() => setRunning(true)} data-pad-label="Create playthrough">
            {playHere ? 'Create and play' : 'Create'}
          </Button>
        </div>
      </div>
    </Overlay>
  )
}
