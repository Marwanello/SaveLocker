import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, relative, resolve } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
const web = resolve(here, '..')
const overridesDir = resolve(here, 'overrides')
const OVERRIDES: Record<string, string> = JSON.parse(readFileSync(resolve(here, 'overrides.json'), 'utf8'))

/** Every import of a replaced console component resolves to its copy in overrides/ (copies import each other
 *  directly, see copy-override.cjs). */
function overrides(): Plugin {
  return {
    name: 'mockup-overrides',
    enforce: 'pre',
    async resolveId(source, importer, options) {
      if (!importer || importer.startsWith(overridesDir)) return null
      const hit = await this.resolve(source, importer, { ...options, skipSelf: true })
      if (!hit) return null
      const swap = OVERRIDES[relative(web, hit.id).replace(/\\/g, '/')]
      return swap ? resolve(overridesDir, swap) : null
    },
  }
}

/** Builds the console feature mockups (the real console + mock backend + the planned features) into one HTML file. */
export default defineConfig({
  root: here,
  base: './',
  plugins: [overrides(), react(), tailwindcss()],
  build: {
    outDir: resolve(here, '../.mockup-features-dist'),
    emptyOutDir: true,
    cssCodeSplit: false,
    assetsInlineLimit: 100_000_000,
    chunkSizeWarningLimit: 4000,
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
})
