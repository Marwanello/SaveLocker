/* Inlines the mockup build into ONE self-contained HTML file:
   - CSS inlined, every font url() rewritten to a data: URI (Archivo + JetBrains Mono);
   - the module script inlined (safe `</script` escaping);
   - modulepreload/favicon/manifest links dropped.
   Output: docs/tasks/gamepad-agent-ui/mockup.html */
const { readFileSync, writeFileSync, readdirSync } = require('node:fs')
const { join, resolve, dirname } = require('node:path')

const dist = resolve(__dirname, '../.mockup-dist')
const out = resolve(__dirname, '../../docs/tasks/gamepad-agent-ui/mockup.html')

const MIME = {
  woff2: 'font/woff2', woff: 'font/woff', ttf: 'font/ttf',
  png: 'image/png', jpg: 'image/jpeg', svg: 'image/svg+xml',
}

function dataUri(file) {
  const ext = file.split('.').pop().toLowerCase()
  const mime = MIME[ext] ?? 'application/octet-stream'
  return `data:${mime};base64,${readFileSync(file).toString('base64')}`
}

let html = readFileSync(join(dist, 'index.html'), 'utf8')

const cssMatch = html.match(/<link[^>]+rel="stylesheet"[^>]+href="\.\/([^"]+)"[^>]*>/)
if (!cssMatch) throw new Error('inline: no stylesheet link found')
const cssPath = join(dist, cssMatch[1])
const cssDir = dirname(cssPath)
let css = readFileSync(cssPath, 'utf8')
css = css.replace(/url\((["']?)([^)"']+)\1\)/g, (whole, _q, ref) => {
  if (ref.startsWith('data:') || ref.startsWith('http')) return whole
  const file = resolve(cssDir, ref.replace(/^\.?\//, ''))
  try {
    return `url(${dataUri(file)})`
  } catch {
    console.warn('inline: could not read', file)
    return whole
  }
})
// Replacer FUNCTIONS, not strings: minified JS contains `$&`/`$'` sequences, which a string
// replacement would expand to pieces of the matched script tag — including its `</script>`.
html = html.replace(cssMatch[0], () => `<style>\n${css}\n</style>`)

const jsMatch = html.match(/<script[^>]+type="module"[^>]+src="\.\/([^"]+)"[^>]*><\/script>/)
if (!jsMatch) throw new Error('inline: no module script found')
// `<script`/`</script` can appear INSIDE the bundle (React's preload helpers hold a literal
// `<script type="module" src=...>` string); the HTML parser would end the inline script early.
// `\x3C` is the same character in every JS string/regex context, so this is semantics-preserving.
const js = readFileSync(join(dist, jsMatch[1]), 'utf8')
  .replace(/<\/?script/gi, m => '\\x3C' + m.slice(1))
html = html.replace(jsMatch[0], () => `<script type="module">\n${js}\n</script>`)

html = html
  .replace(/<link[^>]+rel="modulepreload"[^>]*>\s*/g, '')
  .replace(/<link[^>]+rel="icon"[^>]*>\s*/g, '')
  .replace(/<link[^>]+rel="manifest"[^>]*>\s*/g, '')

writeFileSync(out, html)
console.log(`inline: wrote ${out} (${(html.length / 1024 / 1024).toFixed(2)} MB)`)
const leftovers = readdirSync(dist)
if (leftovers.length) console.log('inline: build dir still holds', leftovers.join(', '))
