// Builds the emulator and console icons every UI draws (tasks/emulator-saves Phase 21) from their upstream sources:
//
//   node scripts/build-platform-icons.mjs           download the sources at their current commit, write everything
//   node scripts/build-platform-icons.mjs --check   no network: every mapped icon exists and draws something
//
// Sources (settled with the maintainer, 2026-10-14; credits in NOTICE-icons.md beside the outputs):
//   emulators  Arcticons line icons (CC BY-SA 4.0), github.com/Arcticons-Team/Arcticons icons/white/<name>.svg
//   consoles   RetroArch's Monochrome theme glyphs (CC BY 4.0), github.com/libretro/retroarch-assets src/xmb/monochrome
//   PlayStation  the PlayStation logo + each console's own number or wordmark, cut out of its Simple Icons wordmark (CC0),
//              github.com/simple-icons/simple-icons icons/<name>.svg at a pinned release (maintainer, 2026-10-19)
//   Xenia      its updated logo from Wikimedia Commons (CC BY-SA 4.0), traced once into scripts/icon-sources/xenia.svg
// PCSX2 and shadPS4 take the PS2 and PS4 glyphs, Model 2 and Supermodel the MAME one (maintainer's calls). RPCS3, ScummVM
// and Cemu have no icon of their own here: their project logos' terms were not checked, and Arcticons has no Cemu, so they
// take their console's glyph (PS3, ScummVM's RetroArch glyph, Wii U) until one is.
//
// Writes one module of <symbol>s for both UIs (web/src and agent-ui/src: platformIcons.generated.ts) and, for the Deck's
// Game Mode UI, the emulator icons' strokes as path strings (src/Agent.Linux/Ui/EmulatorIcons.g.cs) — SvgPath draws those.

import { Resvg } from '@resvg/resvg-js'
import svgpath from 'svgpath'
import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '..', '..')
const at = (...p) => join(root, ...p)
const check = process.argv.includes('--check')

const ARCTICONS = { repo: 'Arcticons-Team/Arcticons', branch: 'main', dir: 'icons/white' }
const RETROARCH = { repo: 'libretro/retroarch-assets', branch: 'master', dir: 'src/xmb/monochrome' }
const SIMPLE_ICONS = { repo: 'simple-icons/simple-icons', ref: '16.35.0', dir: 'icons' }

// Emulator name (ScanCandidate.EmulatorName) → its icon. An Arcticons file name, or a console glyph's id.
const EMULATORS = {
  RetroArch: { arcticons: 'retroarch' },
  Dolphin: { arcticons: 'dolphinemulator', id: 'emu-dolphin' },
  PrimeHack: { arcticons: 'dolphinemulator', id: 'emu-dolphin' },
  PPSSPP: { arcticons: 'ppsspp' },
  DuckStation: { arcticons: 'duckstation' },
  Azahar: { arcticons: 'azahar' },
  Eden: { arcticons: 'eden' },
  Citron: { arcticons: 'citron' },
  Yuzu: { arcticons: 'yuzu' },
  Suyu: { arcticons: 'suyu' },
  Sudachi: { arcticons: 'sudachi' },
  Ryujinx: { arcticons: 'ryujinxandroid', id: 'emu-ryujinx' },
  melonDS: { arcticons: 'melonds' },
  Vita3K: { arcticons: 'vita3k' },
  Xenia: { local: 'xenia' },
  PCSX2: { system: 'ps2' },
  shadPS4: { system: 'ps4' },
  RPCS3: { system: 'ps3' },
  Cemu: { system: 'wiiu' },
  ScummVM: { system: 'scummvm' },
  'Model 2': { system: 'arcade' },
  Supermodel: { system: 'arcade' },
}

