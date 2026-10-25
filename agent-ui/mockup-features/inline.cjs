/* node inline.cjs <dist dir> <out.html> — one self-contained HTML file from a Vite build (CSS, fonts and the module
   script inlined). Used by both apps' `npm run mockups:build`. */
const { readFileSync, writeFileSync } = require('node:fs')
const { join, resolve, dirname } = require('node:path')
const [dist, out] = process.argv.slice(2).map(p => resolve(p))
const MIME = { woff2: 'font/woff2', woff: 'font/woff', ttf: 'font/ttf', png: 'image/png', jpg: 'image/jpeg', svg: 'image/svg+xml' }
const dataUri = f => `data:${MIME[f.split('.').pop().toLowerCase()] ?? 'application/octet-stream'};base64,${readFileSync(f).toString('base64')}`

let html = readFileSync(join(dist, 'index.html'), 'utf8')
const cssMatch = html.match(/<link[^>]+rel="stylesheet"[^>]+href="\.\/([^"]+)"[^>]*>/)
const cssPath = join(dist, cssMatch[1])
let css = readFileSync(cssPath, 'utf8').replace(/url\((["']?)([^)"']+)\1\)/g, (whole, _q, ref) => {
  if (ref.startsWith('data:') || ref.startsWith('http')) return whole
  try { return `url(${dataUri(resolve(dirname(cssPath), ref.replace(/^\.?\//, '')))})` } catch { console.warn('missing', ref); return whole }
})
// Fonts: only woff2 is needed by every browser the console supports; dropping the woff fallbacks halves the size.
css = css.replace(/,\s*url\(data:font\/woff;base64,[^)]+\)\s*format\(["']woff["']\)/g, '')
html = html.replace(cssMatch[0], () => `<style>\n${css}\n</style>`)
const jsMatch = html.match(/<script[^>]+type="module"[^>]+src="\.\/([^"]+)"[^>]*><\/script>/)
const js = readFileSync(join(dist, jsMatch[1]), 'utf8').replace(/<\/?script/gi, m => '\\x3C' + m.slice(1))
  // A literal U+FFFD (a parser's replacement character) reads as corruption to the artifact service; the escape is
  // the same character in every JS string, template and regex.
  .replace(/�/g, '\\uFFFD')
html = html.replace(jsMatch[0], () => `<script type="module">\n${js}\n</script>`)
html = html.replace(/<link[^>]+rel="(modulepreload|icon|manifest)"[^>]*>\s*/g, '')
writeFileSync(out, html)
console.log(`${out}: ${(html.length / 1024 / 1024).toFixed(2)} MB`)
