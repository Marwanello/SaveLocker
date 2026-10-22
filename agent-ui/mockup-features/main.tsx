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
import { goToFeature } from './store'

installFetchMock()
// The controller mockup's fixtures open with a conflict and a folder prompt; neither is part of these features.
db.conflictOpen = false
folderSuggestions.length = 0
initLook()
goToFeature()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <FeatureFrame>
      <App />
    </FeatureFrame>
  </StrictMode>,
)
