/* Copies the needed glyphs out of Julio Cacko's "Free Input Pack" (Universal Version, Source) into
   src/gamepad/pack/, with normalized names, so the app imports them through Vite.

   Usage:  node mockup/copy-glyphpack.cjs "D:\path\to\Universal Version\Source"
   The pack itself is NOT committed to this repo — only the files this UI draws with. */
const { mkdirSync, copyFileSync, existsSync, readFileSync, writeFileSync } = require('node:fs')
const { join, resolve } = require('node:path')
const zlib = require('node:zlib')

const source = process.argv[2] ?? 'D:\\Projects\\SaveLocker\\Assets\\Universal Version\\Source'
const dest = resolve(__dirname, '../src/gamepad/pack')

const X_VARIANTS = { default: '', light: '_Light', alt: '_Alt', alt2: '_Alt_2', retro: '_Retro' }
const P5_VARIANTS = { default: '', light: '_Light', alt: '_Alt', alt2: 'Alt_2', retro: '_Retro' }

/* Candidate file names per glyph; the first that exists wins (covers the pack's naming quirks). */
const X_FILES = {
  a: s => [`T_X_A_White${s}.png`],
  b: s => [`T_X_B_White${s}.png`],
  x: s => [`T_X_X_White${s}.png`],
  y: s => [`T_X_Y_White${s}.png`],
  lb: s => [`T_X_LB${s}.png`],
  rb: s => [`T_X_RB${s}.png`],
  lt: s => [`T_X_LT${s}.png`],
  rt: s => [`T_X_RT${s}.png`],
  l3: s => [`T_X_Left_Stick_Click${s}.png`, `T_X_Left_Stick_Click_Alt${s}.png`],
  view: s => [`T_X_Share${s}.png`, `T_X_Share-1${s}.png`],
  move: s => [`T_X_Dpad${s}.png`],
  stick: s => [`T_X_L_2D${s}.png`, `T_X_L${s}.png`],
  rstick: s => [`T_X_R_2D${s}.png`, `T_X_R${s}.png`],
  r3: s => [`T_X_Right_Stick_Click${s}.png`, `T_X_Right_Stick_Click_Alt${s}.png`],
}
const X_COLORS = {
  a: s => [`T_X_A_Color${s}.png`],
  b: s => [`T_X_B_Color${s}.png`],
  x: s => [`T_X_X_Color${s}.png`],
  y: s => [`T_X_Y_Color${s}.png`],
}
const P5_FILES = {
  a: s => [`T_P5_Cross${s}.png`],
  b: s => [`T_P5_Circle${s}.png`],
  x: s => [`T_P5_Square${s}.png`],
  y: s => [`T_P5_Triangle${s}.png`],
  lb: s => [`T_P5_L1${s}.png`],
  rb: s => [`T_P5_R1${s}.png`],
  lt: s => [`T_P5_L2${s}.png`],
  rt: s => [`T_P5_R2${s}.png`],
  l3: s => [`T_P5_L3${s}.png`],
  view: s => [`T_P5_Share${s}.png`],
  move: s => [`T_P5_Dpad${s}.png`],
  stick: s => [`T_P5_L_2D${s}.png`, `T_P5_L${s}.png`],
  rstick: s => [`T_P5_R_2D${s}.png`, `T_P5_R${s}.png`],
  r3: s => [`T_P5_R3${s}.png`],
}
const P5_COLORS = {
  a: s => [`T_P5_Cross_Color${s}.png`],
  b: s => [`T_P5_Circle_Color${s}.png`],
  x: s => [`T_P5_Square_Color${s}.png`],
  y: s => [`T_P5_Triangle_Color${s}.png`],
}

const PACKS = [
  { key: 'xbox', dir: 'XGamepad', variants: X_VARIANTS, files: X_FILES, colors: X_COLORS },
  { key: 'ps', dir: 'P5Gamepad', variants: P5_VARIANTS, files: P5_FILES, colors: P5_COLORS },
]

const VARIANT_DIRS = { default: 'Default', light: 'Light', alt: 'Alt', alt2: 'Alt 2', retro: 'Retro' }

/**
 * The pack's PNGs carry wildly different transparent padding, so equal `width` in the UI does
 * NOT mean equal visual size. Decode each copied PNG (8-bit RGB/RGBA, pure Node zlib — no image
 * library) and record the bounds of its opaque content; the Glyph component scales every sprite
 * so its content fits one square box. Emitted as `pack/bounds.ts` next to the files.
 */
