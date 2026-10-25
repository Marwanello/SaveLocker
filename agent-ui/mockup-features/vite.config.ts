import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, relative, resolve } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
const agentUi = resolve(here, '..')
const overridesDir = resolve(here, 'overrides')

/** Real components the feature mockups replace with a copy that has the new feature in it. */
const OVERRIDES: Record<string, string> = JSON.parse(readFileSync(resolve(here, 'overrides.json'), 'utf8'))

/** Every import of a replaced component resolves to its copy — except from inside overrides/, so a copy can wrap the
 *  real one. */
function overrides(): Plugin {
  return {
    name: 'mockup-overrides',
    enforce: 'pre',
    async resolveId(source, importer, options) {
      // Vite hands importers over with forward slashes; resolve() gives backslashes on Windows.
      if (!importer || importer.replace(/\\/g, '/').startsWith(overridesDir.replace(/\\/g, '/'))) return null
      const hit = await this.resolve(source, importer, { ...options, skipSelf: true })
      if (!hit) return null
      const rel = relative(agentUi, hit.id).replace(/\\/g, '/')
      const swap = OVERRIDES[rel]
      return swap ? resolve(overridesDir, swap) : null
    },
  }
}

/** Builds the feature mockups (the real agent-ui + mock backend + the planned features) into one HTML file via
 *  `mockup-features/inline.cjs`. */
export default defineConfig({
  root: here,
  base: './',
  plugins: [overrides(), react()],
  build: {
    outDir: resolve(here, '../.mockup-features-dist'),
    emptyOutDir: true,
    cssCodeSplit: false,
    assetsInlineLimit: 100_000_000,
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
})
