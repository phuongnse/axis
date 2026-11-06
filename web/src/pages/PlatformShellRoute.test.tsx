import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { CurrentUser } from '../platform/currentUser'
import type { NotificationPage } from '../platform/notifications'
import { ThemeModeProvider } from '../platform/theme'
import { PlatformContext, type Platform } from './context'
import { PlatformShellRoute } from './PlatformShellRoute'

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

const user: CurrentUser = {
  id: 'u1',
  displayName: 'Ada Admin',
  roles: [],
  studio: false,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  console: false,
  users: true,
  tenantStatus: 'active',
  billingUrl: null,
  tenantExports: false,
}

const notices: NotificationPage = {
  items: [
    {
      id: 'n1',
      releaseId: null,
      subject: 'Emails are held until tomorrow',
      body: 'This tenant has sent its 100 emails for today.',
      open: null,
      read: false,
      createdAt: '2026-01-01T00:00:00Z',
    },
  ],
  page: 1,
  pageSize: 10,
  totalCount: 1,
  unreadCount: 1,
}

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function stubApi(current: CurrentUser) {
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = String(input)
    if (url === '/api/me') {
      return Promise.resolve(json(current))
    }
    if (url === '/api/sites') {
      return Promise.resolve(json({ sites: [] }))
    }
    if (url === '/api/notifications?page=1&pageSize=10') {
      return Promise.resolve(json(notices))
    }
    return Promise.resolve(new Response(null, { status: 404 }))
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderShell() {
  render(
    <ThemeModeProvider>
      <PlatformContext.Provider value={platform}>
        <MemoryRouter>
          <PlatformShellRoute>
            <div />
          </PlatformShellRoute>
        </MemoryRouter>
      </PlatformContext.Provider>
    </ThemeModeProvider>,
  )
}

describe('PlatformShellRoute', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the notification bell with the tenant notices for an administrator', async () => {
    stubApi(user)
    renderShell()

    expect(await screen.findByRole('button', { name: 'Notifications, 1 unread' })).toBeInTheDocument()
  })

  it('shows no bell and never asks for notices for another user', async () => {
    const fetchMock = stubApi({ ...user, users: false })
    renderShell()

    expect(await screen.findByRole('button', { name: /Ada Admin/ })).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sites', expect.anything()))
    expect(screen.queryByRole('button', { name: /^Notifications/ })).toBeNull()
    expect(fetchMock.mock.calls.some(([input]) => String(input).startsWith('/api/notifications'))).toBe(false)
  })
})
