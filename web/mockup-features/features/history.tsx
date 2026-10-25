import { useState } from 'react'
import { Button } from '../../src/components/ui/Button'
import { Card } from '../../src/components/ui/Card'
import { Chip } from '../../src/components/ui/Chip'
import { DataTable } from '../../src/components/ui/DataTable'
import { Icon } from '../../src/components/ui/Icon'
import { Seg } from '../../src/components/ui/Seg'
import { fmtSize } from '../../src/format'
import { toast } from '../../src/toast'
import { getState, setState, useMock, useVariant, type TrashItem } from '../store'
import { Choice, Dialog, fieldCls } from './common'

/* ------------------------------------------------------------------ retention planner (plan.md Phase 3–4) */

interface HV { age: number; size: number; head?: boolean; label?: string; prot?: boolean }
const HIST: HV[] = (() => {
  const out: HV[] = []
  let seed = 7
  const r = () => (seed = (seed * 9301 + 49297) % 233280) / 233280
  for (let d = 0; d < 120; d++) {
    const n = d < 4 ? 5 : r() < 0.55 ? Math.ceil(r() * 3) : 0
    for (let i = 0; i < n; i++) out.push({ age: d + i * 0.11 + 0.05, size: 1_400_000 + Math.round(r() * 120_000) })
  }
  out[0].head = true; out[9].label = 'Before the Radiance'; out[40].prot = true; out[71].label = 'Pantheon 5 start'
  return out
})()
const PRESETS: Record<string, [number, number, number]> = { short: [2, 14, 8], balanced: [3, 30, 26], long: [7, 90, 0] }
interface Policy { mode: 'count' | 'thin'; a: number; d: number; w: number; cap: number; count: number }

function plan(p: Policy) {
  const keep = new Set<HV>()
  const pinned = HIST.filter(v => v.head || v.label || v.prot)
  pinned.forEach(v => keep.add(v))
  if (p.mode === 'count') HIST.slice(0, p.count).forEach(v => keep.add(v))
  else {
    const day: Record<number, 1> = {}, week: Record<number, 1> = {}
    for (const v of HIST) {
      if (v.age < p.a) keep.add(v)
      else if (v.age < p.a + p.d) { const k = Math.floor(v.age); if (!day[k]) { day[k] = 1; keep.add(v) } }
      else if (!p.w || v.age < p.a + p.d + p.w * 7) { const k = Math.floor((v.age - p.a - p.d) / 7); if (!week[k]) { week[k] = 1; keep.add(v) } }
    }
    if (p.cap) [...keep].filter(v => !pinned.includes(v)).sort((x, y) => x.age - y.age).slice(Math.max(0, p.cap - pinned.length)).forEach(v => keep.delete(v))
  }
  const drop = HIST.filter(v => !keep.has(v))
  return { keep, drop, bytes: drop.reduce((s, v) => s + v.size, 0) }
}
const ageText = (a: number) => (a < 1 ? 'today' : a < 2 ? 'yesterday' : `${Math.floor(a)} days ago`)
const why = (v: HV, p: Policy) => v.head ? 'Latest' : v.label ? `Named “${v.label}”` : v.prot ? 'Protected'
  : p.mode === 'count' ? `Among the newest ${p.count}` : v.age < p.a ? `Within ${p.a} days` : v.age < p.a + p.d ? 'Newest of its day' : 'Newest of its week'

/** “How much to keep”: the planned retention planner, in the variant on show. `game` is one game's own setting (opened from
 *  its Versions card, and it can follow the default); `default` is the server-wide default in Configuration. */
