import { useState, type ReactNode } from 'react'
import { Cloud, HardDrive } from 'lucide-react'
import type { Conflict, SaveVersion, VersionStats } from '../types'
import { asUtc, formatAgo, formatDateTime } from '../format'

const shortId = (id: string) => id.replace(/-/g, '').slice(0, 8)
const fmtSize = (n: number) =>
  n < 1024 ? `${n} B`
    : n < 1024 * 1024 ? (n / 1024).toFixed(1) + ' KB'
      : (n / (1024 * 1024)).toFixed(2) + ' MB'

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
      background: 'var(--color-panel)', border: '1px solid var(--color-line)',
      borderRadius: 10, padding: '18px 20px', flexShrink: 0,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 14.5, fontWeight: 700, color: 'var(--color-fg)' }}>{gameName}</span>
        <span style={{
          fontSize: 10, fontWeight: 700, letterSpacing: '0.04em', textTransform: 'uppercase',
          color: 'var(--color-watch-ink)', background: 'var(--color-watch-soft)',
          border: '1px solid var(--color-watch-line)', borderRadius: 20, padding: '2px 8px',
        }}>Conflict</span>
      </div>
      <div style={{ fontSize: 12.5, lineHeight: 1.6, color: 'var(--color-dim)', marginTop: 8, maxWidth: '54ch' }}>
        {labelFor('local', versionB)} and {labelFor('cloud', versionA).toLowerCase()} both changed since
        the last sync. Pick which one to keep — the other is never deleted, just set aside.
      </div>
      {conflict.escalated && (
        <div style={{ color: 'var(--color-accent-ink)', fontSize: 11, fontWeight: 600, marginTop: 6 }}>
          Overdue — this conflict has been unresolved for more than six hours.
        </div>
      )}
      {conflict.count > 1 && (
        <div style={{ color: 'var(--color-dim)', fontSize: 11, marginTop: 6, lineHeight: 1.5 }}>
          {conflict.count} divergent saves folded into this conflict — the newest is offered below.
        </div>
      )}

      <div style={{ display: 'flex', gap: 12, marginTop: 16, flexWrap: 'wrap' }}>
        {sides.map(side => {
          const label = labelFor(side.kind, side.v)
          const Icon = side.kind === 'cloud' ? Cloud : HardDrive
          const isSelected = selected === side.id
          const isNewer = newerId === side.id
          const mine = label === 'This device'
          const caption = !side.v ? null
            : side.kind === 'local'
              ? (mine ? "This is the machine you're using right now." : `Last synced from "${side.v.machineName}"`)
              : `Last updated from "${side.v.machineName}"`

          return (
            <div
              key={side.id}
              onClick={() => { if (!resolving) act(side.id) }}
              style={{
                flex: '1 1 210px', minWidth: 210, cursor: resolving ? 'default' : 'pointer',
                background: isSelected
                  ? 'linear-gradient(180deg, color-mix(in oklab, var(--color-safe) 10%, transparent), color-mix(in oklab, var(--color-safe) 3%, transparent) 60%)'
                  : 'var(--color-raise)',
                border: `1px solid ${isSelected ? 'var(--color-safe-line)' : 'var(--color-line)'}`,
                borderRadius: 8, padding: '14px 15px',
                display: 'flex', flexDirection: 'column', gap: 10,
                transition: 'border-color .12s ease, background .12s ease',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <div style={{
                  width: 26, height: 26, borderRadius: 6, flexShrink: 0,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  background: isSelected ? 'color-mix(in oklab, var(--color-safe) 18%, transparent)' : 'var(--color-raise)',
                  color: isSelected ? 'var(--color-safe-ink)' : 'var(--color-dim)',
                }}>
                  <Icon size={14} strokeWidth={2} />
                </div>
                <span style={{ color: 'var(--color-fg)', fontWeight: 700, fontSize: 13 }}>{label}</span>
              </div>

              <div
                title={side.v ? formatDateTime(side.v.createdAt) : undefined}
                style={{
                  fontFamily: "'JetBrains Mono', monospace", fontSize: 15, fontWeight: 600,
                  color: 'var(--color-fg)', display: 'flex', alignItems: 'baseline', gap: 7,
                }}
              >
                {side.v ? formatAgo(side.v.createdAt) : shortId(side.id)}
                {isNewer && (
                  <span style={{
                    fontFamily: 'var(--font-sans)', fontSize: 9.5, fontWeight: 700,
                    letterSpacing: '0.05em', textTransform: 'uppercase', color: 'var(--color-safe-ink)',
                    background: 'var(--color-safe-soft)', borderRadius: 10, padding: '1px 6px',
                  }}>newer</span>
                )}
              </div>

              {side.v && (
                <div style={{ color: 'var(--color-dim)', fontSize: 11, fontFamily: "'JetBrains Mono', monospace" }}>
                  {side.s ? `${side.s.fileCount} file${side.s.fileCount === 1 ? '' : 's'} · ` : ''}
                  {fmtSize(side.v.size)}
                </div>
              )}
              {caption && (
                <div style={{ color: 'var(--color-dim)', fontSize: 10.5, lineHeight: 1.5 }}>{caption}</div>
              )}

              <button
                disabled={resolving}
                onClick={e => { e.stopPropagation(); act(side.id) }}
                style={{
                  marginTop: 2, alignSelf: 'flex-start',
                  padding: '6px 12px', borderRadius: 5, fontSize: 11.5, fontWeight: 600,
                  cursor: resolving ? 'default' : 'pointer', opacity: resolving ? 0.6 : 1,
                  background: isSelected ? 'var(--color-accent)' : 'var(--color-raise)',
                  color: isSelected ? 'var(--color-on-accent)' : 'var(--color-dim)',
                  border: `1px solid ${isSelected ? 'var(--color-accent)' : 'var(--color-line)'}`,
                }}
              >
                Keep this
              </button>
            </div>
          )
        })}
      </div>

      <div style={{
        marginTop: 14, paddingTop: 14, borderTop: '1px dashed var(--color-line)',
        display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap',
      }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, color: 'var(--color-dim)', cursor: 'pointer' }}>
          <input
            type="checkbox"
            checked={keepBoth}
            onChange={e => setKeepBoth(e.target.checked)}
            style={{ accentColor: 'var(--color-watch-ink)', width: 13, height: 13 }}
          />
          Also keep the other one as a backup — restorable later from Backups
        </label>

        {footerExtra}

        {mode === 'confirm' && (
          <button
            disabled={!selected || resolving}
            onClick={() => selected && onResolve(selected, keepBoth)}
            style={{
              padding: '7px 16px', borderRadius: 6, fontSize: 12, fontWeight: 700, border: 'none',
              cursor: selected && !resolving ? 'pointer' : 'default',
              background: selected ? 'var(--color-accent)' : 'var(--color-raise)',
              color: selected ? 'var(--color-on-accent)' : 'var(--color-dim)',
            }}
          >
            {selectedLabel ? `Resolve with ${selectedLabel}` : 'Resolve'}
          </button>
        )}
      </div>
    </div>
  )
}
