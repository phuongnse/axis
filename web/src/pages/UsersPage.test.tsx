import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { UserList } from '../platform/users'
import { TextProvider } from '../platform/TextProvider'
import { PlatformContext, type Platform } from './context'
import { UsersPage } from './UsersPage'

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

const users: UserList = {
  totalCount: 45,
  items: [
    {
      id: 'u1',
      displayName: 'Ann Invited',
      email: 'ann@axis.local',
      provider: null,
      status: 'invited',
      lastSignedInAt: null,
    },
    {
      id: 'u2',
      displayName: 'Bob Active',
      email: 'bob@axis.local',
      provider: 'google',
      status: 'active',
      lastSignedInAt: '2026-10-28T09:00:00Z',
    },
  ],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stubServer(answer: () => Response) {
  const fetchMock = vi.fn(async (_input: string) => answer())
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function requested(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.map(([input]) => input)
}

function renderPage() {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <UsersPage />
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('UsersPage', () => {
  it('shows the band, the columns and a row of each kind', async () => {
    stubServer(() => json(users))
    renderPage()

    const row = (await screen.findByText('Ann Invited')).closest('tr')!
    expect(screen.getByText('Axis')).toBeVisible()
    expect(screen.getAllByText('Users').length).toBeGreaterThan(0)
    for (const title of ['Name', 'Email', 'Signs in with', 'Status', 'Last sign-in']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeVisible()
    }
    expect(within(row).getByText('ann@axis.local')).toBeVisible()
    expect(within(row).getByText('Account')).toBeVisible()
    expect(within(row).getByText('Invited')).toBeVisible()
    expect(within(row).getByText('—')).toBeVisible()
    const other = screen.getByText('Bob Active').closest('tr')!
    expect(within(other).getByText('google')).toBeVisible()
    expect(within(other).getByText('Active')).toBeVisible()
  })

  it('searches from page 1 and pages from the server', async () => {
    const fetchMock = stubServer(() => json(users))
    renderPage()
    await screen.findByText('Ann Invited')
    expect(requested(fetchMock)).toEqual(['/api/users?page=1&pageSize=20'])

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search by name or email' }), 'ann{Enter}')
    await vi.waitFor(() => expect(requested(fetchMock)).toContain('/api/users?search=ann&page=1&pageSize=20'))

    await userEvent.click(await screen.findByTitle('2'))
    await vi.waitFor(() => expect(requested(fetchMock)).toContain('/api/users?search=ann&page=2&pageSize=20'))
  })

  it('says the page is for administrators on a 403', async () => {
    stubServer(() => new Response(null, { status: 403 }))
    renderPage()

    expect(await screen.findByText('This page is for administrators.')).toBeVisible()
    expect(screen.getByTestId('users-forbidden')).toBeVisible()
  })

  it('shows the not-found page on a 404', async () => {
    stubServer(() => new Response(null, { status: 404 }))
    renderPage()

    expect(await screen.findByTestId('not-found')).toBeVisible()
  })

  it('shows an error on any other failure', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByTestId('users-error')).toHaveTextContent('The users could not be loaded.')
  })
})
