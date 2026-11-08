import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { appRoutes } from '../appRoutes'
import { assignLocation } from '../platform/navigation'
import { ThemeModeProvider } from '../platform/theme'
import type { SiteMetadata } from '../platform/site'

vi.mock('../platform/navigation')

const site: SiteMetadata = {
  name: 'platform',
  titleKey: 'shell.title',
  locales: { default: 'en', fallback: 'en', available: ['en'] },
  navigation: [{ key: 'home', path: '/', labelKey: 'shell.nav.home' }],
  cellConsole: true,
}

const cell = { id: 'vn-1', region: 'vn', tenants: 12, cap: 50, restoreCheck: null }

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Answers the console host's routes. `session` is the status of `GET /api/cell/session`. */
function stubServer(session: number, jobsStatus = 200) {
  const fetchMock = vi.fn(async (input: string) => {
    switch (input) {
      case '/api/site':
        return json(site)
      case '/api/texts/en':
        return json({ locale: 'en', texts: platformEnglish })
      case '/api/cell/session':
        return session === 200 ? json({ displayName: 'Ada Operator' }) : json({ status: session }, session)
      case '/api/cell':
        return json(cell)
      case '/api/cell/jobs':
        return jobsStatus === 200 ? json({ items: [] }) : json({ status: jobsStatus }, jobsStatus)
      default:
        return json({ title: 'Not found', status: 404 }, 404)
    }
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderApp(path: string) {
  const router = createMemoryRouter(appRoutes(false), { initialEntries: [path] })
  return render(
    <ThemeModeProvider>
      <RouterProvider router={router} />
    </ThemeModeProvider>,
  )
}

describe('CellConsoleRoute', () => {
  beforeEach(() => vi.mocked(assignLocation).mockClear())
  afterEach(() => vi.unstubAllGlobals())

  it('sends a person with no session to the console sign-in, back to the page they asked for', async () => {
    stubServer(401)
    renderApp('/jobs')

    await waitFor(() =>
      expect(assignLocation).toHaveBeenCalledExactlyOnceWith('/api/cell/auth/sign-in?returnUrl=%2Fjobs'),
    )
    expect(screen.queryByTestId('cell-account')).not.toBeInTheDocument()
  })

  it('shows the sign-in card after a refused sign-in instead of redirecting again', async () => {
    stubServer(401)
    renderApp('/?signIn=failed')

    expect(await screen.findByTestId('sign-in-card')).toBeInTheDocument()
    expect(screen.getByTestId('sign-in-failed')).toBeInTheDocument()
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('starts the console sign-in from the card, with a return to the home page', async () => {
    stubServer(401)
    window.history.replaceState(null, '', '/?signIn=failed')
    renderApp('/?signIn=failed')

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(assignLocation).toHaveBeenCalledExactlyOnceWith('/api/cell/auth/sign-in?returnUrl=%2F')
    window.history.replaceState(null, '', '/')
  })

  it('shows the forbidden page and no console page to a person without the operator role', async () => {
    const fetchMock = stubServer(403)
    renderApp('/')

    expect(await screen.findByTestId('cell-forbidden')).toHaveTextContent('This page is for platform operators.')
    expect(screen.queryByTestId('cell-overview')).not.toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalledWith('/api/cell', expect.anything())
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it("shows the operator's name in the header and the console's own navigation", async () => {
    const fetchMock = stubServer(200)
    renderApp('/')

    expect(await screen.findByTestId('cell-account')).toHaveTextContent('Ada Operator')
    expect(await screen.findByTestId('cell-overview')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Cell console' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Jobs' })).toHaveAttribute('href', '/jobs')
    expect(screen.getByRole('link', { name: 'Tenants' })).toHaveAttribute('href', '/tenants')
    expect(screen.queryByRole('button', { name: 'Switch to' })).not.toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalledWith('/api/me', expect.anything())
    expect(fetchMock).toHaveBeenCalledWith('/api/cell/session', expect.anything())
  })

  it('reads the session once while the operator moves between pages', async () => {
    const fetchMock = stubServer(200)
    renderApp('/')
    await screen.findByTestId('cell-overview')

    await userEvent.click(screen.getByRole('link', { name: 'Jobs' }))

    expect(await screen.findByRole('heading', { name: 'Jobs' })).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([input]) => input === '/api/cell/session')).toHaveLength(1)
  })

  it('sends the operator to sign in when a page call answers 401', async () => {
    stubServer(200, 401)
    renderApp('/jobs')

    await waitFor(() =>
      expect(assignLocation).toHaveBeenCalledExactlyOnceWith('/api/cell/auth/sign-in?returnUrl=%2Fjobs'),
    )
  })

  it('shows the forbidden page when a page call answers 403', async () => {
    stubServer(200, 403)
    renderApp('/jobs')

    expect(await screen.findByTestId('cell-forbidden')).toBeInTheDocument()
  })

  it('shows the not-found page inside the console for any other path', async () => {
    stubServer(200)
    renderApp('/tenants/a')

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
    expect(screen.getByTestId('cell-account')).toBeInTheDocument()
  })
})
