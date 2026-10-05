import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const serverUrl = process.env.AXIS_SERVER_URL ?? 'http://localhost:5206'

export default defineConfig({
  plugins: [react()],
  build: {
    // The server hosts the built SPA from its web root.
    outDir: '../src/Axis.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': serverUrl,
      '/health': serverUrl,
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