// EmuDeck's console folder names (GameSources.SystemLabel) → a RetroArch Monochrome glyph.
const SYSTEMS = {
  psx: 'Sony - PlayStation', ps2: 'Sony - PlayStation 2', ps3: 'Sony - PlayStation 3', ps4: 'Sony - PlayStation 4',
  psp: 'Sony - PlayStation Portable', psvita: 'Sony - PlayStation Vita',
  gc: 'Nintendo - GameCube', wii: 'Nintendo - Wii', wiiu: 'Nintendo - Wii U', switch: 'Nintendo - Switch',
  n3ds: 'Nintendo - Nintendo 3DS', nds: 'Nintendo - Nintendo DS', n64: 'Nintendo - Nintendo 64',
  nes: 'Nintendo - Nintendo Entertainment System', snes: 'Nintendo - Super Nintendo Entertainment System',
  fds: 'Nintendo - Family Computer Disk System', virtualboy: 'Nintendo - Virtual Boy',
  gb: 'Nintendo - Game Boy', gbc: 'Nintendo - Game Boy Color', gba: 'Nintendo - Game Boy Advance',
  xbox360: 'Microsoft - Xbox 360', msx: 'Microsoft - MSX',
  genesis: 'Sega - Mega Drive - Genesis', mastersystem: 'Sega - Master System - Mark III', gamegear: 'Sega - Game Gear',
  segacd: 'Sega - Mega-CD - Sega CD', sega32x: 'Sega - 32X', saturn: 'Sega - Saturn', dreamcast: 'Sega - Dreamcast',
  pcengine: 'NEC - PC Engine - TurboGrafx 16', neogeo: 'SNK - Neo Geo', ngp: 'SNK - Neo Geo Pocket',
  ngpc: 'SNK - Neo Geo Pocket Color', wonderswan: 'Bandai - WonderSwan', wonderswancolor: 'Bandai - WonderSwan Color',
  atari2600: 'Atari - 2600', atari7800: 'Atari - 7800', lynx: 'Atari - Lynx',
  arcade: 'MAME', scummvm: 'ScummVM',
}
// Other folder names for a console above.
const SYSTEM_ALIASES = {
  '3ds': 'n3ds', famicom: 'nes', sfc: 'snes', megadrive: 'genesis', tg16: 'pcengine', pcenginecd: 'pcengine',
  mame: 'arcade', fbneo: 'arcade', model2: 'arcade', model3: 'arcade',
}

// The PlayStation consoles: the PlayStation logo, then the glyphs of the console's own wordmark that start at or right of
// `from` (its number, or the PSP / Vita lettering). A number stands top right, a wordmark on the logo's baseline.
const PLAYSTATION = {
  psx: null,
  ps2: { wordmark: 'playstation2', from: 16, kind: 'number' },
  ps3: { wordmark: 'playstation3', from: 16, kind: 'number' },
  ps4: { wordmark: 'playstation4', from: 15, kind: 'number' },
  psp: { wordmark: 'playstationportable', from: 0, kind: 'word' },
  psvita: { wordmark: 'playstationvita', from: 9, kind: 'word' },
}

// A console game's release names its console (ConsoleTitles.ConsoleName, the Switch and Wii U readers) → its glyph.
const CONSOLE_NAMES = {
  PlayStation: 'psx', 'PlayStation 2': 'ps2', 'PlayStation 3': 'ps3', 'PlayStation 4': 'ps4', 'PlayStation Portable': 'psp',
  'PlayStation Vita': 'psvita', GameCube: 'gc', Wii: 'wii', 'Wii U': 'wiiu', 'Nintendo 3DS': 'n3ds', 'Xbox 360': 'xbox360',
  'Nintendo Switch': 'switch',
}

const OUTPUTS = ['web/src/platformIcons.generated.ts', 'agent-ui/src/platformIcons.generated.ts']
const DECK_OUTPUT = 'src/Agent.Linux/Ui/EmulatorIcons.g.cs'

async function commitOf({ repo, branch }) {
  const r = await fetch(`https://api.github.com/repos/${repo}/commits/${branch}`, { headers: { Accept: 'application/vnd.github.sha' } })
  if (!r.ok) throw new Error(`${repo}: ${r.status} reading the ${branch} commit`)
  return (await r.text()).trim()
}

async function download(src, sha, name) {
  const url = `https://raw.githubusercontent.com/${src.repo}/${sha ?? src.ref}/${src.dir}/${encodeURIComponent(name)}.svg`
  const r = await fetch(url)
  if (!r.ok) throw new Error(`${url}: ${r.status}`)
  return await r.text()
}

/** Class rules of a <style> block, class → declarations. */
function classRules(svg) {
  const rules = {}
  for (const style of svg.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g))
    for (const m of style[1].matchAll(/((?:\.[\w-]+\s*,?\s*)+)\{([^}]*)\}/g))
      for (const cls of m[1].split(',').map(s => s.trim().slice(1)).filter(Boolean))
        rules[cls] = (rules[cls] ? rules[cls] + ';' : '') + m[2].trim()
  return rules
}

