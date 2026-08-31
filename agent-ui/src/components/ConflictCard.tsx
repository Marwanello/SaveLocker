import { useState, type ReactNode } from 'react'
import { Cloud, HardDrive } from 'lucide-react'
import type { Conflict, SaveVersion, VersionStats } from '../types'

const shortId = (id: string) => id.replace(/-/g, '').slice(0, 8)
// Server timestamps have no zone suffix but are UTC (System.Text.Json default) — without this a
// browser in a non-UTC zone parses them as local time and every "when" is wrong by the offset.
const asUtc = (t: string) => /[Z+]|-\d\d:\d\d$/.test(t) ? t : t + 'Z'
const absolute = (t: string) => new Date(asUtc(t)).toLocaleString()
const fmtSize = (n: number) =>
  n < 1024 ? `${n} B`
    : n < 1024 * 1024 ? (n / 1024).toFixed(1) + ' KB'
      : (n / (1024 * 1024)).toFixed(2) + ' MB'

/** "12m ago" / "2h ago" — the number that actually drives which side to keep, so it is the
 * headline text in each panel; the exact timestamp is still one hover away via `title`. */
function relative(t: string): string {
  const ms = Date.now() - new Date(asUtc(t)).getTime()
  const mins = Math.round(ms / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.round(mins / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.round(hours / 24)}d ago`
}

interface Props {
  conflict: Conflict
  gameName: string
  machineName: string
  versionA?: SaveVersion
  versionB?: SaveVersion
  statsA?: VersionStats
  statsB?: VersionStats
  resolving: boolean
  /** 'confirm' (default): click a side to select it, then a separate Resolve button commits —
   * used on the Conflicts page, where reviewing before committing is the point. 'immediate': a
   * side's own button commits right away — used in the sync-time pop-up, where the point is to
   * keep moving through a queue. */
  mode?: 'confirm' | 'immediate'
  onResolve: (winningVersionId: string, keepBoth: boolean) => void
  /** Extra control rendered in the footer, beside the "keep both" checkbox — the sync pop-up's
   * "Decide later" button. */
  footerExtra?: ReactNode
}

/**
 * One conflict, framed as local vs. cloud — never machine vs. machine. Every device only ever
 * compares itself to the server's current head, so the cloud side is always labelled "The cloud"
 * regardless of which machine last updated it; that machine's name is supporting context only, a
 * small caption, never the primary label (tasks/conflict-resolution-ui/plan.md, decision 2). Shared
 * by `ConflictsView` (the page) and `SyncConflictModal` (the sync-time pop-up) so the two never drift.
 */
export function ConflictCard({
  conflict, gameName, machineName, versionA, versionB, statsA, statsB,
  resolving, mode = 'confirm', onResolve, footerExtra,
}: Props) {
  // No default selection, deliberately — the version data this would key off loads
  // asynchronously after the version and stats fetches resolve, and a choice this consequential
  // should never be pre-picked out from under someone anyway (the same "never silently default to
  // a side" rule the Decky-equivalent popup's own mockup states explicitly).
  const [selected, setSelected] = useState<string | null>(null)
  const [keepBoth, setKeepBoth] = useState(false)

  const sides: Array<{
    id: string; kind: 'cloud' | 'local'; v?: SaveVersion; s?: VersionStats
  }> = [
    { id: conflict.versionAId, kind: 'cloud', v: versionA, s: statsA },
    { id: conflict.versionBId, kind: 'local', v: versionB, s: statsB },
  ]

  const newerId =
    versionA && versionB
      ? (new Date(asUtc(versionA.createdAt)) > new Date(asUtc(versionB.createdAt))
          ? conflict.versionAId : conflict.versionBId)
      : null

  function labelFor(kind: 'cloud' | 'local', v?: SaveVersion): string {
    if (kind === 'cloud') return 'The cloud'
    return v?.machineName === machineName ? 'This device' : 'Local save'
  }

  function act(versionId: string) {
    if (mode === 'immediate') { onResolve(versionId, keepBoth); return }
    setSelected(versionId)
  }

  const selectedSide = sides.find(s => s.id === selected)
  const selectedLabel = selectedSide ? labelFor(selectedSide.kind, selectedSide.v) : null

  return (
    <div style={{
      background: '#241a1a', border: `1px solid ${conflict.escalated ? '#e5534b' : '#4a2a2a'}`,
      borderRadius: 8, padding: '12px 14px', flexShrink: 0,
    }}>
      <div style={{ color: '#f4a60d', fontWeight: 700, fontSize: 13 }}>
        {gameName} — choose the save to keep
      </div>
      {conflict.escalated && (
        <div style={{ color: '#e5534b', fontSize: 11, fontWeight: 600, marginTop: 5 }}>
          Overdue — this conflict has been unresolved for more than six hours.
        </div>
      )}
      {conflict.count > 1 && (
        <div style={{ color: '#8b9aaa', fontSize: 11, marginTop: 5, lineHeight: 1.5 }}>
          {conflict.count} divergent saves folded into this conflict — the newest is offered below.
        </div>
      )}

      <div style={{ display: 'flex', gap: 8, marginTop: 10, flexWrap: 'wrap' }}>
        {sides.map(side => {
          const label = labelFor(side.kind, side.v)
          const Icon = side.kind === 'cloud' ? Cloud : HardDrive
          const isSelected = selected === side.id
          const isNewer = newerId === side.id
          const mine = side.v?.machineName === machineName
          const caption = side.v && !mine ? `from "${side.v.machineName}"` : null

          return (
            <div
              key={side.id}
              onClick={() => act(side.id)}
              style={{
                flex: '1 1 210px', minWidth: 210, cursor: 'pointer',
                background: '#1E252A',
                border: `1px solid ${isSelected ? '#129271' : '#4a2a2a'}`,
                borderRadius: 6, padding: 10,
                transition: 'border-color .12s ease',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 7 }}>
                <div style={{
                  width: 22, height: 22, borderRadius: 5, flexShrink: 0,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  background: isSelected ? 'rgba(18,146,113,0.18)' : '#2c383f',
                  color: isSelected ? '#16b992' : '#8b9aaa',
                }}>
                  <Icon size={12} strokeWidth={2} />
                </div>
                <span style={{ color: '#ECEFF1', fontWeight: 600, fontSize: 12 }}>{label}</span>
              </div>

              <div
                title={side.v ? absolute(side.v.createdAt) : undefined}
                style={{
                  fontFamily: "'JetBrains Mono', monospace", fontSize: 14, fontWeight: 600,
                  color: '#ECEFF1', margin: '6px 0 2px', display: 'flex', alignItems: 'baseline', gap: 6,
                }}
              >
                {side.v ? relative(side.v.createdAt) : shortId(side.id)}
                {isNewer && (
                  <span style={{
                    fontFamily: "'Inter', sans-serif", fontSize: 9, fontWeight: 700,
                    letterSpacing: '0.05em', textTransform: 'uppercase', color: '#16b992',
                    background: 'rgba(18,146,113,0.14)', borderRadius: 10, padding: '1px 6px',
                  }}>newer</span>
                )}
              </div>

              {side.v && (
                <div style={{ color: '#8b9aaa', fontSize: 10.5, fontFamily: "'JetBrains Mono', monospace" }}>
                  {side.s ? `${side.s.fileCount} file${side.s.fileCount === 1 ? '' : 's'} · ` : ''}
                  {fmtSize(side.v.size)}
                </div>
              )}
              {caption && (
                <div style={{ color: '#556070', fontSize: 10.5, marginTop: 3 }}>{caption}</div>
              )}

              <button
                disabled={resolving}
                onClick={e => { e.stopPropagation(); act(side.id) }}
                style={{
                  marginTop: 8, alignSelf: 'flex-start',
                  padding: '5px 10px', borderRadius: 4, fontSize: 11, fontWeight: 600,
                  cursor: resolving ? 'default' : 'pointer', opacity: resolving ? 0.6 : 1,
                  background: isSelected ? '#129271' : '#2c383f',
                  color: isSelected ? '#fff' : '#8b9aaa',
                  border: `1px solid ${isSelected ? '#129271' : '#445059'}`,
                }}
              >
                Keep this
              </button>
            </div>
          )
        })}
      </div>

      <div style={{
        marginTop: 12, paddingTop: 12, borderTop: '1px dashed #3a4750',
        display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap',
      }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 7, fontSize: 11.5, color: '#8b9aaa', cursor: 'pointer' }}>
          <input
            type="checkbox"
            checked={keepBoth}
            onChange={e => setKeepBoth(e.target.checked)}
            style={{ accentColor: '#fdce63', width: 13, height: 13 }}
          />
          Also keep the other one as a backup — restorable later from Backups
        </label>

        {footerExtra}

        {mode === 'confirm' && (
          <button
            disabled={!selected || resolving}
            onClick={() => selected && onResolve(selected, keepBoth)}
            style={{
              padding: '6px 14px', borderRadius: 5, fontSize: 11.5, fontWeight: 700, border: 'none',
              cursor: selected && !resolving ? 'pointer' : 'default',
              background: selected ? '#129271' : '#384249',
              color: selected ? '#fff' : '#556070',
            }}
          >
            {selectedLabel ? `Resolve with ${selectedLabel}` : 'Resolve'}
          </button>
        )}
      </div>
    </div>
  )
}
