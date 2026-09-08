import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The build lands in the API's wwwroot: the backend serves the SPA as static files.
export default defineConfig({
  plugins: [react()],
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
