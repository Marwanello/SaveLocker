import { useRef } from 'react'
import type { KeyboardEvent } from 'react'
import type { GamePlaythroughs, Playthrough } from '../types'
import { formatAgo } from '../format'

interface Props {
  data: GamePlaythroughs
  /** The playthrough being looked at: this device's own unless another tab was picked. */
  viewing: string | null
  onView: (id: string) => void
  onNew: () => void
  /** Another sync holds the agent's gate, or a switch is running. */
  disabled?: boolean
  /** This device's name, so "also on …" never names it. */
  machineName: string
}

/** What a tab says under its name. */
function sub(p: Playthrough, selected: boolean, deviceName?: string): string {
  if (p.archivedAt) return 'Archived'
  const others = p.devices.filter(d => d.machineName !== deviceName).map(d => d.machineName)
  const base = selected ? 'Played here' : p.head ? `Latest ${formatAgo(p.head.createdAt)}` : 'No save yet'
  return others.length > 0 ? `${base} · also on ${others.join(', ')}` : base
}

/**
 * One tab per playthrough of a game (tasks/save-playthroughs Phase 6), with a tick on the one this device plays. Another tab
 * shows that playthrough read-only, with "Play this one here" on the page; picking a tab never changes what the device syncs.
 * Offline there is no list: the tabs are the playthroughs this device keeps a copy of, and the one it is on.
 * Arrow keys move between tabs; every tab is a button the controller's ring reaches.
 */
export function PlaythroughTabs({ data, viewing, onView, onNew, disabled, machineName }: Props) {
  const refs = useRef<(HTMLButtonElement | null)[]>([])
  const current = viewing ?? data.selectedId ?? null

  // Offline, the server's list is empty: build the tabs from what this device itself knows.
  const tabs: { id: string; name: string; selected: boolean; text: string; archived: boolean; conflict: boolean; copy: boolean }[] =
    data.playthroughs.length > 0
      ? data.playthroughs.map(p => ({
          id: p.id, name: p.name, selected: p.id === data.selectedId, text: sub(p, p.id === data.selectedId, machineName),
          archived: !!p.archivedAt, conflict: p.hasOpenConflict, copy: data.copies.some(c => c.playthroughId === p.id),
        }))
      : [
          ...(data.selectedId ? [{
            id: data.selectedId, name: data.selectedName ?? 'This playthrough', selected: true, text: 'Played here',
            archived: false, conflict: false, copy: false,
          }] : []),
          ...data.copies.filter(c => c.playthroughId !== data.selectedId).map(c => ({
            id: c.playthroughId, name: c.name ?? 'Playthrough', selected: false,
            text: `Copy from ${formatAgo(c.capturedAt)}`, archived: false, conflict: false, copy: true,
          })),
        ]

  const index = Math.max(0, tabs.findIndex(t => t.id === current))

  function onKey(e: KeyboardEvent<HTMLButtonElement>) {
    if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return
    e.preventDefault()
    const next = (index + (e.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length
    onView(tabs[next].id)
    refs.current[next]?.focus()
  }

  return (
    <div className="sl-ptabs" role="tablist" aria-label="Playthroughs">
      {tabs.map((t, i) => (
        <button
          key={t.id}
          ref={el => { refs.current[i] = el }}
          type="button"
          role="tab"
          className={`sl-ptab${t.archived ? ' sl-ptab--archived' : ''}`}
          aria-selected={i === index}
          tabIndex={i === index ? 0 : -1}
          data-pad-label={`${t.name} playthrough`}
          onClick={() => onView(t.id)}
          onKeyDown={onKey}
        >
          <span className={`sl-ptab__tick${t.selected ? ' sl-ptab__tick--on' : ''}`} aria-hidden="true">{t.selected ? '✓' : ''}</span>
          <span>
            <span className="sl-ptab__t1">{t.name}</span>
            <span className="sl-ptab__t2">{t.text}</span>
          </span>
          {t.conflict && <span className="sl-dot sl-dot--crit" aria-label="Conflict" />}
        </button>
      ))}
      <button
        type="button" className="sl-ptab sl-ptab--new" disabled={disabled || data.offline}
        data-pad-label="New playthrough" onClick={onNew}
        title={data.offline ? 'A new playthrough needs the server' : undefined}
      >
        + New playthrough
      </button>
    </div>
  )
}
