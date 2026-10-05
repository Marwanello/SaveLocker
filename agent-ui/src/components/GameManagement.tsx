import { useCallback, useEffect, useState } from 'react'
import { api, ApiError, CONFIRM_HINT } from '../api'
import { copyText } from '../clipboard'
import { useFolderPicker } from '../useFolderPicker'
import type { FolderSuggestion, SaveFolder, TrackedGame } from '../types'
import { PathBrowserModal } from './PathBrowserModal'
import { Button } from './ui/Button'
import { Card } from './ui/Card'
import { Chip } from './ui/Chip'

interface Props {
  game: TrackedGame
  platform: string | undefined
  /** The game changed (folder, process names): the shell re-reads the list. */
  onChanged: () => void
  /** The game is no longer tracked here: leave its page. */
  onRemoved: () => void
}

type Note = { text: string; failed?: boolean }

/** What a folder request was for, kept so the agent's follow-up question (a flagged folder, files on
 *  both sides) can re-send exactly it with the answer. */
type FolderAction =
  | { kind: 'main'; path: string }
  | { kind: 'change'; key: string; path: string }
  | { kind: 'add'; path: string; key?: string; freeKey?: boolean; include?: string[] }

/** A suggested folder key from a folder's name, as the agent would make it — the user can change it. */
function keyFrom(path: string): string {
  const name = path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? ''
  const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32).replace(/-+$/, '')
  return slug || 'extra'
}

const MAIN_KEY = 'main'

/**
 * What used to live only in Settings' tracked-games list, on the page of the game it is about: its save
 * folders (the main one and any extra ones, tasks/multiple-save-paths), open it, say which process
 * means "running", stop tracking. Nothing here uses `alert`/`confirm`/`prompt` — a confirmation is the
 * button expanding into its own consequence (plan.md: no modals), and the questions the agent can ask
 * about a folder (a heuristic flagged it; it and the cloud's copy hold different files) are asked in place.
 */
