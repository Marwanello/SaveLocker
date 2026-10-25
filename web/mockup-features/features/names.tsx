import { useState } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Icon } from '../../src/components/ui/Icon'
import { ago, when } from '../../src/format'
import { toast } from '../../src/toast'
import type { Version } from '../../src/types'
import { G, versions } from '../fixtures'
import { getState, setState, useMock } from '../store'
import { Dialog, fieldCls } from './common'

const SUGGEST = ['Before the Radiance', 'Pantheon 5 start', '112% done', 'Before Steel Soul']
const seeded = () => ({ [versions[G.hollow][3].id]: 'Before the Radiance' })

export function useLabels() {
  const labels = useMock(s => s.labels)
  return { ...seeded(), ...labels } as Record<string, string | null>
}
export function saveLabel(id: string, text: string | null) {
  const clean = text && text.trim() ? text.trim().slice(0, 80) : null
  setState({ labels: { ...getState().labels, [id]: clean } })
  toast(clean ? 'Named. Pruning never removes this version.' : 'Name removed. Protect is unchanged.')
}

export function NameChip({ text }: { text: string }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full border border-info-line bg-info-soft text-info-ink px-2 py-0.5 text-[11px] font-sans font-medium whitespace-nowrap">
      <Icon name="pencil" size={10} />{text}
    </span>
  )
}

export function InlineName({ v, label, onDone }: { v: Version; label: string | null; onDone: () => void }) {
  const [text, setText] = useState(label ?? '')
  const save = () => { saveLabel(v.id, text); onDone() }
  return (
    <span className="flex gap-1.5 items-center flex-wrap mt-1 font-sans">
      <input autoFocus className={`${fieldCls} w-[200px]`} value={text} maxLength={80} placeholder="Name this save"
        onChange={e => setText(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') save(); if (e.key === 'Escape') onDone() }} />
      <Button size="sm" variant="primary" onClick={save}>Save</Button>
      <Button size="sm" variant="quiet" onClick={onDone}>Cancel</Button>
    </span>
  )
}

export function NameDialog({ v, label, onClose }: { v: Version; label: string | null; onClose: () => void }) {
  const [text, setText] = useState(label ?? '')
  return (
    <Dialog title={label ? 'Rename this save' : 'Name this save'} sub={`${v.machineName} · ${when(v.createdAt)}`} onClose={onClose}
      actions={<>
        {label && <Button variant="quiet" onClick={() => { saveLabel(v.id, null); onClose() }}>Remove name</Button>}
        <Button variant="quiet" onClick={onClose}>Cancel</Button>
        <Button variant="primary" disabled={!text.trim()} onClick={() => { saveLabel(v.id, text); onClose() }}>Save</Button>
      </>}>
      <input autoFocus className={fieldCls} value={text} maxLength={80} placeholder="e.g. Before the final boss" onChange={e => setText(e.target.value)} aria-label="Name" />
      <div className="flex gap-1.5 flex-wrap">
        {SUGGEST.map(s => (
          <button key={s} type="button" onClick={() => setText(s)}
            className="rounded-full border border-line bg-raise px-2.5 py-1 text-[11.5px] text-fg hover:bg-hover cursor-pointer">{s}</button>
        ))}
      </div>
      <p className="rounded-lg border border-safe-line bg-safe-soft text-safe-ink px-3 py-2 text-[12px]">
        A named save is never removed when old versions are pruned. Removing the name later leaves Protect as it was.
      </p>
    </Dialog>
  )
}

export function NamedStrip({ list, labels, onRename }: { list: Version[]; labels: Record<string, string | null>; onRename: (v: Version) => void }) {
  const named = list.filter(v => labels[v.id])
  return (
    <div className="px-4 py-3 border-b border-line">
      <div className="text-[10px] tracking-[0.12em] uppercase text-faint mb-2">Named saves · kept until you remove the name</div>
      {named.length === 0
        ? <p className="text-[12px] text-dim">Name a version with “Name…” to pin it here.</p>
        : <div className="grid grid-cols-[repeat(auto-fill,minmax(190px,1fr))] gap-2.5">
            {named.map(v => (
              <div key={v.id} className="rounded-xl border border-info-line bg-info-soft px-3 py-2.5 flex flex-col gap-1">
                <b className="text-[13px] text-fg">{labels[v.id]}</b>
                <span className="text-[11.5px] text-dim">{ago(v.createdAt)} · {v.machineName}</span>
                <span className="flex gap-1.5"><Button size="sm" onClick={() => toast('Set as Latest: every machine that syncs Hollow Knight is told to pull it.')}>Set as Latest</Button>
                  <Button size="sm" variant="quiet" onClick={() => onRename(v)}>Rename</Button></span>
              </div>
            ))}
          </div>}
    </div>
  )
}
