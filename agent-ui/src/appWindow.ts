/**
 * Whether this page is the app's own window rather than a tab in a browser: opened by `savelocker open`
 * (which passes `?app` alongside Chromium's `--app=`), or installed as a PWA. Only then does the page draw
 * its own header — a tab already has a title bar above it, and a second one would be chrome inside chrome.
 * The Windows tray hosts this page in WebView2, which is neither, so it keeps the plain header.
 */
export const inAppWindow: boolean = (() => {
  try {
    return new URLSearchParams(window.location.search).has('app')
      || window.matchMedia('(display-mode: standalone)').matches
      || window.matchMedia('(display-mode: window-controls-overlay)').matches
  } catch {
    return false
  }
})()
