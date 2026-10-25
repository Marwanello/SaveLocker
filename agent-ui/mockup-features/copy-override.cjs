/* Copies a real agent-ui component into overrides/ and rewrites its relative imports so they still point at src/ —
   except imports of another overridden component, which point at that copy. Run once per override; the copy is then
   edited to add the planned feature.
     node mockup-features/copy-override.cjs src/components/GameDetailView.tsx */
const { readFileSync, writeFileSync, existsSync } = require('node:fs')
const { basename, dirname, relative, resolve } = require('node:path')

const app = resolve(__dirname, '..')
const overrides = resolve(__dirname, 'overrides')
const src = resolve(app, process.argv[2])
const out = resolve(overrides, basename(src))
const OVERRIDDEN = require('./overrides.json')

let text = readFileSync(src, 'utf8')
text = text.replace(/from '(\.{1,2}\/[^']+)'/g, (whole, spec) => {
  const target = resolve(dirname(src), spec)
  const rel = relative(app, target).replace(/\\/g, '/')
  const hit = Object.keys(OVERRIDDEN).find(k => k.replace(/\.tsx$/, '') === rel.replace(/\.tsx$/, ''))
  if (hit) return `from './${basename(OVERRIDDEN[hit], '.tsx')}'`
  let p = relative(overrides, target).replace(/\\/g, '/')
  if (!p.startsWith('.')) p = './' + p
  return `from '${p}'`
})
if (existsSync(out) && !process.argv.includes('--force')) throw new Error(`${out} exists; pass --force to overwrite`)
writeFileSync(out, text)
console.log('copied', relative(app, src), '->', relative(app, out))