function pngBounds(file) {
  const b = readFileSync(file)
  if (b.length < 24 || b.readUInt32BE(0) !== 0x89504e47) return null
  let pos = 8
  let w = 0
  let h = 0
  let depth = 0
  let ctype = 0
  const idat = []
  while (pos + 12 <= b.length) {
    const len = b.readUInt32BE(pos)
    const type = b.toString('ascii', pos + 4, pos + 8)
    const data = b.subarray(pos + 8, pos + 8 + len)
    if (type === 'IHDR') { w = data.readUInt32BE(0); h = data.readUInt32BE(4); depth = data[8]; ctype = data[9] }
    else if (type === 'IDAT') idat.push(data)
    else if (type === 'IEND') break
    pos += 12 + len
  }
  const full = { cw: w, ch: h, x: 0, y: 0, w, h }
  if (!w || !h || depth !== 8 || (ctype !== 6 && ctype !== 2)) return full
  const ch = ctype === 6 ? 4 : 3
  const stride = w * ch
  let raw
  try { raw = zlib.inflateSync(Buffer.concat(idat)) } catch { return full }
  const out = Buffer.alloc(h * stride)
  let o = 0
  for (let y = 0; y < h; y++) {
    const filter = raw[o++]
    const line = raw.subarray(o, o + stride)
    o += stride
    const prev = y > 0 ? out.subarray((y - 1) * stride, y * stride) : null
    const cur = out.subarray(y * stride, (y + 1) * stride)
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? cur[x - ch] : 0
      const bb = prev ? prev[x] : 0
      const c = prev && x >= ch ? prev[x - ch] : 0
      let v = line[x]
      if (filter === 1) v = (v + a) & 255
      else if (filter === 2) v = (v + bb) & 255
      else if (filter === 3) v = (v + ((a + bb) >> 1)) & 255
      else if (filter === 4) {
        const p = a + bb - c
        const pa = Math.abs(p - a)
        const pb = Math.abs(p - bb)
        const pc = Math.abs(p - c)
        v = (v + (pa <= pb && pa <= pc ? a : pb <= pc ? bb : c)) & 255
      } else if (filter !== 0) return full
      cur[x] = v
    }
  }
  let minX = w
  let minY = h
  let maxX = -1
  let maxY = -1
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const i = y * stride + x * ch
      const solid = ctype === 6
        ? out[i + 3] > 8
        : out[i] < 250 || out[i + 1] < 250 || out[i + 2] < 250
      if (!solid) continue
      if (x < minX) minX = x
      if (x > maxX) maxX = x
      if (y < minY) minY = y
      if (y > maxY) maxY = y
    }
  }
  if (maxX < 0) return full
  return { cw: w, ch: h, x: minX, y: minY, w: maxX - minX + 1, h: maxY - minY + 1 }
}

let copied = 0
const missing = []
const bounds = {}
for (const pack of PACKS) {
  for (const [variant, suffix] of Object.entries(pack.variants)) {
    const outDir = join(dest, pack.key, variant)
    mkdirSync(outDir, { recursive: true })
    const srcDir = join(source, pack.dir, VARIANT_DIRS[variant])
    const pick = (templates, outName) => {
      const found = templates.map(name => join(srcDir, name)).find(p => existsSync(p))
      if (!found) { missing.push(`${pack.key}/${variant}/${outName}`); return }
      const outFile = join(outDir, outName)
      copyFileSync(found, outFile)
      const box = pngBounds(outFile)
      if (box) bounds[`./pack/${pack.key}/${variant}/${outName}`] = [box.cw, box.ch, box.x, box.y, box.w, box.h]
      copied++
    }
    for (const [id, templates] of Object.entries(pack.files)) pick(templates(suffix), `${id}.png`)
    for (const [id, templates] of Object.entries(pack.colors)) pick(templates(suffix), `${id}-color.png`)
  }
}

const boundsTs = '/* GENERATED by mockup/copy-glyphpack.cjs — canvas w/h + opaque content box [x, y, w, h]\n'
  + '   per sprite, so every glyph fits the same square. Do not hand-edit. */\n'
  + 'export const GLYPH_BOUNDS: Record<string, [number, number, number, number, number, number]> = '
  + JSON.stringify(bounds, null, 2) + '\n'
writeFileSync(join(dest, 'bounds.ts'), boundsTs)

if (missing.length) {
  console.error('missing sources:')
  for (const m of missing) console.error('  ' + m)
  process.exit(1)
}
console.log(`copied ${copied} glyph files into src/gamepad/pack/`)
