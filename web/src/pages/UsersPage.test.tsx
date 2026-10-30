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

const administrator = {
  id: 'me',
  displayName: 'Admin',
  roles: [],
  studio: false,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  console: false,
  users: true,
}

/** Answers the current user, the list and the roles by their address, and a POST with `status`. */
function stubInvitations(status = 201) {
  return stubServer((input, init) => {
    if (init?.method === 'POST') {
      return status === 201 ? json({ id: 'u9' }, 201) : new Response(null, { status })
    }
    if (input === '/api/me') {
      return json(administrator)
    }
    return input.startsWith('/api/users?') ? json(users) : json(roles)
  })
}

function posts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
}

/** Opens the invite form, fills it and asks to invite, which opens the confirmation. */
async function fillInvitation() {
  renderPage()
  await userEvent.click(await screen.findByRole('button', { name: 'Invite' }))
  const form = await confirmation('Invite a person')
  await userEvent.type(within(form).getByLabelText('Email'), 'new@axis.local')
  await userEvent.type(within(form).getByLabelText('Display name'), 'New Person')
  return form
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

  it('invites a person with the chosen roles after a confirmation and loads the list again', async () => {
    const fetchMock = stubInvitations()
    const form = await fillInvitation()
    expect(requested(fetchMock)).toContain('/api/users/me/roles')
    await userEvent.click(
      within(await within(form).findByRole('region', { name: 'Purchasing' })).getByRole('checkbox', {
        name: 'approver',
      }),
    )
    await userEvent.click(
      within(within(form).getByRole('region', { name: 'Platform' })).getByRole('checkbox', { name: 'operator' }),
    )
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))

    const dialog = await confirmation('Invite this person?')
    expect(within(dialog).getByTestId('users-invite-confirm')).toHaveTextContent('Email: new@axis.local')
    expect(dialog).toHaveTextContent('Name: New Person')
    expect(dialog).toHaveTextContent('Roles: approver (Purchasing), operator (Platform)')
    expect(posts(fetchMock)).toHaveLength(0)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Invite' }))

    expect(await screen.findByTestId('users-done')).toHaveTextContent('The invitation was sent.')
    const [post] = posts(fetchMock)
    expect(post[0]).toBe('/api/users/invite')
    expect(JSON.parse(post[1]!.body as string)).toEqual({
      email: 'new@axis.local',
      displayName: 'New Person',
      roles: [{ applicationId, role: 'approver' }, { role: 'operator' }],
    })
    await vi.waitFor(() =>
      expect(requested(fetchMock).filter((input) => input.startsWith('/api/users?'))).toHaveLength(2),
    )
  })

  it('invites without roles when the roles cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer((input, init) => {
      if (init?.method === 'POST') {
        return json({ id: 'u9' }, 201)
      }
      return input.startsWith('/api/users?') ? json(users) : new Response(null, { status: 500 })
    })
    const form = await fillInvitation()
    expect(await within(form).findByTestId('users-invite-roles-failed')).toHaveTextContent(
      'The roles could not be loaded. You can invite without roles.',
    )
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))
    const dialog = await confirmation('Invite this person?')
    expect(dialog).toHaveTextContent('Roles: —')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Invite' }))

    await screen.findByTestId('users-done')
    expect(JSON.parse(posts(fetchMock)[0][1]!.body as string).roles).toEqual([])
  })

  it('asks for the fields of the form before it asks to invite', async () => {
    const fetchMock = stubInvitations()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Invite' }))
    const form = await confirmation('Invite a person')
    await userEvent.type(within(form).getByLabelText('Email'), 'not-an-email')
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))

    await vi.waitFor(() => expect(form.querySelectorAll('.ant-form-item-explain-error').length).toBe(2))
    expect(screen.queryByText('Invite this person?')).toBeNull()
    expect(posts(fetchMock)).toHaveLength(0)
  })

  it.each([
    [403, 'Sign in again to give a platform role.'],
    [409, 'An account already uses this email.'],
  ])('shows a fixed text when the invite answers %i', async (status, text) => {
    stubInvitations(status)
    const form = await fillInvitation()
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))
    const dialog = await confirmation('Invite this person?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Invite' }))

    expect(await screen.findByTestId('users-invite-problem')).toHaveTextContent(text)
    expect(screen.queryByTestId('users-done')).toBeNull()
    expect(within(form).getByLabelText('Email')).toHaveValue('new@axis.local')
  })

  it('shows a generic text and loads the list again when the invite fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubInvitations(502)
    const form = await fillInvitation()
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))
    const dialog = await confirmation('Invite this person?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Invite' }))

    expect(await screen.findByTestId('users-invite-problem')).toHaveTextContent('The invitation could not be sent.')
    await vi.waitFor(() =>
      expect(requested(fetchMock).filter((input) => input.startsWith('/api/users?'))).toHaveLength(2),
    )
  })

  it('offers Resend invitation on an invited account only and sends it after a confirmation', async () => {
    const fetchMock = stubInvitations()
    renderPage()
    const row = (await screen.findByText('Ann Invited')).closest('tr')!
    expect(screen.getAllByRole('button', { name: 'Resend invitation' })).toHaveLength(1)
    expect(within(screen.getByText('Bob Active').closest('tr')!).queryByRole('button')).toBeNull()

    await userEvent.click(within(row).getByRole('button', { name: 'Resend invitation' }))
    const dialog = await confirmation('Resend the invitation?')
    expect(dialog).toHaveTextContent('Person: Ann Invited')
    expect(dialog).toHaveTextContent('Email: ann@axis.local')
    expect(posts(fetchMock)).toHaveLength(0)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Resend' }))

    expect(await screen.findByTestId('users-done')).toHaveTextContent('The invitation was sent again.')
    const [post] = posts(fetchMock)
    expect(post[0]).toBe('/api/users/u1/resend-invitation')
    expect(post[1]!.body).toBeUndefined()
    await vi.waitFor(() =>
      expect(requested(fetchMock).filter((input) => input.startsWith('/api/users?'))).toHaveLength(2),
    )
    expect(requested(fetchMock)).not.toContain('/api/users/u1/roles')
    expect(screen.queryByTestId('user-roles')).toBeNull()
  })

  it.each([
    [403, 'This page is for administrators.'],
    [409, 'This person has a password or signs in through a provider, so they cannot get a new invitation.'],
    [502, 'The invitation could not be sent.'],
  ])('shows a fixed text when the resend answers %i', async (status, text) => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubInvitations(status)
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Resend invitation' }))
    const dialog = await confirmation('Resend the invitation?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Resend' }))

    expect(await screen.findByTestId('users-command-problem')).toHaveTextContent(text)
    expect(screen.queryByTestId('users-done')).toBeNull()
  })
})
