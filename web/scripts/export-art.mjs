// Renders every raster brand asset from its SVG source, deterministically:
//
//   node scripts/export-art.mjs           write everything
//   node scripts/export-art.mjs --check   write nothing; exit 1 if any file on disk differs
//
// Sources of truth: the Pixel lock mark (web/src/assets/marks/pixel-lock.svg, already tied to
// appearance.ts by run-appearance-consistency-tests) and the favicon tile (web/public/favicon.svg). The
// four Steam pieces are the same lockup at four crops, written as SVG to packaging/linux/artwork/src/ and
// rasterised to packaging/linux/artwork/dist/ — an accent change is a re-export, not a redraw. They are
// fixed to the default mark and Ember: a Steam shortcut's art is a file on disk, which the Appearance
// setting cannot repaint. The agent CAN, though: it repaints them itself for the accent and mark in effect
// (src/Agent.Linux/Art), compositing the LAYER files this script also writes to src/Agent.Linux/Art/layers/
// — text and mark shapes rasterised once, here, by the same renderer as everything else, and tinted at run
// time. tests/SaveLocker.Agent.Tests/SteamArtRendererTests holds the two paths to the same picture.
//
// Text is drawn with the Archivo files the Deck UI already embeds (src/Agent.Linux/Ui/Fonts), and only
// those: no system fonts are loaded, so the output does not depend on the machine it ran on.

import { Resvg } from '@resvg/resvg-js'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '..', '..')
const check = process.argv.includes('--check')
const at = (...p) => join(root, ...p)

// Every text file is read without its line endings' carriage returns. A Windows checkout
// (core.autocrlf) holds the sources AND the outputs with CRLF, so reading them raw copied CRLF into the
// generated SVGs and made --check report files it had no quarrel with.
const readText = path => readFileSync(path, 'utf8').replaceAll('\r\n', '\n')

const EMBER = '#e0533c'
const FONTS = [
  at('src', 'Agent.Linux', 'Ui', 'Fonts', 'Archivo-Regular.ttf'),
  at('src', 'Agent.Linux', 'Ui', 'Fonts', 'Archivo-SemiBold.ttf'),
]

const MARK_FILES = { pixel: 'pixel-lock', cartridge: 'cartridge', memcard: 'memory-card' }

/** A mark's shapes, straight from the shipped SVG. Without colours the CSS variables resolve to their
 *  Ember defaults; with them, to the two given (the layer files use pure red and blue as placeholders). */
