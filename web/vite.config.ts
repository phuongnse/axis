import react from '@vitejs/plugin-react'
import type { ProxyOptions } from 'vite'
import { defineConfig } from 'vitest/config'

const serverUrl = process.env.AXIS_SERVER_URL ?? 'http://localhost:5206'

// Studio reconnects on 502, 503 and 504. Vite would answer 500 while the server restarts.
function answerBadGateway(proxy: Parameters<NonNullable<ProxyOptions['configure']>>[0]) {
  proxy.on('error', (_error, _request, response) => {
    if ('writeHead' in response && !response.headersSent) {
      response.writeHead(502).end()
    }
  })
}

export default defineConfig({
  plugins: [react()],
  build: {
    // The server hosts the built SPA from its web root.
    outDir: '../src/Axis.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': { target: serverUrl, configure: answerBadGateway },
      '/health': { target: serverUrl, configure: answerBadGateway },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Form tests type through many fields. With one worker per test file they come close to the
    // 5 second default on a busy machine.
    testTimeout: 15000,
  },
})
