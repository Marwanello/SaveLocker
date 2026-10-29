import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))

/** Builds `mockup/index.html` (the real agent-ui + fetch mock + pad layer) into one self-contained
 *  HTML file via `mockup/inline.cjs`, for `docs/tasks/_done/gamepad-agent-ui/mockup.html`. */
export default defineConfig({
  root: here,
  base: './',
  plugins: [react()],
  build: {
    outDir: resolve(here, '../.mockup-dist'),
    emptyOutDir: true,
    cssCodeSplit: false,
    // Inline every glyph PNG (the input pack) as a data URI so the mockup stays one file.
    assetsInlineLimit: 100_000_000,
    rollupOptions: {
      output: { inlineDynamicImports: true },
    },
  },
})