export function GameManagement({ game, platform, onChanged, onRemoved }: Props) {
  const picker = useFolderPicker()
  const [note, setNote] = useState<Note | null>(null)
  const [path, setPath] = useState<{ where: string; copied: boolean } | null>(null)
  // A folder the agent's sanity heuristics flagged (a suspected Wine prefix, an oversized folder):
  // never applied without an explicit yes, and the question repeats the agent's own words.
  const [flagged, setFlagged] = useState<{ ask: string; action: FolderAction } | null>(null)
  // Files here and in the cloud's copy of the folder differ: which one wins is the user's call.
  const [choice, setChoice] = useState<{ ask: string; action: FolderAction } | null>(null)
  // A folder picked for "Add save folder", waiting for its name and optional include patterns.
  const [adding, setAdding] = useState<{ path: string; key: string; include: string } | null>(null)
  const [removing, setRemoving] = useState<string | null>(null)
  const [suggestions, setSuggestions] = useState<FolderSuggestion[]>([])
  const [editingProcess, setEditingProcess] = useState(false)
  const [processText, setProcessText] = useState('')
  const [stopping, setStopping] = useState(false)

  const folders: SaveFolder[] = game.paths?.length
    ? game.paths
    : [{ key: MAIN_KEY, label: null, path: game.path, mapped: !!game.path, includeGlobs: [] }]
  const extras = folders.filter(f => f.key !== MAIN_KEY)

  const loadSuggestions = useCallback(() => {
    api.folderSuggestions()
      .then(all => setSuggestions(all.filter(s => s.gameId === game.id)))
      .catch(() => setSuggestions([]))
  }, [game.id])
  useEffect(() => { loadSuggestions() }, [loadSuggestions, game.paths?.length])

  function clearPrompts() {
    setFlagged(null)
    setChoice(null)
  }

  async function run(action: FolderAction, confirm = false, keep?: 'local' | 'cloud') {
    try {
      if (action.kind === 'main') await api.setGameFolder(game.id, action.path, confirm)
      else if (action.kind === 'change') await api.setGameFolder(game.id, action.path, confirm, action.key, keep)
      else {
        const added = await api.addGameFolder(game.id, {
          path: action.path, key: action.key, includeGlobs: action.include, keep, confirm,
          freeKey: action.freeKey,
        })
        action = { ...action, key: added.key }
      }
      clearPrompts()
      setAdding(null)
      setNote({
        text: action.kind === 'main' ? `Save folder set to ${action.path}.`
          : action.kind === 'add' ? `Now syncing ${action.path} as “${action.key}”, on every device.`
          : `“${action.key}” is now ${action.path} on this device.`,
      })
      onChanged()
      loadSuggestions()
    } catch (e) {
      const message = (e as Error).message
      // Only the heuristic warnings are confirmable, and the agent says so in a field of its own — so a
      // hard refusal (a drive root, a user profile) can never be clicked past, whatever its wording.
      if (!confirm && e instanceof ApiError && e.needsConfirm) {
        setChoice(null)
        setFlagged({ ask: message.replace(CONFIRM_HINT, ''), action })
        return
      }
      if (!keep && e instanceof ApiError && e.needsChoice) {
        setFlagged(null)
        setChoice({ ask: message, action })
        return
      }
      setNote({ text: `Could not set the save folder: ${message}`, failed: true })
    }
  }

  const pickFor = (apply: (next: string) => Promise<void>, start: string | null) => {
    setNote(null)
    clearPrompts()
    return picker.pick({
      name: game.name,
      start: async () => start
        || game.path
        || (await api.suggestedPath(game.id).catch(() => ({ path: null }))).path,
      nativePick: () => api.folderPick(),
      apply,
    })
  }

  const changeFolder = (f: SaveFolder) => pickFor(
    next => run(f.key === MAIN_KEY ? { kind: 'main', path: next } : { kind: 'change', key: f.key, path: next }),
    f.path || null)

  const addFolder = () => pickFor(async next => {
    setAdding({ path: next, key: keyFrom(next), include: '' })
  }, null)

  async function removeFolder(key: string) {
    try {
      await api.removeGameFolder(game.id, key)
      setRemoving(null)
      setNote({ text: `“${key}” no longer syncs, on any device. Its files are still where they were.` })
      onChanged()
      loadSuggestions()
    } catch (e) {
      setNote({ text: `Could not remove “${key}”: ${(e as Error).message}`, failed: true })
    }
  }

  async function dontSync(s: FolderSuggestion) {
    try {
      await api.answerFolderSuggestions(game.id, { ignore: [s.path] })
      loadSuggestions()
    } catch (e) {
      setNote({ text: (e as Error).message, failed: true })
    }
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
  const actionPath = (a: FolderAction) => a.path

  return (
    <Card title="This game on this device">
      <div className="sl-stack">
        <div className="sl-field">
          <label>Save folders</label>
          {folders.map(f => (
            <div key={f.key} className="sl-confirm" style={{ alignItems: 'flex-start', justifyContent: 'space-between' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0, flex: 1 }}>
                <span className="sl-inline" style={{ gap: 6 }}>
                  <b style={{ color: 'var(--color-fg)', fontWeight: 600 }}>{f.key === MAIN_KEY ? 'Main' : (f.label || f.key)}</b>
                  {f.key !== MAIN_KEY && !f.mapped && <Chip tone="warn">Not on this device</Chip>}
                  {f.includeGlobs.length > 0 && <Chip>only {f.includeGlobs.join(', ')}</Chip>}
                </span>
                {f.path
                  ? <span className="sl-path">{f.path}</span>
                  : <span style={{ fontSize: 12 }}>
                      {f.key === MAIN_KEY
                        ? 'Not set yet.'
                        : 'SaveLocker keeps a copy of this folder so your other devices still get it. Choose where it lives here to use it.'}
                    </span>}
              </div>
              <div className="sl-inline">
                <Button size="sm" variant={f.path ? undefined : 'primary'} onClick={() => void changeFolder(f)}>
                  {f.path ? 'Change' : 'Choose folder'}
                </Button>
                {f.key === MAIN_KEY && (
                  <Button size="sm" disabled={!game.path} onClick={() => void openFolder()}>Open folder</Button>
                )}
                {f.key !== MAIN_KEY && (
                  <Button size="sm" variant="quiet" onClick={() => setRemoving(r => (r === f.key ? null : f.key))}>Remove…</Button>
                )}
              </div>
              {removing === f.key && (
                <div className="sl-confirm" role="alert" style={{ width: '100%' }}>
                  <span>
                    Stop syncing “{f.label || f.key}” on every device? Its files stay where they are, and versions already
                    uploaded still hold it.
                  </span>
                  <Button size="sm" variant="primary" onClick={() => void removeFolder(f.key)}>Stop syncing it</Button>
                  <Button size="sm" variant="quiet" onClick={() => setRemoving(null)}>Keep it</Button>
                </div>
              )}
            </div>
          ))}
          <div className="sl-inline">
            <Button size="sm" disabled={!game.path} onClick={() => void addFolder()}
              title={game.path ? undefined : 'Choose the main save folder first.'}>
              Add save folder…
            </Button>
            {extras.length === 0 && (
              <small>For a game that keeps saves in more than one place — say Documents and AppData.</small>
            )}
          </div>
        </div>

        {adding && (
          <div className="sl-field" role="group" aria-label="New save folder">
            <span className="sl-path">{adding.path}</span>
            <div className="sl-inline">
              <label htmlFor="folder-key" style={{ fontSize: 11.5 }}>Name</label>
              <input
                id="folder-key" className="sl-input sl-input--mono" style={{ width: 160 }}
                value={adding.key} onChange={e => setAdding({ ...adding, key: e.target.value })}
              />
              <label htmlFor="folder-include" style={{ fontSize: 11.5 }}>Only these files</label>
              <input
                id="folder-include" className="sl-input sl-input--mono" style={{ flex: 1, minWidth: 160 }}
                value={adding.include} placeholder="optional, e.g. *.sav, slot*"
                onChange={e => setAdding({ ...adding, include: e.target.value })}
              />
            </div>
            <small>
              Every device that syncs {game.name} gets this folder. The name is how they recognise it: lower-case
              letters, digits and “-”, and it cannot be changed later.
            </small>
            <div className="sl-inline">
              <Button size="sm" variant="primary" onClick={() => void run({
                kind: 'add', path: adding.path, key: adding.key.trim() || undefined,
                include: adding.include.split(',').map(s => s.trim()).filter(Boolean),
              })}>Add folder</Button>
              <Button size="sm" variant="quiet" onClick={() => setAdding(null)}>Cancel</Button>
            </div>
          </div>
        )}

        {suggestions.length > 0 && (
          <div className="sl-field">
            <label>Also found</label>
            <small>
              The save database lists these folders for {game.name} too, and they exist here. It cannot tell saves from
              settings, so nothing is synced until you add it.
            </small>
            {suggestions.map(s => (
              <div key={s.path} className="sl-confirm">
                <span className="sl-path" style={{ flex: 1, minWidth: 0 }}>{s.path}</span>
                <Button size="sm" onClick={() => void run({ kind: 'add', path: s.path, key: s.key, freeKey: true })}>Add</Button>
                <Button size="sm" variant="quiet" onClick={() => void dontSync(s)}>Don’t sync</Button>
              </div>
            ))}
          </div>
        )}

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
            <span>Use <span className="sl-path">{actionPath(flagged.action)}</span> anyway?</span>
            <Button size="sm" variant="primary" onClick={() => void run(flagged.action, true)}>Use it anyway</Button>
            <Button size="sm" variant="quiet" onClick={() => { setFlagged(null); setNote({ text: 'Save folder unchanged.' }) }}>Cancel</Button>
          </div>
        )}

        {choice && (
          <div className="sl-confirm" role="alert">
            <span>{choice.ask}</span>
            <span>Which copy should every device keep?</span>
            <Button size="sm" variant="primary" onClick={() => void run(choice.action, true, 'local')}>This device’s files</Button>
            <Button size="sm" onClick={() => void run(choice.action, true, 'cloud')}>The cloud’s copy</Button>
            <Button size="sm" variant="quiet" onClick={() => { setChoice(null); setNote({ text: 'Save folder unchanged.' }) }}>Cancel</Button>
          </div>
        )}

        {needsProcess && (
          <div className="sl-inline">
            <Button size="sm" onClick={() => { setProcessText(game.processNames.join(', ')); setEditingProcess(e => !e) }}>
              {game.processNames.length > 0 ? 'Edit game process' : 'Set game process'}
            </Button>
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