const COLOUR = /(fill|stroke)\s*:\s*(?!none)[^;"]+/g

/** One upstream SVG as a <symbol>'s body: Inkscape's metadata and every namespaced attribute dropped, class styles inlined,
 *  every colour made currentColor, ids made unique to the icon, and (a line icon) its own stroke width dropped so the UI's
 *  applies. */
function clean(svg, id, line) {
  const rules = classRules(svg)
  let body = svg
    .replace(/<\?xml[\s\S]*?\?>/g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/<metadata[\s\S]*?<\/metadata>/g, '')
    .replace(/<(?:sodipodi|inkscape):[\w-]+\b[^>]*\/>/g, '')
    .replace(/<(sodipodi|inkscape):([\w-]+)\b[^>]*>[\s\S]*?<\/\1:\2>/g, '')
    .replace(/<style[\s\S]*?<\/style>/g, '')
    .replace(/<title[\s\S]*?<\/title>/g, '')
  const open = body.match(/<svg\b[^>]*>/)
  body = body.slice(body.indexOf(open[0]) + open[0].length, body.lastIndexOf('</svg>'))
  body = body
    .replace(/\s(?:inkscape|sodipodi|xml|xmlns)(?::[\w-]+)?="[^"]*"/g, '')
    .replace(/\sclass="([^"]*)"/g, (_, cls) => {
      const decl = cls.split(/\s+/).map(c => rules[c]).filter(Boolean).join(';')
      return decl ? ` style="${decl}"` : ''
    })
    .replace(/style="([^"]*)"/g, (_, s) => `style="${s.replace(COLOUR, '$1:currentColor')}"`)
    .replace(/\s(fill|stroke)="(?!none)[^"]*"/g, ' $1="currentColor"')
    .replace(/\sid="([^"]*)"/g, ` id="${id}-$1"`)
    .replace(/url\(#([^)]*)\)/g, `url(#${id}-$1)`)
    .replace(/href="#([^"]*)"/g, `href="#${id}-$1"`)
  if (line)
    body = body.replace(/stroke-width\s*:\s*[^;"]+;?/g, '').replace(/\sstroke-width="[^"]*"/g, '')
  body = body.replace(/\sstyle="\s*;?\s*"/g, '').replace(/>\s+</g, '><').replace(/\s{2,}/g, ' ').trim()
  return body
}

function viewBoxOf(svg) {
  const open = svg.match(/<svg\b[^>]*>/)[0]
  const vb = open.match(/viewBox="([^"]*)"/)
  if (vb) return vb[1].trim().split(/[\s,]+/).map(Number)
  const w = Number(open.match(/\bwidth="([\d.]+)/)?.[1] ?? 64)
  const h = Number(open.match(/\bheight="([\d.]+)/)?.[1] ?? 64)
  return [0, 0, w, h]
}

/** The drawn part of an icon, by rendering it: several RetroArch glyphs are drawn off their own canvas (at 0 128 64 64, or
 *  MAME on a 256 canvas), so the file's viewBox alone would leave them blank. */
function bbox(body, viewBox) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox.join(' ')}" color="#000" stroke-width="2.6">${body}</svg>`
  const b = new Resvg(svg).getBBox()
  return b && b.width > 0 && b.height > 0 ? [b.x, b.y, b.width, b.height] : null
}

const round = n => Math.round(n * 100) / 100

/** A glyph's viewBox: its drawn box, padded, square unless it is a wordmark (MAME, about 3.6 : 1). */
function fit([x, y, w, h]) {
  const pad = Math.max(w, h) * 0.04
  if (w / h > 1.6) return { viewBox: [x - pad, y - pad, w + 2 * pad, h + 2 * pad].map(round), wide: true }
  const side = Math.max(w, h) + 2 * pad
  return { viewBox: [x + w / 2 - side / 2, y + h / 2 - side / 2, side, side].map(round), wide: false }
}

// ---- the PlayStation marks ----

const pathOf = svg => svg.match(/\sd="([^"]+)"/)[1]

/** A path's drawn box [x0, y0, x1, y1], rendered. */
function boundsOf(d) {
  const b = new Resvg(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="-50 -50 200 200"><path d="${d}"/></svg>`).getBBox()
  return [b.x, b.y, b.x + b.width, b.y + b.height]
}

/** The PlayStation logo with `suffix` beside it, both from Simple Icons' 24 x 24 grid: a number 8 units tall, its top on the
 *  logo's top; a wordmark 6 units tall, on the logo's baseline. */
function playstationMark(logo, suffix) {
  const [, top, , bottom] = boundsOf(logo)
  let body = `<path fill="currentColor" d="${logo}"/>`
  if (suffix) {
    const parts = svgpath(suffix.d).abs().toString().split(/(?=M)/)
      .filter(part => boundsOf(part)[0] >= suffix.from)
    if (parts.length === 0) throw new Error(`${suffix.wordmark}: nothing at x >= ${suffix.from}`)
    const glyph = parts.join('')
    const [x0, y0, , y1] = boundsOf(glyph)
    const height = suffix.kind === 'number' ? 8 : 6
    const k = height / (y1 - y0)
    const placed = svgpath(glyph).translate(-x0, -y0).scale(k).translate(24 + 1.2, suffix.kind === 'number' ? top : bottom - height)
    body += `<path fill="currentColor" fill-rule="evenodd" d="${placed.round(3).toString()}"/>`
  }
  return body
}

/** A PlayStation mark's viewBox: its drawn box, padded by the same 4 % as the other glyphs, as wide as it draws. */
function fitMark([x, y, w, h]) {
  const pad = h * 0.04
  return [x - pad, y - pad, w + 2 * pad, h + 2 * pad].map(round)
}

// ---- the Deck: stroke paths of the line icons ----

const num = s => Number(s)

/** An SVG transform list as a 2x3 matrix [a b c d e f]. */
function matrixOf(transform) {
  let m = [1, 0, 0, 1, 0, 0]
  const mul = ([a, b, c, d, e, f], [A, B, C, D, E, F]) =>
    [a * A + c * B, b * A + d * B, a * C + c * D, b * C + d * D, a * E + c * F + e, b * E + d * F + f]
  for (const [, op, args] of (transform ?? '').matchAll(/(\w+)\s*\(([^)]*)\)/g)) {
    const v = args.trim().split(/[\s,]+/).map(Number)
    if (op === 'translate') m = mul(m, [1, 0, 0, 1, v[0], v[1] ?? 0])
    else if (op === 'scale') m = mul(m, [v[0], 0, 0, v[1] ?? v[0], 0, 0])
    else if (op === 'matrix') m = mul(m, v)
    else if (op === 'rotate') {
      const r = (v[0] * Math.PI) / 180, [cx, cy] = [v[1] ?? 0, v[2] ?? 0]
      m = mul(m, [1, 0, 0, 1, cx, cy])
      m = mul(m, [Math.cos(r), Math.sin(r), -Math.sin(r), Math.cos(r), 0, 0])
      m = mul(m, [1, 0, 0, 1, -cx, -cy])
    } else throw new Error(`transform ${op}`)
  }
  return m
}

/** A closed shape's outline as points, corners and curves sampled finely enough for the Deck's 14-40 px icons. */
function outline(tag, a) {
  const pts = []
  const arc = (cx, cy, rx, ry, from, to) => {
    for (let k = 0; k <= 12; k++) { const t = from + ((to - from) * k) / 12; pts.push([cx + rx * Math.cos(t), cy + ry * Math.sin(t)]) }
  }
  if (tag === 'circle' || tag === 'ellipse') {
    const cx = num(a('cx') ?? 0), cy = num(a('cy') ?? 0)
    const rx = num(a(tag === 'circle' ? 'r' : 'rx') ?? 0), ry = num(a(tag === 'circle' ? 'r' : 'ry') ?? 0)
    for (let k = 0; k < 48; k++) { const t = (k / 48) * 2 * Math.PI; pts.push([cx + rx * Math.cos(t), cy + ry * Math.sin(t)]) }
    return pts
  }
  const [x, y, w, h] = ['x', 'y', 'width', 'height'].map(k => num(a(k) ?? 0))
  const r = Math.min(num(a('rx') ?? a('ry') ?? 0), w / 2, h / 2)
  if (r <= 0) return [[x, y], [x + w, y], [x + w, y + h], [x, y + h]]
  const q = Math.PI / 2
  arc(x + w - r, y + r, r, r, -q, 0)
  arc(x + w - r, y + h - r, r, r, 0, q)
  arc(x + r, y + h - r, r, r, q, 2 * q)
  arc(x + r, y + r, r, r, 2 * q, 3 * q)
  return pts
}

const fmt = n => String(Math.round(n * 1000) / 1000)

/** Every drawn element of a line icon as path data, for SvgPath: a path as it is, a shape as its outline with its transform
 *  applied. A path with a transform is refused (none of these icons has one; it would be drawn wrong). */
function strokes(body, name) {
  const out = []
  for (const m of body.matchAll(/<(path|circle|ellipse|rect|line|polyline|polygon)\b([^>]*)>/g)) {
    const [, tag, attrs] = m
    const a = k => attrs.match(new RegExp(`\\s${k}="([^"]*)"`))?.[1]
    const t = a('transform')
    if (tag === 'path') {
      if (t) throw new Error(`${name}: a <path> with a transform`)
      out.push(a('d'))
      continue
    }
    let pts, closed = true
    if (tag === 'line') { pts = [[num(a('x1')), num(a('y1'))], [num(a('x2')), num(a('y2'))]]; closed = false }
    else if (tag === 'polyline' || tag === 'polygon') {
      const v = a('points').trim().split(/[\s,]+/).map(Number)
      pts = []
      for (let i = 0; i + 1 < v.length; i += 2) pts.push([v[i], v[i + 1]])
      closed = tag === 'polygon'
    } else pts = outline(tag, a)
    const [A, B, C, D, E, F] = matrixOf(t)
    const p = pts.map(([x, y]) => [A * x + C * y + E, B * x + D * y + F])
    out.push(`M${fmt(p[0][0])} ${fmt(p[0][1])}` + p.slice(1).map(([x, y]) => `L${fmt(x)} ${fmt(y)}`).join('') + (closed ? 'Z' : ''))
  }
  return out
}