export function KeepHistory({ scope, onClose }: { scope: 'game' | 'default'; onClose: () => void }) {
  const variant = useVariant('vh-keep')!
  const [own, setOwn] = useState(false)
  const [mode, setMode] = useState<'count' | 'thin'>('thin')
  const [preset, setPreset] = useState('balanced')
  const [custom, setCustom] = useState<[number, number, number]>([3, 30, 26])
  const [cap, setCap] = useState(0)
  const [count, setCount] = useState(10)
  const [showList, setShowList] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [level, setLevel] = useState(3)
  const [advanced, setAdvanced] = useState(false)

  let p: Policy
  if (scope === 'game' && !own) {
    p = { mode: 'thin', a: 3, d: 30, w: 26, cap: 0, count: 10 }
  } else if (variant === 'C' && !advanced) {
    const map: Record<number, Policy> = {
      1: { mode: 'count', a: 0, d: 0, w: 0, cap: 0, count: 10 },
      2: { mode: 'thin', a: 2, d: 14, w: 8, cap: 0, count: 10 },
      3: { mode: 'thin', a: 3, d: 30, w: 26, cap: 0, count: 10 },
      4: { mode: 'thin', a: 7, d: 90, w: 0, cap: 0, count: 10 },
    }
    p = map[level]
  } else {
    const [a, d, w] = preset === 'custom' ? custom : PRESETS[preset]
    p = { mode, a, d, w, cap, count }
  }
  const r = plan(p)
  const total = HIST.reduce((s, v) => s + v.size, 0)
  const sentence = p.mode === 'count' ? `Keeps the newest ${p.count} versions of each playthrough.`
    : `Everything from the last ${p.a} days, then one a day for ${p.d} days, then one a week ${p.w ? `for ${p.w} weeks` : 'forever'}.`

  const controls = (
    <div className="flex flex-col gap-2.5">
      <div className="flex items-center gap-2 flex-wrap">
        <Seg size="sm" value={mode} onChange={setMode} aria-label="How to keep" options={[{ value: 'count', label: 'By count' }, { value: 'thin', label: 'Thinning' }]} />
        {mode === 'thin' && <Seg size="sm" value={preset} onChange={setPreset} aria-label="Preset"
          options={[{ value: 'short', label: 'Short' }, { value: 'balanced', label: 'Balanced' }, { value: 'long', label: 'Long' }, { value: 'custom', label: 'Custom' }]} />}
        {mode === 'count' && <label className="flex items-center gap-1.5 text-[12.5px] text-dim">Keep the newest
          <input type="number" min={1} className={`${fieldCls} w-[70px] tabular-nums`} value={count} onChange={e => setCount(Math.max(1, Number(e.target.value) || 1))} /> versions</label>}
      </div>
      {mode === 'thin' && preset === 'custom' && (
        <div className="flex gap-3 flex-wrap text-[12.5px] text-dim">
          {(['Keep everything for', 'then one a day for', 'then one a week for'] as const).map((l, i) => (
            <label key={l} className="flex items-center gap-1.5">{l}
              <input type="number" min={0} className={`${fieldCls} w-[64px] tabular-nums`} value={custom[i]}
                onChange={e => { const n = [...custom] as [number, number, number]; n[i] = Math.max(0, Number(e.target.value) || 0); setCustom(n) }} />
              {i === 2 ? 'weeks (0 = forever)' : 'days'}</label>
          ))}
        </div>
      )}
      {mode === 'thin' && (
        <label className="flex items-center gap-1.5 text-[12.5px] text-dim">and never more than
          <input type="number" min={0} className={`${fieldCls} w-[64px] tabular-nums`} value={cap} onChange={e => setCap(Math.max(0, Number(e.target.value) || 0))} /> versions (0 = no limit)</label>
      )}
      <p className="text-[12.5px] text-dim">{sentence} Named, protected and latest versions are always kept.</p>
    </div>
  )

  const timeline = (
    <div className="overflow-x-auto">
      <div className="min-w-[560px] relative h-[104px] mt-1">
        {p.mode === 'thin' && [[0, p.a, 'All'], [p.a, p.a + p.d, 'One a day'], [p.a + p.d, p.w ? p.a + p.d + p.w * 7 : 120, 'One a week']].map(([from, to, l], i) => (
          <div key={i} className={`absolute top-0 bottom-6 border-r border-dashed border-line ${['bg-safe-soft', 'bg-info-soft', 'bg-raise'][i]}`}
            style={{ left: `${(Number(from) / 120) * 100}%`, width: `${((Math.min(Number(to), 120) - Number(from)) / 120) * 100}%` }}>
            <span className="absolute top-1 left-1.5 text-[10.5px] text-dim whitespace-nowrap">{l}</span>
          </div>
        ))}
        <div className="absolute inset-x-0 top-[58px] h-px bg-line" />
        {HIST.map((v, i) => {
          const kept = r.keep.has(v), pin = v.head || v.label || v.prot
          return <span key={i} title={`${ageText(v.age)} · ${kept ? why(v, p) : 'to trash'}`}
            className={`absolute rounded-full ${pin ? 'w-2.5 h-2.5 rounded-[3px]' : 'w-[7px] h-[7px]'} ${kept ? (pin ? 'bg-info' : 'bg-safe') : 'border-[1.5px] border-accent'}`}
            style={{ left: `${(v.age / 120) * 100}%`, top: 52 - (i % 3) * 7 }} />
        })}
        {[0, 30, 60, 90, 120].map(d => <span key={d} className="absolute bottom-0 font-mono text-[10.5px] text-faint" style={{ left: `${(d / 120) * 100}%`, transform: `translateX(${d === 120 ? '-100%' : d ? '-50%' : '0'})` }}>{d ? `${d} d` : 'now'}</span>)}
      </div>
      <div className="flex gap-3.5 flex-wrap text-[11.5px] text-dim mt-1.5">
        <span className="flex items-center gap-1.5"><i className="w-2 h-2 rounded-full bg-safe" />Kept</span>
        <span className="flex items-center gap-1.5"><i className="w-2 h-2 rounded-full border-[1.5px] border-accent" />To trash</span>
        <span className="flex items-center gap-1.5"><i className="w-2 h-2 rounded-[3px] bg-info" />Named, protected or latest</span>
      </div>
    </div>
  )

  const summary = (
    <div className={`flex items-center justify-between gap-3 flex-wrap rounded-xl border px-3.5 py-2.5 ${r.drop.length ? 'border-watch-line bg-watch-soft' : 'border-safe-line bg-safe-soft'}`}>
      <span className={`text-[13px] ${r.drop.length ? 'text-watch-ink' : 'text-safe-ink'}`}>
        {r.drop.length ? <>Saving this moves <b>{r.drop.length} versions ({fmtSize(r.bytes)})</b> to trash. Keeps {r.keep.size} of {HIST.length}.</> : 'Nothing would be removed.'}
      </span>
      <span className="flex gap-1.5">
        {variant === 'A' && r.drop.length > 0 && <Button size="sm" onClick={() => setShowList(!showList)}>{showList ? 'Hide' : 'Show which'}</Button>}
        <Button size="sm" variant="primary" onClick={() => (r.drop.length ? setConfirm(true) : (toast('Saved. Nothing to remove.'), onClose()))}>Save</Button>
      </span>
    </div>
  )

  const bar = (label: string, n: number, bytes: number, cls: string) => (
    <div className="flex flex-col gap-1">
      <div className="flex justify-between text-[12px] text-dim"><span>{label}</span><span className="font-mono tabular-nums">{n} versions · {fmtSize(bytes)}</span></div>
      <div className="h-2 rounded-full bg-tile overflow-hidden"><i className={`block h-full ${cls}`} style={{ width: `${(bytes / total) * 100}%` }} /></div>
    </div>
  )

  const follows = scope === 'game' && !own
  return (
    <>
      <Dialog wide onClose={onClose}
        title={scope === 'game' ? 'Keep history · Hollow Knight' : 'History to keep · every game'}
        sub={scope === 'game' ? `Main playthrough · ${HIST.length} versions · ${fmtSize(total)}` : 'Each playthrough of a game that has no setting of its own. Shown on Hollow Knight’s history.'}>
        <div className="flex flex-col gap-3">
          {scope === 'game' && (
            <div className="grid sm:grid-cols-2 gap-2" role="radiogroup" aria-label="Which setting">
              <Choice checked={!own} onPick={() => setOwn(false)} title="Use the default" sub="Thinning · Balanced, set in Configuration" />
              <Choice checked={own} onPick={() => setOwn(true)} title="Its own setting" sub="Only Hollow Knight" />
            </div>
          )}
          {follows ? <p className="text-[12.5px] text-dim">{sentence} Named, protected and latest versions are always kept.</p> : variant === 'C' && !advanced ? (
            <>
              <input type="range" min={1} max={4} value={level} onChange={e => setLevel(Number(e.target.value))} aria-label="How much history"
                className="w-full accent-[var(--color-accent)]" />
              <div className="flex justify-between text-[11.5px] text-dim"><span>Least space · last 10</span><span>Short</span><span>Balanced</span><span>Most history · Long</span></div>
              <p className="text-[12.5px] text-dim">{sentence}</p>
            </>
          ) : controls}
          {!follows && variant === 'C' && <label className="flex items-center gap-1.5 text-[12px] text-dim cursor-pointer"><input type="checkbox" className="accent-[var(--color-accent)]" checked={advanced} onChange={e => setAdvanced(e.target.checked)} />Advanced</label>}
          {variant === 'B' && timeline}
          {variant === 'C' && <>{bar('Now', HIST.length, total, 'bg-info')}{bar('After saving', r.keep.size, total - r.bytes, 'bg-safe')}</>}
          {follows ? <div className="flex justify-end"><Button size="sm" variant="primary" onClick={() => { toast('Hollow Knight follows the default.'); onClose() }}>Save</Button></div> : summary}
          {variant === 'A' && showList && (
            <div className="border border-line rounded-xl max-h-[220px] overflow-y-auto">
              {HIST.slice(0, 50).map((v, i) => (
                <div key={i} className="flex items-center justify-between gap-3 px-3.5 py-2 border-b border-row last:border-b-0 text-[12.5px]">
                  <span className="text-fg">{ageText(v.age)}</span>{r.keep.has(v) ? <Chip tone="ok">{why(v, p)}</Chip> : <Chip tone="warn">To trash</Chip>}
                </div>
              ))}
            </div>
          )}
        </div>
      </Dialog>
      {confirm && (
        <Dialog title={`Move ${r.drop.length} versions to trash?`} sub={scope === 'game' ? 'Hollow Knight · Main' : 'Every game that uses the default · 61 versions in all'} onClose={() => setConfirm(false)}
          actions={<><Button variant="quiet" onClick={() => setConfirm(false)}>Cancel</Button>
            <Button variant="primary" onClick={() => { setConfirm(false); onClose(); toast(`Saved. ${r.drop.length} versions moved to trash for 14 days.`) }}>Save and move to trash</Button></>}>
          <p>With this setting, {r.drop.length} versions ({fmtSize(r.bytes)}) go to trash now. You can restore them from Storage › Trash for 14 days. Named, protected and latest versions are always kept.</p>
        </Dialog>
      )}
    </>
  )
}

