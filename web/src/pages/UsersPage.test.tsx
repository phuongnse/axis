import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { UserList, UserRoles } from '../platform/users'
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

const applicationId = '5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c01'

const roles: UserRoles = {
  given: [
    { applicationId, role: 'buyer', declared: true },
    { applicationId, role: 'dropped', declared: false },
  ],
  applications: [{ id: applicationId, name: 'Purchasing', roles: ['approver', 'buyer'] }],
  platformRoles: ['author', 'publisher', 'operator', 'administrator'],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stubServer(answer: (input: string, init?: RequestInit) => Response) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => answer(input, init))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** Answers the list and the roles by their address, and any other request with `change`. */
function stubRoles(change: () => Response = () => new Response(null, { status: 204 })) {
  return stubServer((input, init) => {
    if (input.startsWith('/api/users?')) {
      return json(users)
    }
    return init?.method === 'POST' ? change() : json(roles)
  })
}

async function openAnn() {
  renderPage()
  await userEvent.click(await screen.findByText('Ann Invited'))
  return await screen.findByTestId('user-roles')
}

function roleRow(drawer: HTMLElement, name: string) {
  return within(drawer)
    .getAllByTestId('user-role')
    .find((row) => within(row).queryByText(name) !== null)!
}

/** The confirmation dialog with this title. The side panel is a dialog too, so the role alone does not find it. */
async function confirmation(title: string) {
  return (await screen.findByText(title)).closest<HTMLElement>('[role="dialog"]')!
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

  it('opens a row to show the given roles, tags one that is not declared and lists the platform roles', async () => {
    const fetchMock = stubRoles()
    const drawer = await openAnn()

    expect(await within(drawer).findByText('Purchasing')).toBeVisible()
    expect(requested(fetchMock)).toContain('/api/users/u1/roles')
    expect(within(roleRow(drawer, 'buyer')).getByText('Given')).toBeVisible()
    expect(within(roleRow(drawer, 'buyer')).queryByText('Not declared')).toBeNull()
    expect(within(roleRow(drawer, 'dropped')).getByText('Given')).toBeVisible()
    expect(within(roleRow(drawer, 'dropped')).getByText('Not declared')).toBeVisible()
    expect(within(roleRow(drawer, 'approver')).getByRole('button', { name: 'Give' })).toBeVisible()
    expect(within(drawer).getByText('Platform')).toBeVisible()
    expect(within(roleRow(drawer, 'administrator')).getByRole('button', { name: 'Give' })).toBeVisible()
  })

  it('gives a role after a confirmation and loads the roles again', async () => {
    const fetchMock = stubRoles()
    const drawer = await openAnn()
    await within(drawer).findByText('Purchasing')

    await userEvent.click(within(roleRow(drawer, 'approver')).getByRole('button', { name: 'Give' }))
    const dialog = await confirmation('Give this role?')
    expect(within(dialog).getByTestId('user-roles-confirm')).toHaveTextContent('Role: approver')
    expect(dialog).toHaveTextContent('In: Purchasing')
    expect(dialog).toHaveTextContent('Person: Ann Invited')
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Give' }))

    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.filter(([input]) => input === '/api/users/u1/roles')).toHaveLength(2),
    )
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(post[0]).toBe('/api/users/u1/give-role')
    expect(JSON.parse(post[1]!.body as string)).toEqual({ applicationId, role: 'approver' })
  })

  it('shows that an administrator cannot remove their own administrator role on a 409', async () => {
    stubRoles(() => new Response(null, { status: 409 }))
    const drawer = await openAnn()
    await within(drawer).findByText('Purchasing')

    await userEvent.click(within(roleRow(drawer, 'buyer')).getByRole('button', { name: 'Remove' }))
    const dialog = await confirmation('Remove this role?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove' }))

    expect(await screen.findByTestId('user-roles-problem')).toHaveTextContent(
      'An administrator cannot remove their own administrator role.',
    )
  })

  it('asks for a new sign-in on a 403', async () => {
    stubRoles(() => new Response(null, { status: 403 }))
    const drawer = await openAnn()
    await within(drawer).findByText('Purchasing')

    await userEvent.click(within(roleRow(drawer, 'administrator')).getByRole('button', { name: 'Give' }))
    const dialog = await confirmation('Give this role?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Give' }))

    expect(await screen.findByTestId('user-roles-problem')).toHaveTextContent(
      'Sign in again to change a platform role.',
    )
  })
})
