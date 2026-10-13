import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { formatAgo, formatBytes } from '../format'
import type { ContentTransfer, DlcCheck } from '../types'
import { Banner } from './ui/Banner'
import { Button } from './ui/Button'
import { Card } from './ui/Card'

interface Props {
  gameId: string
  gameName: string
  /** Bumped by the page after every sync it runs, so the card follows what that sync did. */
  refreshKey: number
  /** A pull from this page was just held: open the choice. */
  askSignal: number
  onPulled: (message: string) => void
}

const OPEN: ContentTransfer['status'][] = ['Requested', 'Approved', 'Ready']
const ENDED: ContentTransfer['status'][] = ['Declined', 'Expired', 'Cancelled']

/**
 * The DLC check on a game's page (SaveLocker tasks/emulator-saves Phase 5c): what the latest save was made with, pack by
 * pack, against what is installed here — names, IDs and sizes only, nothing is uploaded. When a pull is held for missing
 * packs, the choice: pull anyway, have the machine that made the save hand them over through the server, or wait until
 * they are installed (the pull then goes ahead by itself). Requests from other machines for this game's packs are answered
 * here too.
 */
export function DlcCheckCard({ gameId, gameName, refreshKey, askSignal, onPulled }: Props) {
  const [check, setCheck] = useState<DlcCheck | null>(null)
  const [asking, setAsking] = useState(false)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    api.gameDlc(gameId).then(setCheck).catch(() => setCheck(null))
  }, [gameId])

  useEffect(() => { load() }, [load, refreshKey])
  useEffect(() => { if (askSignal > 0) setAsking(true) }, [askSignal])

  // While a hand-over is moving, follow it.
  const moving = [check?.handover, ...(check?.asked ?? [])].some(t => t && OPEN.includes(t.status))
  useEffect(() => {
    if (!moving) return
    const timer = window.setInterval(load, 5000)
    return () => window.clearInterval(timer)
  }, [moving, load])

  const asked = check?.asked ?? []
  const packages = check?.packages ?? []
  if (!check || (packages.length === 0 && asked.length === 0)) return null
  const missing = packages.filter(p => !p.here)
  const handover = check.handover ?? null
  const handoverOpen = !!handover && OPEN.includes(handover.status)
  const canHandOver = !!check.headMachineId && !handoverOpen && missing.length > 0

  async function run(work: () => Promise<string | void>, failed: string) {
    setBusy(true)
    try {
      const message = await work()
      if (message) onPulled(message)
    } catch (err) { onPulled(err instanceof Error ? err.message : failed) }
    finally { setBusy(false); load() }
  }

  async function pullAnyway() {
    await run(async () => (await api.dlcPullAnyway(gameId)).message, 'The pull failed.')
    setAsking(false)
  }

  async function handOver() {
    await run(async () => {
      const t = await api.dlcHandover(gameId)
      const n = `${t.parts.length} pack${t.parts.length === 1 ? '' : 's'}`
      return check?.held
        ? `Asked ${t.fromMachineName} for ${n}. They install here once sent, and the pull goes ahead.`
        : `Asked ${t.fromMachineName} for ${n}. They install here once sent.`
    }, 'The request failed.')
    setAsking(false)
  }

  const answer = (t: ContentTransfer, approve: boolean) =>
    run(async () => { await api.dlcHandoverAnswer(t.id, approve) }, 'The answer failed.')
  const cancel = (t: ContentTransfer) =>
    run(async () => (await api.dlcHandoverCancel(t.id)).message, 'The cancel failed.')

  return (
    <>
      {asked.map(t => (
        <Banner
          key={t.id} tone={t.status === 'Requested' ? 'warn' : 'ok'}
          title={t.status === 'Requested'
            ? `${t.toMachineName} asks for ${t.parts.length} DLC pack${t.parts.length === 1 ? '' : 's'} of ${gameName}`
            : `Sending ${gameName}'s DLC to ${t.toMachineName}`}
          detail={t.status === 'Requested'
            ? `${t.parts.map(p => p.name || p.packageId).join(', ')} · ${formatBytes(totalSize(t))}. They pass through your server and are deleted there once delivered.`
            : transferProgress(t)}
          action={t.status === 'Requested'
            ? <span style={{ display: 'flex', gap: 8 }}>
                <Button variant="primary" disabled={busy} onClick={() => void answer(t, true)}>Send</Button>
                <Button disabled={busy} onClick={() => void answer(t, false)}>Decline</Button>
              </span>
            : <Button disabled={busy} onClick={() => void cancel(t)}>Cancel</Button>}
        />
      ))}
      {handover && (
        <Banner
          tone={ENDED.includes(handover.status) ? 'warn' : 'ok'}
          title={handoverTitle(handover)}
          detail={handoverDetail(handover)}
          action={handoverOpen
            ? <Button disabled={busy} onClick={() => void cancel(handover)}>Cancel</Button>
            : canHandOver ? <Button disabled={busy} onClick={() => void handOver()}>Ask again</Button> : undefined}
        />
      )}
      {check.held && missing.length > 0 && !handoverOpen && (
        <Banner
          tone="warn"
          title={`Pull on hold: ${gameName}'s latest save needs ${missing.length} DLC pack${missing.length === 1 ? '' : 's'} this device doesn't have`}
          detail={`${formatBytes(check.missingSize)}. Install them and the pull goes ahead by itself, or choose another way.`}
          action={<Button variant="primary" onClick={() => setAsking(true)}>Choose</Button>}
        />
      )}
      {packages.length > 0 && (
        <Card
          title="DLC this save was made with"
          headerRight={<span style={{ fontSize: 11.5, color: 'var(--color-dim)' }}>
            {check.headMachineName ? `from ${check.headMachineName}` : ''}{check.headAt ? ` · ${formatAgo(check.headAt)}` : ''} · names and sizes only
          </span>}
          flush
        >
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12.5 }}>
            <thead>
              <tr style={{ textAlign: 'left', color: 'var(--color-faint)', fontSize: 10, letterSpacing: '0.12em', textTransform: 'uppercase' }}>
                <th style={{ padding: '8px 18px', fontWeight: 500 }}>Package</th>
                <th style={{ padding: '8px 18px', fontWeight: 500 }}>Size</th>
                <th style={{ padding: '8px 18px', fontWeight: 500 }}>Here</th>
              </tr>
            </thead>
            <tbody>
              {packages.map(p => (
                <tr key={`${p.kind}/${p.id}`} style={{ borderTop: '1px solid var(--color-line)' }}>
                  <td style={{ padding: '8px 18px' }}>
                    {p.name || p.id}{p.kind === 'update' && <span style={{ color: 'var(--color-dim)' }}> · title update</span>}
                  </td>
                  <td style={{ padding: '8px 18px', color: 'var(--color-dim)' }}>{formatBytes(p.size)}</td>
                  <td style={{ padding: '8px 18px', color: p.here ? 'var(--color-safe-ink)' : 'var(--color-watch-ink)', fontWeight: p.here ? 400 : 600 }}>
                    {p.here ? 'Has it' : 'Missing'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {canHandOver && !check.held && (
            <div style={{
              display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap',
              padding: '10px 18px', borderTop: '1px solid var(--color-line)', fontSize: 12.5, color: 'var(--color-dim)',
            }}>
              <span>{missing.length} missing here · {formatBytes(check.missingSize)}</span>
              <Button variant="primary" disabled={busy} onClick={() => void handOver()}>
                {busy ? 'Asking…' : `Pull missing DLC from ${check.headMachineName ?? 'the other machine'}`}
              </Button>
            </div>
          )}
        </Card>
      )}
      {asking && missing.length > 0 && (
        <div role="dialog" aria-modal="true" aria-label="This save uses DLC that is not here" style={{
          position: 'fixed', inset: 0, zIndex: 1000, background: 'rgba(8,10,12,0.62)', backdropFilter: 'blur(1.5px)',
          display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
        }}>
          <div style={{ width: '100%', maxWidth: 520, background: 'var(--color-panel)', border: '1px solid var(--color-line)', borderRadius: 14, padding: '18px 20px' }}>
            <div style={{ fontSize: 10.5, fontWeight: 700, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--color-dim)' }}>
              Before pulling {gameName}
            </div>
            <div style={{ fontSize: 15, fontWeight: 700, margin: '6px 0 6px' }}>
              {check.headMachineName ?? 'Another machine'} made this save with {missing.length} pack{missing.length === 1 ? '' : 's'} this device doesn't have
            </div>
            <div style={{ fontSize: 12.5, color: 'var(--color-dim)', lineHeight: 1.5, marginBottom: 14 }}>
              {missing.map(p => p.name || p.id).join(', ')} · {formatBytes(check.missingSize)}. A game may refuse to load such a
              save, or leave out what came from those packs.
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {canHandOver && (
                <Button variant="primary" disabled={busy} onClick={() => void handOver()}>
                  Hand them over from {check.headMachineName ?? 'the other machine'} (through your server, deleted once here)
                </Button>
              )}
              <Button variant={canHandOver ? undefined : 'primary'} disabled={busy} onClick={() => void pullAnyway()}>
                {busy ? 'Working…' : 'Pull the save anyway (the save here is still on the server under Versions)'}
              </Button>
              <Button disabled={busy} onClick={() => setAsking(false)}>
                Not now, I'll install them — the pull goes ahead once they're here
              </Button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}

function totalSize(t: ContentTransfer) {
  return t.parts.reduce((n, p) => n + p.size, 0)
}

function transferProgress(t: ContentTransfer) {
  const zip = t.parts.reduce((n, p) => n + (p.zipSize ?? p.size), 0)
  const got = t.parts.reduce((n, p) => n + p.received, 0)
  return `${formatBytes(got)} of ${formatBytes(zip)} on the server.`
}

function handoverTitle(t: ContentTransfer) {
  switch (t.status) {
    case 'Requested': return `Asked ${t.fromMachineName} for the DLC — waiting for it to agree`
    case 'Approved': return `${t.fromMachineName} is sending the DLC`
    case 'Ready': return 'The DLC is on the server — installing it here'
    case 'Declined': return `${t.fromMachineName} declined to send the DLC`
    case 'Expired': return `The DLC never arrived from ${t.fromMachineName}`
    default: return 'The DLC hand-over was cancelled'
  }
}

function handoverDetail(t: ContentTransfer) {
  if (t.status === 'Requested') return `${t.fromMachineName} sees the request on its next check, so it must be on. It lapses after ${new Date(t.expiresAt).toLocaleDateString()}.`
  if (t.status === 'Approved' || t.status === 'Ready') return `${transferProgress(t)} It installs while Xenia is closed, then the held pull goes ahead.`
  return 'Install the packs yourself, ask again, or pull the save anyway.'
}
