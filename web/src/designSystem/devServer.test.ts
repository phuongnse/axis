/// <reference types="node" />
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createLogger, createServer, type IndexHtmlTransformContext, type ViteDevServer } from 'vite'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'

const webFolder = resolve(fileURLToPath(import.meta.url), '../../..')
const timeout = 60_000
const devToolsPath = '/.well-known/appspecific/com.chrome.devtools.json'

let server: ViteDevServer
let baseUrl: string
const errors: string[] = []

beforeAll(async () => {
  const logger = createLogger('error')
  logger.error = (message) => {
    errors.push(message)
  }
  server = await createServer({
    configFile: resolve(webFolder, 'vite.design-system.config.ts'),
    root: webFolder,
    server: { port: 0 },
    customLogger: logger,
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

  it(
    'answers 404 without an error for a path that names no file or module',
    async () => {
      errors.length = 0
      const response = await fetchText(devToolsPath)
      expect(response.status).toBe(404)
      expect(errors).toEqual([])
    },
    timeout,
  )
})

describe('the bundle guard of the design-system dev server', () => {
  function transformHtml(path: string, html: string): unknown {
    const plugin = server.config.plugins.find((candidate) => candidate.name === 'serve-bundle-from-source')
    const transform = plugin?.transformIndexHtml as {
      handler: (html: string, ctx: IndexHtmlTransformContext) => unknown
    }
    return transform.handler(html, { path, filename: resolve(webFolder, path.slice(1)) })
  }

  it('throws for the harness without the bundle tag', () => {
    expect(() => transformHtml('/design-system.html', '<html></html>')).toThrow(
      /has no script tag of the design-system bundle/,
    )
  })

  it('passes other HTML pages through unchanged', () => {
    expect(transformHtml('/index.html', '<html></html>')).toBe('<html></html>')
    expect(transformHtml(devToolsPath, '<html></html>')).toBe('<html></html>')
  })
})
