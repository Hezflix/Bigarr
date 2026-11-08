import fs from 'node:fs'
import path from 'node:path'
import zlib from 'node:zlib'
import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Writes `x.js.br` and `x.js.gz` next to every text asset: the API serves them as they are to clients that ask for them,
// so nothing is compressed per request.
function precompress(): Plugin {
  const compressible = /\.(js|css|html|svg|json|txt|map|webmanifest)$/
  const walk = (dir: string): string[] =>
    fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => (e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]))
  return {
    name: 'bigarr-precompress',
    apply: 'build',
    closeBundle() {
      const outDir = path.resolve(import.meta.dirname, '../src/Bigarr.Api/wwwroot')
      for (const file of walk(outDir).filter((f) => compressible.test(f))) {
        const body = fs.readFileSync(file)
        if (body.length < 1024) continue
        fs.writeFileSync(`${file}.br`, zlib.brotliCompressSync(body, { params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 11 } }))
        fs.writeFileSync(`${file}.gz`, zlib.gzipSync(body, { level: 9 }))
      }
    },
  }
}

// The build lands in the API's wwwroot: the backend serves the SPA as static files.
export default defineConfig({
  plugins: [react(), tailwindcss(), precompress()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  build: {
    outDir: '../src/Bigarr.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:7979',
      '/health': 'http://localhost:7979',
      '/openapi': 'http://localhost:7979',
    },
  },
})