/* ------------------------------------------------------------------ trash */

export function restoreTrash(t: TrashItem, withPlaythrough = false) {
  setState({ trash: getState().trash.filter(x => x.id !== t.id) })
  toast(`Restored to ${t.game} · ${t.pt}${withPlaythrough ? ' (the playthrough is back too)' : ''}. It isn’t Latest; use Set as Latest if you want it.`, 4500)
}

/** Restore one trashed version, asking first when its playthrough was deleted with it. */
export function useRestore() {
  const [gone, setGone] = useState<TrashItem | null>(null)
  const ask = (t: TrashItem) => (t.ptGone ? setGone(t) : restoreTrash(t))
  const dialog = gone && (
    <Dialog title="Restore the playthrough too?" onClose={() => setGone(null)}
      actions={<><Button variant="quiet" onClick={() => setGone(null)}>Cancel</Button><Button variant="primary" onClick={() => { restoreTrash(gone, true); setGone(null) }}>Restore playthrough and version</Button></>}>
      <p>This version belonged to <b className="text-fg">{gone.pt}</b>, which was deleted. Restoring it brings the playthrough back with its versions. No device plays it until you switch one.</p>
    </Dialog>
  )
  return { ask, dialog }
}

export function EmptyTrashDialog({ onClose }: { onClose: () => void }) {
  const trash = getState().trash
  return (
    <Dialog title="Empty trash?" onClose={onClose}
      actions={<><Button variant="quiet" onClick={onClose}>Cancel</Button><Button variant="alert" onClick={() => { setState({ trash: [] }); onClose(); toast('Trash emptied.') }}>Delete {trash.length} versions</Button></>}>
      <p>Permanently deletes {trash.length} versions ({fmtSize(trash.reduce((s, t) => s + t.size, 0))}). They can’t be restored afterwards.</p>
    </Dialog>
  )
}

