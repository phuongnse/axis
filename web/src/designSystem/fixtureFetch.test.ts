import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { installFixtureFetch } from './fixtureFetch'

const routes = {
  '/api/apps/Sample/things': () => ({ items: ['one'] }),
  '/api/apps/Sample/things?page=2': () => ({ items: ['two'] }),
}
const scopes = ['/api/apps/Sample/']

describe('installFixtureFetch', () => {
  const originalFetch = window.fetch
  let pageFetch: ReturnType<typeof vi.fn>

  beforeEach(() => {
    pageFetch = vi.fn(async () => new Response('from the page', { status: 200 }))
    window.fetch = pageFetch as unknown as typeof window.fetch
    installFixtureFetch(routes, scopes)
  })

  afterEach(() => {
    window.fetch = originalFetch
    vi.restoreAllMocks()
  })

  test('answers a fixture application path with the fixture', async () => {
    const response = await window.fetch('/api/apps/Sample/things')

    expect(response.status).toBe(200)
    expect(await response.json()).toEqual({ items: ['one'] })
    expect(pageFetch).not.toHaveBeenCalled()
  })

  test('prefers the route with the query and falls back to the path alone', async () => {
    expect(await (await window.fetch('/api/apps/Sample/things?page=2')).json()).toEqual({ items: ['two'] })
    expect(await (await window.fetch('/api/apps/Sample/things?page=3')).json()).toEqual({ items: ['one'] })
  })

  test.each([
    ['another origin', 'https://example.test/x'],
    ['another application path', '/api/apps/Other/x'],
  ])('passes %s to the page fetch with the same arguments', async (_, url) => {
    const init = { method: 'POST', body: 'x' }

    const response = await window.fetch(url, init)

    expect(await response.text()).toBe('from the page')
    expect(pageFetch).toHaveBeenCalledExactlyOnceWith(url, init)
  })

  test('passes a fixture path on another origin to the page fetch', async () => {
    await window.fetch('https://example.test/api/apps/Sample/things')

    expect(pageFetch).toHaveBeenCalledOnce()
  })

  test('returns a Response answer as it is', async () => {
    installFixtureFetch({ '/api/apps/Sample/failing': () => new Response(null, { status: 500 }) }, scopes)

    const response = await window.fetch('/api/apps/Sample/failing')

    expect(response.status).toBe(500)
  })

  test('keeps the fetch pending when the route never settles', async () => {
    installFixtureFetch({ '/api/apps/Sample/loading': () => new Promise<never>(() => {}) }, scopes)
    const settled = Symbol('settled')

    const result = await Promise.race([
      window.fetch('/api/apps/Sample/loading').then(() => settled),
      new Promise((resolve) => setTimeout(() => resolve('pending'), 0)),
    ])

    expect(result).toBe('pending')
  })

  test('answers a scoped path with no fixture with 404 and a console error', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})

    const response = await window.fetch('/api/apps/Sample/missing')

    expect(response.status).toBe(404)
    expect(error).toHaveBeenCalledOnce()
    expect(pageFetch).not.toHaveBeenCalled()
  })
})
