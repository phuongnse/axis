import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { OwnSessions } from '../platform/ownSessions'
import { assignLocation } from '../platform/navigation'
import { TextProvider } from '../platform/TextProvider'
import { AccountPage } from './AccountPage'
import { PlatformContext, type Platform } from './context'

vi.mock('../platform/navigation')

const platform: Platform = {
  site: {
    name: 'platform',
    titleKey: 'shell.title',
    locales: { default: 'en', fallback: 'en', available: ['en'] },
    navigation: [],
  },
  fallbackTexts: platformEnglish,
  current: { locale: 'en', texts: platformEnglish },
  setCurrent: () => {},
  development: false,
}

const sessions = (endAllowed: boolean): OwnSessions => ({
  endAllowed,
  sessions: [
    { id: 'current-id', signedInAt: '2026-10-28T09:00:00Z', lastRequestAt: '2026-10-28T09:30:00Z', current: true },
    { id: 'other id/1', signedInAt: '2026-10-27T08:00:00Z', lastRequestAt: '2026-10-27T08:10:00Z', current: false },
  ],
})

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs `fetch` with the list, which answers `list()`, and the end routes, which answer `end()`. */
function stubServer(list: () => Response, end: () => Response = () => new Response(null, { status: 204 })) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/me/sessions' && (init?.method ?? 'GET') === 'GET') {
      return list()
    }
    if (input.startsWith('/api/me/sessions') && init?.method === 'DELETE') {
      return end()
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function deleteCalls(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE')
}

function renderPage() {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <AccountPage />
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

beforeEach(() => {
  vi.mocked(assignLocation).mockReset()
})

describe('AccountPage', () => {
  it('disables every end button and offers Sign in again when the session may not end sessions', async () => {
    const fetchMock = stubServer(() => json(sessions(false)))
    renderPage()

    const endButtons = await screen.findAllByRole('button', { name: 'End' })
    expect(endButtons).toHaveLength(1)
    expect(endButtons[0]).toBeDisabled()
    expect(screen.getByRole('button', { name: 'End all other sessions' })).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Sign in again' }))

    expect(assignLocation).toHaveBeenCalledExactlyOnceWith('/api/auth/sign-in?returnUrl=%2Faccount&reauthenticate=true')
    expect(deleteCalls(fetchMock)).toHaveLength(0)
  })

  it('marks the current session and gives it no End button', async () => {
    stubServer(() => json(sessions(true)))
    renderPage()

    const current = (await screen.findByText('This session')).closest('tr')!

    expect(within(current).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign in again' })).not.toBeInTheDocument()
  })

  it('ends one session with the CSRF header, then loads the list again', async () => {
    const fetchMock = stubServer(() => json(sessions(true)))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End' }))

    await waitFor(() => expect(deleteCalls(fetchMock)).toHaveLength(1))
    const [input, init] = deleteCalls(fetchMock)[0]
    expect(input).toBe('/api/me/sessions/other%20id%2F1')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
    await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => !init?.method)).toHaveLength(2))
  })

  it('ends all other sessions, then loads the list again', async () => {
    const fetchMock = stubServer(() => json(sessions(true)))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End all other sessions' }))

    await waitFor(() => expect(deleteCalls(fetchMock)).toHaveLength(1))
    const [input, init] = deleteCalls(fetchMock)[0]
    expect(input).toBe('/api/me/sessions')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
    await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => !init?.method)).toHaveLength(2))
  })

  it('loads the list again after a 403, which now shows Sign in again', async () => {
    let allowed = true
    stubServer(
      () => json(sessions(allowed)),
      () => {
        allowed = false
        return json({ title: 'Sign in again.' }, 403)
      },
    )
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End' }))

    expect(await screen.findByRole('button', { name: 'Sign in again' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'End' })).toBeDisabled()
    expect(screen.queryByTestId('sessions-end-error')).not.toBeInTheDocument()
  })

  it('shows an error when ending a session fails with another status', async () => {
    stubServer(
      () => json(sessions(true)),
      () => new Response(null, { status: 500 }),
    )
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End' }))

    expect(await screen.findByText('The session could not be ended.')).toBeInTheDocument()
  })

  it('disables End all other sessions when the current session is the only one', async () => {
    stubServer(() => json({ endAllowed: true, sessions: [sessions(true).sessions[0]] }))
    renderPage()

    expect(await screen.findByRole('button', { name: 'End all other sessions' })).toBeDisabled()
  })

  it('shows an error when the list cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByText('Your sessions could not be loaded.')).toBeInTheDocument()
    warn.mockRestore()
  })
})
