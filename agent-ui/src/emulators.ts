import type { TrackedGame } from './types'

/** An emulator that has its own settings for a game. Each one added here gets its own sections in Emulator settings. */
export type EmulatorWithSettings = 'xenia'

export const EMULATOR_NAME: Record<EmulatorWithSettings, string> = {
  xenia: 'Xenia',
}

/** The emulator whose settings a game's page offers, or null when the game isn't run by one that has any. */
export function emulatorSettingsFor(game: TrackedGame): EmulatorWithSettings | null {
  const source = game.source
  if (source?.kind !== 'emulator') return null
  return /xenia/i.test(source.detail) ? 'xenia' : null
}
