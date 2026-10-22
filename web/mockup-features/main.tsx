import './session'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@fontsource/archivo/400.css'
import '@fontsource/archivo/500.css'
import '@fontsource/archivo/600.css'
import '@fontsource/archivo/700.css'
import '@fontsource/jetbrains-mono/400.css'
import '@fontsource/jetbrains-mono/500.css'
import './console.css'
import App from '../src/App'
import { initLook } from '../src/appearance'
import { installFetchMock } from './fetchMock'
import { FeatureFrame } from './FeatureFrame'

installFetchMock()
initLook()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <FeatureFrame>
      <App />
    </FeatureFrame>
  </StrictMode>,
)
