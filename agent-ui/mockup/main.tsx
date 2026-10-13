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
import './mockup.css'
import App from '../src/App'
import { initLook } from '../src/appearance'
import { fireForTest, type PadAction } from '../src/gamepad/pad'
import { navDebugState } from '../src/gamepad/nav'
import { installFetchMock } from './fetchMock'
import { MockFrame } from './MockFrame'

/* jsdom lacks these; the mock must run under a plain DOM for its own smoke test. */
if (!window.matchMedia) {
  (window as { matchMedia?: unknown }).matchMedia = () => ({
    matches: false,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
  })
}
if (!('createObjectURL' in URL)) {
  (URL as { createObjectURL?: (b: Blob) => string }).createObjectURL = () => `blob:mock/${Math.random().toString(36).slice(2)}`
}
if (!('revokeObjectURL' in URL)) {
  (URL as { revokeObjectURL?: (u: string) => void }).revokeObjectURL = () => {}
}
if (!window.requestAnimationFrame) {
  (window as { requestAnimationFrame?: (cb: FrameRequestCallback) => number }).requestAnimationFrame = cb => window.setTimeout(() => cb(performance.now()), 16) as unknown as number
}
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = () => {}
}

installFetchMock()
initLook()
;(window as { __slPad?: { fire: (a: PadAction) => void } }).__slPad = { fire: fireForTest }
;(window as { __slNav?: { debug: () => unknown } }).__slNav = { debug: navDebugState }

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <MockFrame>
      <App />
    </MockFrame>
  </StrictMode>,
)
