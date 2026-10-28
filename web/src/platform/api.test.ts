import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch, setApiGuard, startSignIn } from './api'
import { fetchCurrentUser } from './currentUser'
import { assignLocation } from './navigation'
import { isSignInNeeded, subscribeSignInNeeded } from './signInNeeded'

vi.mock('./navigation')

beforeEach(() => {
  vi.mocked(assignLocation).mockReset()
  history.pushState(null, '', '/e2e/notes?page=2')
})

function stubFetch(status = 204) {
  const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => new Response(null, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('startSignIn', () => {
  it('returns to / from the sign-in page after a refused sign-in', () => {
    history.pushState(null, '', '/?signIn=failed')

    startSignIn()

    expect(assignLocation).toHaveBeenCalledWith('/api/auth/sign-in?returnUrl=%2F')
  })

  it('keeps another path that has a signIn query parameter', () => {
    history.pushState(null, '', '/e2e/notes?signIn=failed')

    startSignIn()

    expect(assignLocation).toHaveBeenCalledWith('/api/auth/sign-in?returnUrl=%2Fe2e%2Fnotes%3FsignIn%3Dfailed')
  })
})

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

  it('asks to sign in on a 401 and does not start sign-in', async () => {
    stubFetch(401)

    const response = await apiFetch('/api/x')

    expect(response.status).toBe(401)
    expect(isSignInNeeded()).toBe(true)
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('does not ask to sign in on a 401 when the caller opts out', async () => {
    stubFetch(401)

    const response = await apiFetch('/api/x', undefined, { promptSignInOnUnauthorized: false })

    expect(response.status).toBe(401)
    expect(isSignInNeeded()).toBe(false)
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('does not ask to sign in on a success', async () => {
    stubFetch(200)

    await apiFetch('/api/x')

    expect(isSignInNeeded()).toBe(false)
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('tells a listener once, however many calls answer 401', async () => {
    stubFetch(401)
    const listener = vi.fn()
    const unsubscribe = subscribeSignInNeeded(listener)

    await apiFetch('/api/x')
    await apiFetch('/api/y')
    unsubscribe()

    expect(listener).toHaveBeenCalledOnce()
  })
})

describe('setApiGuard', () => {
  afterEach(() => setApiGuard(null))

  it('answers an /api/apps/ request with the guard and never calls fetch', async () => {
    const fetchMock = stubFetch()
    const guard = vi.fn(() => new Response('guarded', { status: 200 }))
    setApiGuard(guard)

    const response = await apiFetch('/api/apps/A/entities/B/records?page=1', { method: 'POST' })

    expect(await response.text()).toBe('guarded')
    expect(guard).toHaveBeenCalledExactlyOnceWith('/api/apps/A/entities/B/records?page=1', { method: 'POST' })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('does not ask to sign in on a guarded 401', async () => {
    setApiGuard(() => new Response(null, { status: 401 }))

    const response = await apiFetch('/api/apps/A/entities/B/records')

    expect(response.status).toBe(401)
    expect(isSignInNeeded()).toBe(false)
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('sends other paths to fetch while the guard is set', async () => {
    const fetchMock = stubFetch()
    const guard = vi.fn(() => new Response(null, { status: 200 }))
    setApiGuard(guard)

    await apiFetch('/api/site')
    await apiFetch('/api/studio/apps')

    expect(fetchMock.mock.calls.map(([url]) => url)).toEqual(['/api/site', '/api/studio/apps'])
    expect(guard).not.toHaveBeenCalled()
  })

  it('sends /api/apps/ requests to fetch once the guard is cleared', async () => {
    const fetchMock = stubFetch()
    setApiGuard(() => new Response(null, { status: 200 }))
    setApiGuard(null)

    await apiFetch('/api/apps/A/entities/B/records')

    expect(fetchMock).toHaveBeenCalledExactlyOnceWith('/api/apps/A/entities/B/records', undefined)
  })
})

describe('fetchCurrentUser', () => {
  it('returns null on a 401 and does not ask to sign in', async () => {
    const fetchMock = stubFetch(401)

    expect(await fetchCurrentUser()).toBeNull()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/me')
    expect(isSignInNeeded()).toBe(false)
    expect(assignLocation).not.toHaveBeenCalled()
  })
})
