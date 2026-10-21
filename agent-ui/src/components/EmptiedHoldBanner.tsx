import { useState } from 'react'
import { api } from '../api'
import { formatAgo, formatDateTime } from '../format'
import type { EmptiedHold } from '../types'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'

interface Props {
  hold: EmptiedHold
  gameName: string
  /** What the choice did (or why it could not), for the page's toast; the caller re-reads the holds. */
  onAnswered: (message: string, failed: boolean) => void
}

/**
 * A game held because every save file vanished from its folder here (tasks/empty-save-hold): nothing is pushed or
 * pulled for it on this device until one of two choices. Restore here is the safe one and goes first; Delete
 * everywhere removes the save on every machine, so it asks once more. Shown on the game page and on Conflicts.
 */
export function EmptiedHoldBanner({ hold, gameName, onAnswered }: Props) {
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [busy, setBusy] = useState(false)

  async function answer(action: 'restore-here' | 'delete-everywhere') {
    setBusy(true)
    try { onAnswered((await api.answerEmptiedHold(hold.gameId, action)).message, false) }
    catch (e) { onAnswered(e instanceof Error ? e.message : String(e), true) }
    finally { setBusy(false); setConfirmDelete(false) }
  }

  return (
    <Banner
      tone="crit"
      title={`Every save file of ${gameName} is gone from this device`}
      detail={
        <span title={formatDateTime(hold.heldAt)}>
          {hold.saveDirectory} had them when it last synced. Nothing is pushed or pulled for it here until you choose,
          so your other machines keep theirs. Held since {formatAgo(hold.heldAt)}.
        </span>
      }
      action={confirmDelete ? (
        <span className="sl-confirm">
          Delete it on every machine?
          <Button variant="primary" disabled={busy} data-pad-label="Delete on every machine"
            onClick={() => void answer('delete-everywhere')}>
            {busy ? 'Deleting…' : 'Delete on every machine'}
          </Button>
          <Button variant="quiet" disabled={busy} data-pad-label="Cancel" onClick={() => setConfirmDelete(false)}>Cancel</Button>
        </span>
      ) : (
        <span className="sl-confirm">
          <Button variant="primary" disabled={busy} data-pad-label="Restore here" onClick={() => void answer('restore-here')}>
            {busy ? 'Restoring…' : 'Restore here'}
          </Button>
          <Button disabled={busy} data-pad-label="Delete everywhere…" onClick={() => setConfirmDelete(true)}>
            Delete everywhere…
          </Button>
        </span>
      )}
    />
  )
}
