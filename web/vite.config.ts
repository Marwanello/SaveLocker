import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    host: '0.0.0.0',
    port: process.env.PORT ? parseInt(process.env.PORT) : 5173,
    proxy: {
      '/api': 'http://localhost:5179',
      '/art': 'http://localhost:5179',
    },
  },
  build: {
    outDir: 'dist',
    // Raised from Vite's 500 kB default, which v0.5.0's release notes crossed (494 → 509 kB).
    // Every release bundles its notes as raw markdown and keeps every previous release's too, so
    // this grows monotonically by design and would warn on every release from now on.
    //
    // The point of raising it rather than silencing it: a warning-free console build is a signal
    // this project relies on (Gotchas.md), and a permanent advisory that everyone learns to ignore
    // destroys that. 800 kB still leaves room to notice a real regression — an accidental dependency
    // or a lost tree-shake would clear it easily.
    chunkSizeWarningLimit: 800,
  },
})