export function TrashDays() {
  const days = useMock(s => s.trashDays)
  return (
    <label className="flex items-center gap-1.5 text-[11.5px] text-dim">Keep deleted versions for
      <select className={`${fieldCls} w-auto`} value={days} onChange={e => setState({ trashDays: Number(e.target.value) })}>
        {[7, 14, 30, 60, 90].map(n => <option key={n} value={n}>{n}</option>)}
      </select> days</label>
  )
}

const leftChip = (t: TrashItem) => <Chip tone={t.left <= 3 ? 'warn' : 'default'}>{t.left} days left</Chip>

/** A · the Trash card on Backups. */
export function TrashCard() {
  const trash = useMock(s => s.trash)
  const days = useMock(s => s.trashDays)
  const [empty, setEmpty] = useState(false)
  const { ask, dialog } = useRestore()
  return (
    <>
      <Card title="Trash" flush headerRight={<span className="flex items-center gap-2 flex-wrap"><TrashDays /><Button size="sm" disabled={!trash.length} onClick={() => setEmpty(true)}>Empty trash…</Button></span>}>
        <p className="px-4 py-3 text-xs text-dim border-b border-line">
          Pruned and deleted versions wait here for {days} days before they’re gone for good. {trash.length} versions · {fmtSize(trash.reduce((s, t) => s + t.size, 0))}.
        </p>
        <DataTable caption="Deleted versions" rows={trash} rowKey={t => t.id}
          empty={<div className="px-4 py-5 text-[12.5px] text-dim">Trash is empty.</div>}
          columns={[
            { head: 'Game', kind: 'k', cell: t => <span>{t.game}<span className="block text-[11.5px] text-dim font-normal">{t.pt}</span></span> },
            { head: 'Version', cell: t => t.made },
            { head: 'Why', cell: t => t.why },
            { head: 'Size', kind: 'n', cell: t => fmtSize(t.size) },
            { head: 'Gone in', cell: leftChip },
            { head: '', label: 'Actions', end: true, cell: t => <Button size="sm" onClick={() => ask(t)}>Restore</Button> },
          ]} />
      </Card>
      {empty && <EmptyTrashDialog onClose={() => setEmpty(false)} />}
      {dialog}
    </>
  )
}

