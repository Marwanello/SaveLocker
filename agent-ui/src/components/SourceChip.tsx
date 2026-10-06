import { CircleDashed, FolderSearch, Gamepad2, Library, Pencil, Shield, SquarePlay } from 'lucide-react'
import type { components } from '../api-types'

export type GameSource = components['schemas']['GameSourceDto']

/** The first level as people read it. A kind this build does not know shows as itself. */
const KINDS: Record<string, { label: string; Icon: typeof Gamepad2 }> = {
  emulator: { label: 'Emulator', Icon: Gamepad2 },
  steam: { label: 'Steam', Icon: Library },
  heroic: { label: 'Heroic', Icon: Shield },
  playnite: { label: 'Playnite', Icon: SquarePlay },
  'save-folder': { label: 'Save-folder scan', Icon: FolderSearch },
  manual: { label: 'Added by hand', Icon: Pencil },
}

/** How this machine found the game — "Emulator › RetroArch" — with its extra facts as tags beside it. */
export function SourceChip({ source }: { source: GameSource | null | undefined }) {
  if (!source) {
    return (
      <span className="sl-source sl-source--none">
        <CircleDashed size={14} strokeWidth={1.8} aria-hidden="true" />
        <span className="sl-source__kind">Source not recorded on this device</span>
      </span>
    )
  }
  const kind = KINDS[source.kind] ?? { label: source.kind, Icon: CircleDashed }
  return (
    <>
      <span className="sl-source" title="How this device found the game">
        <kind.Icon size={14} strokeWidth={1.8} aria-hidden="true" />
        <span className="sl-source__kind">{kind.label}</span>
        <span className="sl-source__sep" aria-hidden="true">›</span>
        <span className="sl-source__detail">{source.detail}</span>
      </span>
      {(source.tags ?? []).map(t => <span key={t} className="sl-tag">{t}</span>)}
    </>
  )
}
