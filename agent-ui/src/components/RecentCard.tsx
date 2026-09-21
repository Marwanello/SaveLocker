import { useState } from 'react'
import { formatTime } from '../format'
import { useActivityRecent } from '../useActivity'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

/** Same reading the old activity card applied to its log lines. A conflict is a decision waiting
 *  (accent); the rest of these are something that failed or was refused (amber); everything else is
 *  plain history and carries no colour at all. */
function dotTone(message: string): string {
  if (/conflict/i.test(message)) return 'sl-dot--crit'
  if (/refused|failed|unreachable|blocked|error/i.test(message)) return 'sl-dot--warn'
  return ''
}

/**
 * The Overview's "Recent": the last three things the agent did. The rolling history used to be shown
 * in full at the bottom of the page; trimming the Overview to quick info (plan.md Phase 5) must not
 * make the rest of it unreachable, so "Show all" expands it in place — plan.md's "no modals" rule —
 * rather than sending anyone to a separate page that does not exist yet.
 *
 * It subscribes to the `recent` slice only, so a byte-progress tick during a push does not re-render it.
 */
export function RecentCard() {
  const recent = useActivityRecent() ?? []
  const [expanded, setExpanded] = useState(false)
  const shown = expanded ? recent : recent.slice(0, 3)

  return (
    <Card
      title="Recent"
      flush
      headerRight={recent.length > 3 && (
        <Button size="sm" aria-expanded={expanded} onClick={() => setExpanded(e => !e)}>
          {expanded ? 'Show fewer' : `Show all ${recent.length}`}
        </Button>
      )}
    >
      {shown.length === 0 ? (
        <div className="sl-empty">No activity yet. Pushes, pulls and warnings show up here as they happen.</div>
      ) : (
        <ul className={`sl-feed ${expanded ? 'sl-feed--scroll' : ''}`.trim()}>
          {shown.map((e, i) => (
            <li key={`${e.timestampUtc}-${i}`}>
              <span className="sl-feed__when">{formatTime(e.timestampUtc)}</span>
              <span className={`sl-dot ${dotTone(e.message)}`.trim()} style={{ marginTop: 6 }} aria-hidden="true" />
              <span className="sl-feed__what">{e.message}</span>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
