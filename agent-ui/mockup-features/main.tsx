import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource/archivo/400.css'
import '@fontsource/archivo/500.css'
import '@fontsource/archivo/600.css'
import '@fontsource/archivo/700.css'
import '@fontsource/jetbrains-mono/400.css'
import '@fontsource/jetbrains-mono/500.css'
import '../src/tokens.css'
import '../src/ui.css'
import '../mockup/mockup.css'
import './features.css'
import App from '../src/App'
import { initLook } from '../src/appearance'
import { installFetchMock } from '../mockup/fetchMock'
import { db, folderSuggestions } from '../mockup/fixtures'
import { FeatureFrame } from './FeatureFrame'
import { HUNTERS_MELON, HUNTERS_RETRO, goToFeature } from './store'

installFetchMock()
// The controller mockup's fixtures open with a conflict and a folder prompt; neither is part of these features.
db.conflictOpen = false
folderSuggestions.length = 0
// The multi-emulator-games branch's demo title: one game, a save tree per emulator, both set up on this Deck.
const hunters = (id: string, emulator: string, path: string, name: string, size: number) => ({
  id, name, path, processNames: [], alias: null, steamAppId: null,
  pullBeforeLaunchEnabled: null, hasSteamCloud: false, pushAfterExitEnabled: true,
  lastPushBytes: size, lastPushAt: new Date(Date.now() - 5 * 3_600_000).toISOString(),
  source: { kind: 'emulator', detail: emulator, tags: ['DS', 'EmuDeck'] },
  release: { console: 'DS', region: 'USA', gameIds: ['AMHE'], languages: ['English'], namedBy: 'No-Intro' },
})
db.games.push(
  hunters(HUNTERS_MELON, 'melonDS', '/home/deck/Emulation/saves/melonds/saves', 'Metroid Prime - Hunters', 262_144),
  hunters(HUNTERS_RETRO, 'RetroArch', '/home/deck/Emulation/saves/retroarch/saves/melonDS DS', 'Metroid Prime - Hunters (RetroArch)', 524_288),
)
initLook()
goToFeature()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <FeatureFrame>
      <App />
    </FeatureFrame>
  </StrictMode>,
)
