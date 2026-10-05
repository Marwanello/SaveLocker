import { useMemo, useState } from 'react'
import { api, ApiError } from '../api'
import type { FolderSuggestion } from '../types'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

interface Props {
  suggestions: FolderSuggestion[]
  /** Something was added: the shell re-reads the game list. */
  onChanged: () => void
  onDone: () => void
}

/**
 * "SaveLocker found more save folders" — the start-up question for games tracked before an agent could
 * sync more than one folder (tasks/multiple-save-paths plan §8). One game at a time over the whole
 * shell, the way the sync-time conflict queue (`SyncConflictModal`) works. Every folder starts ticked;
 * what is left unticked, and every folder of a skipped game, is "Skip for now": it stays on the game's
 * page and is not asked about again. Nothing here ever adds a folder the user did not leave ticked.
 */
export function FolderSuggestionsModal({ suggestions, onChanged, onDone }: Props) {
  const games = useMemo(() => {
    const byGame = new Map<string, { gameId: string; gameName: string; items: FolderSuggestion[] }>()
    for (const s of suggestions) {
      const g = byGame.get(s.gameId) ?? { gameId: s.gameId, gameName: s.gameName, items: [] }
      g.items.push(s)
      byGame.set(s.gameId, g)
    }
    return [...byGame.values()]
  }, [suggestions])

  const [index, setIndex] = useState(0)
  const [off, setOff] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)

  if (index >= games.length) return null
  const game = games[index]

  function next() {
    setOff(new Set())
    setProblem(null)
    if (index + 1 >= games.length) onDone()
    else setIndex(index + 1)
  }

  async function defer(paths: string[]) {
    if (paths.length > 0) await api.answerFolderSuggestions(game.gameId, { defer: paths }).catch(() => {})
  }

  async function addTicked() {
    setBusy(true)
    const failed: string[] = []
    const left: string[] = []
    try {
      for (const s of game.items) {
        if (off.has(s.path)) { left.push(s.path); continue }
        try {
          await api.addGameFolder(s.gameId, { path: s.path, key: s.key, freeKey: true })
        } catch (e) {
          // A folder the agent wants a second look at (a flagged folder, files on both sides) is not
          // decided here: it stays on the game's page, where those questions are asked in place.
          left.push(s.path)
          failed.push(`${s.path}: ${e instanceof ApiError ? e.message : String(e)}`)
        }
      }
      await defer(left)
      onChanged()
      if (failed.length > 0) {
        setProblem(`Not added — finish ${failed.length === 1 ? 'it' : 'them'} on the game's page: ${failed.join(' · ')}`)
        return
      }
      next()
    } finally {
      setBusy(false)
    }
  }

  async function skip() {
    setBusy(true)
    try { await defer(game.items.map(s => s.path)) } finally { setBusy(false) }
    next()
  }

  async function skipAll() {
    setBusy(true)
    try {
      for (const g of games.slice(index)) {
        await api.answerFolderSuggestions(g.gameId, { defer: g.items.map(s => s.path) }).catch(() => {})
      }
    } finally { setBusy(false) }
    onDone()
  }

  const toggle = (path: string) => setOff(prev => {
    const n = new Set(prev)
    if (n.has(path)) n.delete(path)
    else n.add(path)
    return n
  })
  const ticked = game.items.filter(s => !off.has(s.path)).length

  return (
    <div style={{
      position: 'fixed', inset: 0, zIndex: 1000,
      background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
    }}>
      <div style={{ width: '100%', maxWidth: 520 }} role="dialog" aria-modal="true" aria-label="More save folders found">
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          fontSize: 10.5, fontWeight: 700, letterSpacing: '0.08em', textTransform: 'uppercase',
          color: 'var(--color-dim)', marginBottom: 8,
        }}>
          <span>More save folders found</span>
          {games.length > 1 && (
            <span style={{
              fontFamily: "'JetBrains Mono', monospace", color: 'var(--color-watch-ink)',
              background: 'var(--color-watch-soft)', border: '1px solid var(--color-watch-line)',
              borderRadius: 20, padding: '2px 8px', letterSpacing: 0,
            }}>{index + 1} of {games.length}</span>
          )}
        </div>

        <Card title={game.gameName}>
          <div className="sl-stack">
            <span style={{ fontSize: 12.5, color: 'var(--color-dim)', lineHeight: 1.5 }}>
              SaveLocker syncs one folder for {game.gameName} today, but the save database lists more that exist on
              this device. It cannot tell saves from settings, so check them: ticked folders sync on every device.
            </span>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              {game.items.map(s => (
                <label key={s.path} className="sl-inline" style={{ gap: 8, cursor: 'pointer' }}>
                  <input type="checkbox" checked={!off.has(s.path)} onChange={() => toggle(s.path)} disabled={busy} />
                  <span className="sl-path">{s.path}</span>
                </label>
              ))}
            </div>
            {problem && <span role="alert" style={{ fontSize: 12, color: 'var(--color-watch-ink)' }}>{problem}</span>}
            <div className="sl-inline" style={{ justifyContent: 'space-between' }}>
              <div className="sl-inline">
                {problem ? (
                  <Button size="sm" variant="primary" onClick={next}>Continue</Button>
                ) : (
                  <Button size="sm" variant="primary" disabled={busy || ticked === 0} onClick={() => void addTicked()}>
                    {ticked === 0 ? 'Add selected' : `Add ${ticked} folder${ticked === 1 ? '' : 's'}`}
                  </Button>
                )}
                <Button size="sm" disabled={busy || !!problem} onClick={() => void skip()}>Skip for now</Button>
              </div>
              {games.length - index > 1 && !problem && (
                <Button size="sm" variant="quiet" disabled={busy} onClick={() => void skipAll()}>Skip all</Button>
              )}
            </div>
            <small style={{ fontSize: 11, color: 'var(--color-dim)' }}>
              Skipped folders stay on each game's page under “Also found”, to add later.
            </small>
          </div>
        </Card>
      </div>
    </div>
  )
}
