import { useCallback, useEffect, useState } from 'react'
import { api } from '../api'
import { formatAgo, formatBytes } from '../format'
import type { DlcCheck } from '../types'
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

/**
 * The DLC check on a game's page (SaveLocker tasks/emulator-saves Phase 5c): what the latest save was made with, pack by
 * pack, against what is installed here — names, IDs and sizes only, nothing is uploaded. When a pull is held for missing
 * packs, the choice: pull anyway, or wait until they are installed (the pull then goes ahead by itself).
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

  if (!check?.packages || check.packages.length === 0) return null
  const missing = check.packages.filter(p => !p.here)

  async function pullAnyway() {
    setBusy(true)
    try { onPulled((await api.dlcPullAnyway(gameId)).message) }
    catch (err) { onPulled(err instanceof Error ? err.message : 'The pull failed.') }
    finally { setBusy(false); setAsking(false); load() }
  }

  return (
    <>
      {check.held && missing.length > 0 && (
        <Banner
          tone="warn"
          title={`Pull on hold: ${gameName}'s latest save needs ${missing.length} DLC pack${missing.length === 1 ? '' : 's'} this device doesn't have`}
          detail={`${formatBytes(check.missingSize)}. Install them and the pull goes ahead by itself, or pull anyway.`}
          action={<Button variant="primary" onClick={() => setAsking(true)}>Choose</Button>}
        />
      )}
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
            {check.packages.map(p => (
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
      </Card>
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
              <Button variant="primary" disabled={busy} onClick={() => void pullAnyway()}>
                {busy ? 'Pulling…' : 'Pull the save anyway (the save here is still on the server under Versions)'}
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
