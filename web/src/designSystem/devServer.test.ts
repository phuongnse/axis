/// <reference types="node" />
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createServer, type ViteDevServer } from 'vite'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'

const webFolder = resolve(fileURLToPath(import.meta.url), '../../..')
const timeout = 60_000

let server: ViteDevServer
let baseUrl: string

beforeAll(async () => {
  server = await createServer({
    configFile: resolve(webFolder, 'vite.design-system.config.ts'),
    root: webFolder,
    server: { port: 0 },
    logLevel: 'error',
  })
  await server.listen()
  baseUrl = server.resolvedUrls!.local[0]
}, timeout)

afterAll(async () => {
  await server.close()
}, timeout)

async function fetchText(path: string): Promise<{ status: number; body: string }> {
  const response = await fetch(new URL(path, baseUrl))
  return { status: response.status, body: await response.text() }
}

describe('the design-system dev server', () => {
  it(
    'serves the harness with the previews from source',
    async () => {
      const page = await fetchText('design-system.html')
      expect(page.status).toBe(200)
      expect(page.body).toContain('/@react-refresh')
      expect(page.body).not.toContain('artifacts/')
      // Vite may move the inline module into a module of its own.
      const proxy = /src="([^"]*html-proxy[^"]*)"/.exec(page.body)
      const code = proxy === null ? page.body : (await fetchText(proxy[1].replaceAll('&amp;', '&'))).body
      expect(code).toContain('/src/designSystem/bundle.ts')
    },
    timeout,
  )

  it(
    'serves the preview components with React Refresh',
    async () => {
      const module = await fetchText('src/designSystem/Preview.tsx')
      expect(module.status).toBe(200)
      expect(module.body).toContain('$RefreshReg$')
    },
    timeout,
  )
})