function csString(s) { return '"' + s.replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"' }

// ---- write ----

function writeOutputs(icons, emulators, systems, consoles, shas, strokesById) {
  const header = `// Generated by web/scripts/build-platform-icons.mjs — do not edit. Arcticons ${shas.arcticons.slice(0, 12)}, ` +
    `retroarch-assets ${shas.retroarch.slice(0, 12)}, simple-icons ${SIMPLE_ICONS.ref}. Credits: NOTICE-icons.md.\n`
  const ts = header +
    `export interface PlatformIconDef { viewBox: string; body: string; line: boolean; wide: boolean }\n\n` +
    `export const PLATFORM_ICONS: Record<string, PlatformIconDef> = ${JSON.stringify(icons, null, 2)}\n\n` +
    `/** Emulator name → icon id. */\nexport const EMULATOR_ICONS: Record<string, string> = ${JSON.stringify(emulators, null, 2)}\n\n` +
    `/** EmuDeck console folder name → icon id. */\nexport const SYSTEM_ICONS: Record<string, string> = ${JSON.stringify(systems, null, 2)}\n\n` +
    `/** A release's console name → icon id. */\nexport const CONSOLE_ICONS: Record<string, string> = ${JSON.stringify(consoles, null, 2)}\n`
  for (const out of OUTPUTS) writeFileSync(at(out), ts)

  const deck = Object.entries(emulators)
    .filter(([, id]) => strokesById[id])
    .map(([name, id]) => `        [${csString(name)}] = [${strokesById[id].map(csString).join(', ')}],`)
  writeFileSync(at(DECK_OUTPUT), header +
    `namespace SaveLocker.Agent.Linux.Ui;\n\n` +
    `/// <summary>Each emulator's icon as stroke paths on a 48 x 48 grid for <see cref="SvgPath"/>: Arcticons' line icons as drawn,\n` +
    `/// Xenia's filled glyph as its outline (both CC BY-SA 4.0).</summary>\n` +
    `static class EmulatorIcons\n{\n    public static readonly IReadOnlyDictionary<string, string[]> Paths = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)\n    {\n` +
    deck.join('\n') + `\n    };\n}\n`)
}

async function build() {
  const shas = { arcticons: await commitOf(ARCTICONS), retroarch: await commitOf(RETROARCH) }
  const icons = {}
  const emulators = {}
  const systems = {}
  const strokesById = {}

  const logo = pathOf(await download(SIMPLE_ICONS, null, 'playstation'))
  for (const [system, mark] of Object.entries(PLAYSTATION)) {
    const id = `sys-${system}`
    const suffix = mark && { ...mark, d: pathOf(await download(SIMPLE_ICONS, null, mark.wordmark)) }
    const body = playstationMark(logo, suffix)
    const box = bbox(body, [-50, -50, 200, 200])
    icons[id] = { viewBox: fitMark(box).join(' '), body, line: false, wide: false }
    systems[system] = id
  }

  for (const [system, file] of Object.entries(SYSTEMS)) {
    if (PLAYSTATION[system] !== undefined) continue
    const id = `sys-${system}`
    const raw = await download(RETROARCH, shas.retroarch, file)
    const body = clean(raw, id, false)
    const box = bbox(body, viewBoxOf(raw))
    if (!box) throw new Error(`${file}: draws nothing`)
    const { viewBox, wide } = fit(box)
    icons[id] = { viewBox: viewBox.join(' '), body, line: false, wide }
    systems[system] = id
  }
  for (const [alias, system] of Object.entries(SYSTEM_ALIASES)) systems[alias] = systems[system]

  for (const [name, src] of Object.entries(EMULATORS)) {
    if (src.system) { emulators[name] = systems[src.system]; continue }
    if (src.local) {
      const id = `emu-${src.local}`
      const raw = readFileSync(join(here, 'icon-sources', `${src.local}.svg`), 'utf8')
      const d = pathOf(raw)
      const body = `<path fill="currentColor" fill-rule="evenodd" d="${d}"/>`
      const box = bbox(body, viewBoxOf(raw))
      icons[id] = { viewBox: fit(box).viewBox.join(' '), body, line: false, wide: false }
      // The Deck strokes, never fills: the glyph's outline, fitted to the 48 x 48 grid the line icons use.
      const [x, y, w, h] = box, k = (48 * 0.92) / Math.max(w, h)
      strokesById[id] = [svgpath(d).translate(-x - w / 2, -y - h / 2).scale(k).translate(24, 24).round(2).toString()]
      emulators[name] = id
      continue
    }
    const id = src.id ?? `emu-${src.arcticons}`
    if (!icons[id]) {
      const raw = await download(ARCTICONS, shas.arcticons, src.arcticons)
      const body = clean(raw, id, true)
      icons[id] = { viewBox: viewBoxOf(raw).join(' '), body, line: true, wide: false }
      strokesById[id] = strokes(body, src.arcticons)
    }
    emulators[name] = id
  }

  const consoles = Object.fromEntries(Object.entries(CONSOLE_NAMES).map(([name, system]) => [name, systems[system]]))
  writeOutputs(icons, emulators, systems, consoles, shas, strokesById)
  console.log(`${Object.keys(icons).length} icons, ${Object.keys(emulators).length} emulators, ${Object.keys(systems).length} console names`)
}

function verify() {
  const src = readFileSync(at(OUTPUTS[0]), 'utf8')
  const grab = name => JSON.parse(src.match(new RegExp(`export const ${name}[^=]*= (\\{[\\s\\S]*?\\n\\})`))[1])
  const icons = grab('PLATFORM_ICONS')
  let bad = 0
  for (const [map, ids] of [['EMULATOR_ICONS', grab('EMULATOR_ICONS')], ['SYSTEM_ICONS', grab('SYSTEM_ICONS')], ['CONSOLE_ICONS', grab('CONSOLE_ICONS')]])
    for (const [k, id] of Object.entries(ids))
      if (!icons[id]) { console.error(`${map}.${k} → ${id}: no such icon`); bad++ }
  for (const [id, icon] of Object.entries(icons))
    if (!bbox(icon.body, icon.viewBox.split(' ').map(Number))) { console.error(`${id}: draws nothing`); bad++ }
  for (const out of OUTPUTS.slice(1))
    if (readFileSync(at(out), 'utf8') !== src) { console.error(`${out} differs from ${OUTPUTS[0]}`); bad++ }
  console.log(bad ? `${bad} problem(s)` : `${Object.keys(icons).length} icons, all drawn`)
  process.exit(bad ? 1 : 0)
}

if (check) verify()
else await build()
