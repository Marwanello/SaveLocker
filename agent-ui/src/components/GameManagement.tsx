import { useState } from 'react'
import { api } from '../api'
import { copyText } from '../clipboard'
import { useFolderPicker } from '../useFolderPicker'
import type { TrackedGame } from '../types'
import { PathBrowserModal } from './PathBrowserModal'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

interface Props {
  game: TrackedGame
  platform: string | undefined
  /** The game changed (folder, process names): the shell re-reads the list. */
  onChanged: () => void
  /** The game is no longer tracked here: leave its page. */
  onRemoved: () => void
}

type Note = { text: string; failed?: boolean }

/**
 * What used to live only in Settings' tracked-games list, on the page of the game it is about:
 * change the save folder, open it, say which process means "running", stop tracking. Nothing here
 * uses `alert`/`confirm`/`prompt` — a confirmation is the button expanding into its own consequence
 * (plan.md: no modals), and the one heuristic warning the agent can raise about a folder is asked
 * about in place.
 */
export function GameManagement({ game, platform, onChanged, onRemoved }: Props) {
  const picker = useFolderPicker()
  const [note, setNote] = useState<Note | null>(null)
  const [path, setPath] = useState<{ where: string; copied: boolean } | null>(null)
  // A folder the agent's sanity heuristics flagged (a suspected Wine prefix, an oversized folder):
  // never applied without an explicit yes, and the question repeats the agent's own words.
  const [flagged, setFlagged] = useState<{ ask: string; path: string } | null>(null)
  const [editingProcess, setEditingProcess] = useState(false)
  const [processText, setProcessText] = useState('')
  const [stopping, setStopping] = useState(false)

  async function applyFolder(next: string, confirm: boolean) {
    try {
      await api.setGameFolder(game.id, next, confirm)
      setFlagged(null)
      setNote({ text: `Save folder set to ${next}.` })
      onChanged()
    } catch (e) {
      const message = (e as Error).message
      // Only the heuristic warnings carry this sentence, so a hard refusal (a drive root, a user
      // profile) can never be clicked past.
      if (!confirm && message.includes('Re-send with confirm')) {
        setFlagged({ ask: message.replace(' Re-send with confirm to use it anyway.', ''), path: next })
        return
      }
      setNote({ text: `Could not set the save folder: ${message}`, failed: true })
    }
  }

  const changeFolder = () => {
    setNote(null)
    setFlagged(null)
    return picker.pick({
      name: game.name,
      start: async () => game.path
        || (await api.suggestedPath(game.id).catch(() => ({ path: null }))).path,
      nativePick: () => api.folderPick(),
      apply: next => applyFolder(next, false),
    })
  }

  async function openFolder() {
    setNote(null)
    setPath(null)
    try {
      const res = await api.openFolder(game.id)
      if (!res.opened) setPath({ where: res.path, copied: false })
    } catch (e) {
      setNote({ text: (e as Error).message, failed: true })
    }
  }

  async function saveProcesses() {
    try {
      await api.setGameProcesses(game.id, processText.split(',').map(s => s.trim()).filter(Boolean))
      setEditingProcess(false)
      setNote({ text: 'Launch and exit syncing updated.' })
      onChanged()
    } catch (e) {
      setNote({ text: `Could not set the game process: ${(e as Error).message}`, failed: true })
    }
  }

  async function stopTracking() {
    try {
      await api.removeGame(game.id)
      onChanged()
      onRemoved()
    } catch (e) {
      setStopping(false)
      setNote({ text: `Could not stop tracking: ${(e as Error).message}`, failed: true })
    }
  }

  // A Linux game is launched through the Steam wrapper, which already knows what it launched; only the
  // Windows watcher needs a process name to know the game is running.
  const needsProcess = platform !== 'Linux'

  return (
    <Card title="This game on this device">
      <div className="sl-stack">
        <div className="sl-inline">
          <Button size="sm" onClick={() => void changeFolder()}>Change folder</Button>
          <Button size="sm" disabled={!game.path} onClick={() => void openFolder()}>Open folder</Button>
          {needsProcess && (
            <Button size="sm" onClick={() => { setProcessText(game.processNames.join(', ')); setEditingProcess(e => !e) }}>
              {game.processNames.length > 0 ? 'Edit game process' : 'Set game process'}
            </Button>
          )}
        </div>

        {path && (
          <div className="sl-confirm" role="status">
            <span>No desktop to open it on. The folder is</span>
            <span className="sl-path">{path.where}</span>
            <Button size="sm" onClick={() => void copyText(path.where).then(ok => setPath({ ...path, copied: ok }))}>
              {path.copied ? 'Copied' : 'Copy'}
            </Button>
          </div>
        )}

        {flagged && (
          <div className="sl-confirm" role="alert">
            <span>{flagged.ask}</span>
            <span>Use <span className="sl-path">{flagged.path}</span> anyway?</span>
            <Button size="sm" variant="primary" onClick={() => void applyFolder(flagged.path, true)}>Use it anyway</Button>
            <Button size="sm" variant="quiet" onClick={() => { setFlagged(null); setNote({ text: 'Save folder unchanged.' }) }}>Cancel</Button>
          </div>
        )}

        {editingProcess && (
          <div className="sl-field">
            <label htmlFor="game-process">Which process means “{game.name}” is running?</label>
            <div className="sl-inline">
              <input
                id="game-process"
                className="sl-input sl-input--mono"
                style={{ flex: 1, minWidth: 180 }}
                value={processText}
                placeholder="game.exe, launcher.exe"
                onChange={e => setProcessText(e.target.value)}
              />
              <Button size="sm" variant="primary" onClick={() => void saveProcesses()}>Save</Button>
              <Button size="sm" variant="quiet" onClick={() => setEditingProcess(false)}>Cancel</Button>
            </div>
            <small>
              Use the executable name; separate several with commas. Until this is set SaveLocker cannot take a
              lease when you launch, push when you quit, or stop a pull from overwriting a save while the game is open.
            </small>
          </div>
        )}

        {note && (
          <div role="status" style={{ fontSize: 12.5, color: note.failed ? 'var(--color-watch-ink)' : 'var(--color-dim)' }}>
            {note.text}
          </div>
        )}

        <div className="sl-setting__note" style={{ marginTop: 0 }}>
          {stopping ? (
            <div className="sl-confirm">
              <span>
                {game.name} stops syncing on this device. Its saves stay on the server for your other machines,
                and nothing on this device is deleted.
              </span>
              <Button size="sm" variant="primary" onClick={() => void stopTracking()}>Stop tracking {game.name}</Button>
              <Button size="sm" variant="quiet" onClick={() => setStopping(false)}>Keep tracking</Button>
            </div>
          ) : (
            <Button size="sm" variant="quiet" onClick={() => setStopping(true)}>Stop tracking…</Button>
          )}
        </div>
      </div>

      {picker.browsing && (
        <PathBrowserModal
          gameName={picker.browsing.name}
          initialPath={picker.browsing.start}
          onConfirm={p => void picker.confirmBrowsed(p)}
          onCancel={picker.cancel}
        />
      )}
    </Card>
  )
}
