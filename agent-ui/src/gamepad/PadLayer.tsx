import { useEffect } from 'react'
import { setActionHandler, setScrollHandler, startInput, stopInput, usePadVisible } from './pad'
import { attachFocusSync, handleAction, scrollActive, useOsk } from './nav'
import { GLYPH_BAR_SIZE } from './glyphs'
import { HintBar } from './HintBar'
import { Osk } from './Osk'

/**
 * The whole controller layer in one mount: starts the input funnel, routes actions into navigation,
 * and renders the hint bar and on-screen keyboard. The real App mounts it; hosts need no wiring.
 */
export function PadLayer() {
  const visible = usePadVisible()
  const osk = useOsk()

  useEffect(() => {
    setActionHandler(handleAction)
    setScrollHandler(scrollActive)
    startInput()
    const detach = attachFocusSync()
    return () => { detach(); stopInput(); setActionHandler(null); setScrollHandler(null) }
  }, [])

  // The strip's height is a variable so the shell can reserve exactly that much (ui.css).
  useEffect(() => {
    const root = document.documentElement
    root.style.setProperty('--sl-hintbar-h', `${GLYPH_BAR_SIZE + 29}px`)
    root.classList.toggle('sl-pad-mode', visible)
    return () => root.classList.remove('sl-pad-mode')
  }, [visible])

  return (
    <>
      {visible && <HintBar />}
      {osk.open && <Osk />}
    </>
  )
}
