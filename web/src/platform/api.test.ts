import { describe, expect, it, vi } from 'vitest'
import { apiFetch } from './api'

function stubFetch() {
  const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => new Response(null, { status: 204 }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('apiFetch', () => {
  it.each(['POST', 'PUT', 'PATCH', 'DELETE', 'post'])(
    'sends the CSRF header on %s and keeps the other headers',
    async (method) => {
      const fetchMock = stubFetch()
      const signal = new AbortController().signal

      await apiFetch('/api/x', {
        method,
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'key-1' },
        body: '{}',
        signal,
      })

      const [url, init] = fetchMock.mock.calls[0]
      const headers = new Headers(init?.headers)
      expect(url).toBe('/api/x')
      expect(headers.get('X-Axis-Csrf')).toBe('1')
      expect(headers.get('Content-Type')).toBe('application/json')
      expect(headers.get('Idempotency-Key')).toBe('key-1')
      expect([init?.method, init?.body, init?.signal]).toEqual([method, '{}', signal])
    },
  )

  it('replaces a CSRF header the caller set with 1', async () => {
    const fetchMock = stubFetch()

    await apiFetch('/api/x', { method: 'POST', headers: { 'X-Axis-Csrf': '0' } })

    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('X-Axis-Csrf')).toBe('1')
  })

  it('passes a GET through unchanged, without the CSRF header', async () => {
    const fetchMock = stubFetch()
    const init = { method: 'GET', headers: { Accept: 'application/json' } }

    await apiFetch('/api/x', init)

    expect(fetchMock).toHaveBeenCalledWith('/api/x', init)
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).has('X-Axis-Csrf')).toBe(false)
  })

  it('passes a call without options through unchanged, as a GET', async () => {
    const fetchMock = stubFetch()
    const signal = new AbortController().signal

    await apiFetch('/api/x')
    await apiFetch('/api/y', { signal })

    expect(fetchMock.mock.calls).toEqual([
      ['/api/x', undefined],
      ['/api/y', { signal }],
    ])
  })
})