/** C · Configuration's storage line and its “Recently deleted” panel. */
export function RecentlyDeleted() {
  const trash = useMock(s => s.trash)
  const [open, setOpen] = useState(false)
  const [picked, setPicked] = useState<Record<string, boolean>>({})
  const [empty, setEmpty] = useState(false)
  const n = Object.values(picked).filter(Boolean).length
  const games = [...new Set(trash.map(t => t.game))]
  return (
    <>
      <Card title="Storage" headerRight={<TrashDays />}>
        <div className="flex flex-col gap-2.5">
          <div className="h-2 rounded-full bg-tile overflow-hidden flex"><i className="block h-full bg-info" style={{ width: '36%' }} /><i className="block h-full bg-watch" style={{ width: trash.length ? '2%' : 0 }} /></div>
          <div className="flex justify-between gap-3 flex-wrap items-center text-[11.5px] text-dim">
            <span className="flex gap-3 flex-wrap"><span>Versions 14.2 GB</span><span>Recently deleted {fmtSize(trash.reduce((s, t) => s + t.size, 0))}</span><span>Free 17.8 GB of 40 GB</span></span>
            <Button size="sm" onClick={() => setOpen(true)}><Icon name="trash-2" size={13} />Recently deleted ({trash.length})</Button>
          </div>
        </div>
      </Card>
      {open && (
        <div className="fixed inset-0 z-40 flex justify-end bg-black/30" onClick={e => { if (e.target === e.currentTarget) setOpen(false) }}>
          <aside className="w-full max-w-[420px] h-full bg-panel border-l border-line flex flex-col animate-rise" aria-label="Recently deleted">
            <div className="flex items-center justify-between px-5 py-4 border-b border-line"><h2 className="text-[15px] font-semibold text-fg">Recently deleted</h2>
              <button type="button" aria-label="Close" onClick={() => setOpen(false)} className="p-1 rounded-md text-dim hover:text-fg hover:bg-hover cursor-pointer"><Icon name="x" size={16} /></button></div>
            <div className="flex-1 overflow-y-auto p-5 flex flex-col gap-4">
              {games.length === 0 && <p className="text-[12.5px] text-dim">Nothing here.</p>}
              {games.map(g => (
                <div key={g}>
                  <div className="text-[10px] tracking-[0.12em] uppercase text-faint mb-1.5">{g}</div>
                  <div className="border border-line rounded-xl overflow-hidden">
                    {trash.filter(t => t.game === g).map(t => (
                      <label key={t.id} className="flex gap-3 items-start px-3.5 py-2.5 border-b border-row last:border-b-0 cursor-pointer">
                        <input type="checkbox" className="mt-1 accent-[var(--color-accent)]" checked={!!picked[t.id]} onChange={e => setPicked({ ...picked, [t.id]: e.target.checked })} />
                        <span className="flex-1 min-w-0"><span className="block text-[13px] text-fg">{t.made}</span><span className="text-[11.5px] text-dim">{t.pt} · {t.why} · {fmtSize(t.size)}</span>
                          <span className="block h-1 rounded-full bg-tile mt-1.5 overflow-hidden"><i className={`block h-full ${t.left <= 3 ? 'bg-watch' : 'bg-faint'}`} style={{ width: `${(t.left / 14) * 100}%` }} /></span></span>
                        <span className="font-mono text-[11px] text-dim">{t.left} d</span>
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <div className="flex justify-end gap-2 px-5 py-3 border-t border-line">
              <Button variant="quiet" disabled={!trash.length} onClick={() => setEmpty(true)}>Empty…</Button>
              <Button variant="primary" disabled={!n} onClick={() => {
                setState({ trash: trash.filter(t => !picked[t.id]) }); setPicked({}); toast(`Restored ${n} version${n === 1 ? '' : 's'}.`)
              }}>{n ? `Restore ${n}` : 'Restore'}</Button>
            </div>
          </aside>
        </div>
      )}
      {empty && <EmptyTrashDialog onClose={() => setEmpty(false)} />}
    </>
  )
}
