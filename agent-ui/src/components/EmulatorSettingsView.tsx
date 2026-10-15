import type { DlcCheck, TrackedGame } from '../types'
import { EMULATOR_NAME, type EmulatorWithSettings } from '../emulators'
import { DlcCheckCard } from './DlcCheckCard'
import { SourceChip } from './SourceChip'
import { SwitchPlaysIn } from './SwitchPlaysIn'
import { Xbox360SettingsCard } from './Xbox360SettingsCard'
import { XeniaProfileCard } from './XeniaProfileCard'
import { Button } from './ui/Button'
import { PageHead } from './ui/PageHead'
import { Seg } from './ui/Seg'

export type EmulatorTab = 'profile' | 'dlc' | 'sharing'

interface Props {
  game: TrackedGame
  emulator: EmulatorWithSettings
  machineName: string
  tab: EmulatorTab
  onTab: (tab: EmulatorTab) => void
  onBack: () => void
  /** The game page's DLC check, for the counts on the section switch. */
  dlc: DlcCheck | null
  refreshKey: number
  /** A pull from the game page was just held: the DLC section opens its choice. */
  askSignal: number
  onPulled: (message: string) => void
  /** The game's folder or source changed here (a Switch game's synced copy moved): the shell re-reads the list. */
  onChanged: () => void
}

/**
 * One game's emulator options on this machine, reached from the game's page. Only the emulator the game runs in is shown;
 * Xenia's are the Xbox 360 profile at launch, the DLC the latest save needs, and sharing this machine's DLC. A Switch game's
 * are which Switch emulator it plays in here, and its copies in the others. Settings that cover every game of the emulator
 * say so where they appear.
 */
export function EmulatorSettingsView({ game, emulator, machineName, tab, onTab, onBack, dlc, refreshKey, askSignal, onPulled, onChanged }: Props) {
  const missing = dlc?.packages?.filter(p => !p.here).length ?? 0
  const requests = dlc?.asked?.filter(t => t.status === 'Requested').length ?? 0

  if (emulator === 'switch') {
    const runs = game.source?.detail ?? EMULATOR_NAME.switch
    return (
      <div className="sl-page">
        <Button size="sm" variant="quiet" className="sl-back" onClick={onBack}>← {game.name}</Button>
        <PageHead
          title="Emulator settings"
          sub={`${game.name} syncs from ${runs} on ${machineName}. These settings apply to this device.`}
          actions={<SourceChip source={game.source} />}
        />
        <SwitchPlaysIn gameId={game.id} gameName={game.name} machineName={machineName} refreshKey={refreshKey} onChanged={onChanged} />
      </div>
    )
  }

  return (
    <div className="sl-page">
      <Button size="sm" variant="quiet" className="sl-back" onClick={onBack}>← {game.name}</Button>
      <PageHead
        title="Emulator settings"
        sub={`${game.name} runs in ${EMULATOR_NAME[emulator]} on ${machineName}. These settings apply to this device.`}
        actions={<SourceChip source={game.source} />}
      />
      <div>
        <Seg<EmulatorTab>
          aria-label={`${EMULATOR_NAME[emulator]} settings`}
          value={tab}
          onChange={onTab}
          options={[
            { value: 'profile', label: 'Xbox 360 profile' },
            { value: 'dlc', label: missing > 0 ? `DLC and title updates · ${missing} missing` : 'DLC and title updates' },
            { value: 'sharing', label: requests > 0 ? `Sharing DLC · ${requests} asking` : 'Sharing DLC' },
          ]}
        />
      </div>

      {tab === 'profile' && (
        <>
          <XeniaProfileCard gameId={game.id} refreshKey={refreshKey} />
          <Xbox360SettingsCard show="profile" />
        </>
      )}
      {tab === 'dlc' && (
        <DlcCheckCard gameId={game.id} gameName={game.name} refreshKey={refreshKey} askSignal={askSignal} onPulled={onPulled} show="packs" />
      )}
      {tab === 'sharing' && (
        <>
          <Xbox360SettingsCard show="dlc" />
          <DlcCheckCard gameId={game.id} gameName={game.name} refreshKey={refreshKey} askSignal={0} onPulled={onPulled} show="requests" />
        </>
      )}

      <div className="sl-setting__note" style={{ marginTop: 0 }}>
        Only the emulator this game runs in is shown. A game in another emulator lists that emulator's options here.
      </div>
    </div>
  )
}