function markShapes(id = 'pixel', accent, on) {
  const svg = readText(at('web', 'src', 'assets', 'marks', `${MARK_FILES[id]}.svg`))
  const body = svg.slice(svg.indexOf('>') + 1, svg.lastIndexOf('</svg>'))
    .replace(/<title>[\s\S]*?<\/title>/, '')
    .replace(/var\(--color-accent, (#[0-9a-f]{6})\)/gi, accent ?? '$1')
    .replace(/var\(--color-on-accent, (#[0-9a-f]{6})\)/gi, on ?? '$1')
  return body.trim()
}

/** The mark, `size` px square at (x, y). Its own box is 32 units. */
const mark = (x, y, size, id, accent, on) =>
  `<g transform="translate(${x} ${y}) scale(${size / 32})">${markShapes(id, accent, on)}</g>`

// `fg` is "Save", `locker` is "Locker"; 'none' hides a half while keeping the other where it sits.
const wordmark = (x, y, size, fg = '#ffffff', locker = EMBER) =>
  `<text x="${x}" y="${y}" font-family="Archivo" font-weight="600" font-size="${size}" letter-spacing="${-size * 0.035}">` +
  `<tspan fill="${fg}">Save</tspan><tspan fill="${locker}">Locker</tspan></text>`

const tagline = (x, y, size, text, fill = '#ffffff') =>
  `<text x="${x}" y="${y}" font-family="Archivo" font-weight="400" font-size="${size}" letter-spacing="${size * 0.16}" ` +
  `fill="${fill}" fill-opacity=".72">${text.toUpperCase()}</text>`

/** The lockup background: the brand kit's radial wash (accent 42% into #101014, fading to #0b0b0e) and its
 *  faint diagonal hairlines. `color-mix` is resolved to sRGB here, once. */
const backdrop = (w, h) => `
  <defs>
    <radialGradient id="wash" gradientUnits="userSpaceOnUse" cx="${w * 0.22}" cy="${h * 0.08}" r="${Math.max(w, h) * 1.2}">
      <stop offset="0" stop-color="#672c25"/>
      <stop offset=".68" stop-color="#0b0b0e"/>
    </radialGradient>
    <pattern id="lines" width="9" height="9" patternUnits="userSpaceOnUse" patternTransform="rotate(25)">
      <rect width="2" height="9" fill="#ffffff" fill-opacity=".05"/>
    </pattern>
  </defs>
  <rect width="${w}" height="${h}" fill="url(#wash)"/>
  <rect width="${w}" height="${h}" fill="url(#lines)"/>`

const svgDoc = (w, h, inner) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">\n${inner}\n</svg>\n`

// ---- the four Steam pieces -----------------------------------------------------------------------------
// The vertical capsule keeps the wordmark low and the mark high on purpose: Steam overlays a progress bar
// and a Play badge across the LOWER THIRD of a grid tile, so the wordmark sits above that band.
const PIECES = {
  'capsule': { w: 600, h: 900, wash: true, mark: [64, 72, 170], word: [58, 600, 88], tag: [64, 656, 20, 'self-hosted save sync'] },
  'capsule-wide': { w: 920, h: 430, wash: true, mark: [86, 120, 190], word: [330, 250, 98], tag: [334, 302, 22, 'your saves, on every machine'] },
  'hero': { w: 1920, h: 620, wash: true, mark: [110, 300, 200], word: [350, 440, 150],
    tag: [356, 500, 24, 'hub-and-spoke save sync · windows · linux · steam deck'] },
  // Transparent: Steam lays it over the hero. White "Save" so it reads on that dark banner.
  'logo': { w: 1000, h: 340, wash: false, mark: [20, 60, 220], word: [280, 200, 128], tag: null },
}

const STEAM = Object.fromEntries(Object.entries(PIECES).map(([name, p]) => [name,
  svgDoc(p.w, p.h, (p.wash ? backdrop(p.w, p.h) : '') + mark(...p.mark) + wordmark(...p.word) +
    (p.tag ? tagline(...p.tag) : ''))]))

// ---- the layers the agent tints at run time ------------------------------------------------------------
// One transparent picture per thing that changes with the look, drawn in placeholder colours that the agent
// maps back: `white` is the white text (its alpha), `accent` the accent text (alpha), and each mark is
// rasterised with red where the accent goes and blue where the on-accent ink goes — so a pixel's blue share
// says how much ink it is, and anti-aliased seams between the two come out right for any pair of colours.
const LAYER_MARKS = ['pixel', 'cartridge', 'memcard']
function layerSvgs(name) {
  const p = PIECES[name]
  const out = {
    white: svgDoc(p.w, p.h, wordmark(...p.word, '#ffffff', 'none') + (p.tag ? tagline(...p.tag) : '')),
    accent: svgDoc(p.w, p.h, wordmark(p.word[0], p.word[1], p.word[2], 'none', '#ff0000')),
  }
  for (const id of LAYER_MARKS) out[`mark-${id}`] = svgDoc(p.w, p.h, mark(...p.mark, id, '#ff0000', '#0000ff'))
  return out
}

// ---- rendering -----------------------------------------------------------------------------------------
function render(svg, width) {
  const opts = {
    font: { fontFiles: FONTS, loadSystemFonts: false, defaultFontFamily: 'Archivo' },
    shapeRendering: 2, textRendering: 1, imageRendering: 0,
  }
  if (width) opts.fitTo = { mode: 'width', value: width }
  return new Resvg(svg, opts).render()
}

/**
 * An .ico with a BMP (DIB) image for every size under 256 and a PNG for 256, which is the layout every
 * Windows tool understands — csc's /win32icon, Inno Setup and Explorer alike. The tile is the SAME svg
 * rasterised at each size, never a downscale, so the 16 px entry is drawn on its own grid.
 */
function ico(svg, sizes) {
  const images = sizes.map(size => {
    const r = render(svg, size)
    if (size >= 256) return { size, data: Buffer.from(r.asPng()) }
    // BMP: BITMAPINFOHEADER (height doubled: colour + mask), bottom-up BGRA, then a 1bpp AND mask (all 0 —
    // the alpha channel carries transparency).
    const px = r.pixels, stride = size * 4, maskStride = Math.ceil(size / 32) * 4
    const head = Buffer.alloc(40)
    head.writeUInt32LE(40, 0); head.writeInt32LE(size, 4); head.writeInt32LE(size * 2, 8)
    head.writeUInt16LE(1, 12); head.writeUInt16LE(32, 14); head.writeUInt32LE(0, 16)
    head.writeUInt32LE(stride * size + maskStride * size, 20)
    const bgra = Buffer.alloc(stride * size)
    for (let y = 0; y < size; y++) {
      for (let x = 0; x < size; x++) {
        const s = (y * size + x) * 4, d = ((size - 1 - y) * size + x) * 4
        bgra[d] = px[s + 2]; bgra[d + 1] = px[s + 1]; bgra[d + 2] = px[s]; bgra[d + 3] = px[s + 3]
      }
    }
    return { size, data: Buffer.concat([head, bgra, Buffer.alloc(maskStride * size)]) }
  })
  const header = Buffer.alloc(6)
  header.writeUInt16LE(1, 2); header.writeUInt16LE(images.length, 4)
  let offset = 6 + 16 * images.length
  const dir = images.map(i => {
    const e = Buffer.alloc(16)
    e[0] = i.size >= 256 ? 0 : i.size; e[1] = i.size >= 256 ? 0 : i.size
    e.writeUInt16LE(1, 4); e.writeUInt16LE(32, 6)
    e.writeUInt32LE(i.data.length, 8); e.writeUInt32LE(offset, 12)
    offset += i.data.length
    return e
  })
  return Buffer.concat([header, ...dir, ...images.map(i => i.data)])
}

const MANIFEST = JSON.stringify({
  name: 'SaveLocker',
  short_name: 'SaveLocker',
  description: 'Self-hosted save sync for Windows, Linux and Steam Deck',
  icons: [
    { src: '/android-chrome-192x192.png', sizes: '192x192', type: 'image/png' },
    { src: '/android-chrome-512x512.png', sizes: '512x512', type: 'image/png' },
  ],
  theme_color: '#0f0f10',
  background_color: '#0f0f10',
  display: 'standalone',
}) + '\n'

// ---- the plan: every file this script owns ------------------------------------------------------------
const faviconSvg = readText(at('web', 'public', 'favicon.svg'))
const files = new Map()   // absolute path -> Buffer | string

for (const [name, svg] of Object.entries(STEAM)) {
  files.set(at('packaging', 'linux', 'artwork', 'src', `${name}.svg`), svg)
  files.set(at('packaging', 'linux', 'artwork', 'dist', `${name}.png`), Buffer.from(render(svg).asPng()))
}
for (const name of Object.keys(PIECES)) {
  for (const [layer, svg] of Object.entries(layerSvgs(name))) {
    files.set(at('src', 'Agent.Linux', 'Art', 'layers', `${name}.${layer}.png`), Buffer.from(render(svg).asPng()))
  }
}
for (const [name, size] of [['favicon-16x16', 16], ['favicon-32x32', 32], ['apple-touch-icon', 180],
                            ['android-chrome-192x192', 192], ['android-chrome-512x512', 512]]) {
  files.set(at('web', 'public', `${name}.png`), Buffer.from(render(faviconSvg, size).asPng()))
}
// The application-menu icon install.sh points the .desktop entry at.
files.set(at('packaging', 'linux', 'artwork', 'dist', 'icon.png'), Buffer.from(render(faviconSvg, 256).asPng()))
files.set(at('web', 'public', 'site.webmanifest'), MANIFEST)

// The agent UI is installable as a PWA from any Chromium too (`savelocker open` prefers an --app= window,
// but a person can also use the browser's own "Install"), and until now it had no icon at all. Its page is
// served without a token gate on static files, so the manifest and icons load like the rest of the bundle.
const AGENT_MANIFEST = JSON.stringify({
  name: 'SaveLocker Agent',
  short_name: 'SaveLocker',
  description: 'SaveLocker on this machine',
  start_url: '/?app',
  scope: '/',
  icons: [
    { src: '/android-chrome-192x192.png', sizes: '192x192', type: 'image/png' },
    { src: '/android-chrome-512x512.png', sizes: '512x512', type: 'image/png' },
  ],
  theme_color: '#0f0f10',
  background_color: '#0f0f10',
  display: 'standalone',
}) + '\n'
files.set(at('agent-ui', 'public', 'site.webmanifest'), AGENT_MANIFEST)
files.set(at('agent-ui', 'public', 'favicon.svg'), faviconSvg)
files.set(at('agent-ui', 'public', 'android-chrome-192x192.png'), Buffer.from(render(faviconSvg, 192).asPng()))
files.set(at('agent-ui', 'public', 'android-chrome-512x512.png'), Buffer.from(render(faviconSvg, 512).asPng()))
files.set(at('src', 'Agent', 'Assets', 'favicon.png'), Buffer.from(render(faviconSvg, 64).asPng()))
const icoBytes = ico(faviconSvg, [16, 24, 32, 48, 64, 128, 256])
files.set(at('web', 'public', 'favicon.ico'), icoBytes)
files.set(at('src', 'Agent', 'Assets', 'SaveLocker.ico'), icoBytes)

let differing = 0
for (const [path, content] of files) {
  const isText = !Buffer.isBuffer(content)
  const next = isText ? Buffer.from(content) : content
  const same = existsSync(path) && (isText ? readText(path) === content : readFileSync(path).equals(next))
  if (check) {
    if (!same) { differing++; console.log(`differs  ${relative(root, path)}`) }
    continue
  }
  mkdirSync(dirname(path), { recursive: true })
  if (!same) writeFileSync(path, next)
  console.log(`${same ? 'same    ' : 'wrote   '} ${relative(root, path)}`)
}
if (check && differing) { console.log(`${differing} file(s) differ — run npm run export:art`); process.exit(1) }
