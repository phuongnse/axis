import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import type { UserList, UserRoles } from '../platform/users'
import { TextProvider } from '../platform/TextProvider'
import { isRequiredLabel, openDropdown, optionTexts } from '../test/antd'
import { expectTitleAloneInBand } from '../test/pageBand'
import { clickInDialog, dialog, pick } from '../test/tableFilter'
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
  page: 1,
  pageSize: 20,
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
    {
      id: 'u3',
      displayName: 'Cid Disabled',
      email: 'cid@axis.local',
      provider: null,
      status: 'disabled',
      lastSignedInAt: '2026-10-27T09:00:00Z',
    },
  ],
}

const applicationId = '5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c01'

const roles: UserRoles = {
  displayName: 'Admin',
  email: 'admin@axis.local',
  given: [
    { applicationId, role: 'buyer', declared: true },
    { applicationId, role: 'dropped', declared: false },
  ],
  applications: [{ id: applicationId, name: 'Purchasing', roles: ['approver', 'buyer'] }],
  platformRoles: ['author', 'publisher', 'developer', 'operator', 'administrator'],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const outsideClientsPath = '/api/tenant/outside-clients'
const applicationPackagesPath = '/api/tenant/application-packages'
const hostsPath = '/api/tenant/hosts'

/** Stubs `fetch`. `/api/me` answers `me`, the administrator unless a test says otherwise. */
function stubServer(answer: (input: string, init?: RequestInit) => Response, me: object = administrator) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) =>
    input === '/api/tenant/usage' ||
    input === hostsPath ||
    input === outsideClientsPath ||
    input === applicationPackagesPath
      ? new Response(null, { status: 404 })
      : input === '/api/me'
        ? json(me)
        : answer(input, init),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
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
  studioAi: false,
  studioDeveloper: false,
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
    return isList(input) ? json(users) : json(roles)
  })
}

/** Whether the address is the list route, with or without a query. Another route under /api/users has a path after it. */
function isList(input: string) {
  return /^\/api\/users(\?|$)/.test(input)
}

/** Opens the "…" menu of a row and chooses the command. A row's commands are entries of that menu, not buttons. */
async function chooseCommand(row: HTMLElement, name: string) {
  await userEvent.click(within(row).getByRole('button', { name: 'Actions' }))
  await userEvent.click(await screen.findByRole('menuitem', { name }))
}

function posts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
}

/** Opens the invite form, fills it and asks to invite, which opens the confirmation. */
async function fillInvitation() {
  renderPage()
  await userEvent.click(await screen.findByRole('button', { name: 'Invite…' }))
  const form = await confirmation('Invite a person')
  await userEvent.type(within(form).getByLabelText(/^Email\*?$/), 'new@axis.local')
  await userEvent.type(within(form).getByLabelText(/^Display name\*?$/), 'New Person')
  return form
}

/** The confirmation dialog with this title. Another dialog may be open, so the role alone does not find it. */
async function confirmation(title: string) {
  return (await screen.findByText(title)).closest<HTMLElement>('[role="dialog"]')!
}

function requested(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.map(([input]) => input)
}

function LocationProbe() {
  return <output data-testid="location">{useLocation().search}</output>
}

function renderPage(initialEntry = '/users') {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[initialEntry]}>
          <UsersPage />
          <LocationProbe />
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
  it('holds the title alone in the band', async () => {
    stubServer(() => json(users))
    renderPage()

    await screen.findByText('Ann Invited')

    expectTitleAloneInBand('Users')
  })

  it('shows the columns and a row of each kind', async () => {
    stubServer(() => json(users))
    renderPage()

    const row = (await screen.findByText('Ann Invited')).closest('tr')!
    expect(screen.getAllByText('Users').length).toBeGreaterThan(0)
    for (const title of ['Name', 'Email', 'Signs in with', 'Status', 'Last sign-in']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeVisible()
    }
    expect(within(row).getByText('ann@axis.local')).toBeVisible()
    expect(within(row).getByText('Account')).toBeVisible()
    expect(within(row).getByText('Invited')).toBeVisible()
    expect(within(row).getByText('Invited').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    expect(within(row).getByText('—')).toBeVisible()
    const other = screen.getByText('Bob Active').closest('tr')!
    expect(within(other).getByText('google')).toBeVisible()
    expect(within(other).getByText('Active')).toBeVisible()
    expect(within(other).getByText('Active').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    const disabled = screen.getByText('Cid Disabled').closest('tr')!
    expect(within(disabled).getByText('Disabled').closest('[data-tone]')).toHaveAttribute('data-tone', 'muted')
  })

  it('shows the Connected apps tab next to People when the outside clients route answers 200', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input === outsideClientsPath) {
          return json({ outsideClients: 'allowList', allowedClients: ['claude'] })
        }
        if (input === '/api/tenant/connected-apps') {
          return json({ items: [] })
        }
        return input === '/api/tenant/usage' || input === hostsPath ? new Response(null, { status: 404 }) : json(users)
      }),
    )
    renderPage()

    expect(await screen.findByRole('tab', { name: 'People', selected: true })).toBeVisible()
    expect(screen.getByText('Ann Invited')).toBeVisible()
    await userEvent.click(screen.getByRole('tab', { name: 'Connected apps' }))
    expect(await screen.findByTestId('connected-apps')).toBeVisible()
    expect(screen.getByRole('radio', { name: /^Allow list/ })).toBeChecked()
    expect(screen.getByText('claude')).toBeVisible()
  })

  it('shows no Connected apps tab when the outside clients route answers 403', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input === outsideClientsPath
          ? new Response(null, { status: 403 })
          : input === '/api/tenant/usage' || input === hostsPath
            ? new Response(null, { status: 404 })
            : json(users),
      ),
    )
    renderPage()

    expect(await screen.findByText('Ann Invited')).toBeVisible()
    expect(screen.getByRole('tab', { name: 'People' })).toBeVisible()
    expect(screen.queryByRole('tab', { name: 'Connected apps' })).toBeNull()
  })

  it('shows Export data, Usage, Hosts, Model key and Application packages as parts under the table, each with a rule above', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        switch (input) {
          case '/api/me':
            return json({ ...administrator, tenantExports: true })
          case '/api/tenant/usage':
            return json({ quotas: { users: { limit: 5, used: 2 } }, days: [] })
          case hostsPath:
            return json({ items: [] })
          case '/api/tenant/model-key':
            return json({ set: false })
          case applicationPackagesPath:
            return json({ on: false })
          case outsideClientsPath:
            return new Response(null, { status: 404 })
          default:
            return json(users)
        }
      }),
    )
    renderPage()

    expect(await screen.findByText('Ann Invited')).toBeVisible()
    for (const name of ['Export data', 'Usage', 'Hosts', 'Model key', 'Application packages']) {
      const region = await screen.findByRole('region', { name })
      expect(within(region).getByRole('heading', { level: 2, name })).toBeVisible()
    }
    expect(screen.getAllByRole('separator')).toHaveLength(5)
  })

  it('shows none of the five parts and no rule when their routes refuse', async () => {
    const fetchMock = vi.fn(async (input: string) => {
      switch (input) {
        case '/api/me':
          return json({ ...administrator, tenantExports: false })
        case '/api/tenant/usage':
        case hostsPath:
        case '/api/tenant/model-key':
        case applicationPackagesPath:
          return new Response(null, { status: 403 })
        case outsideClientsPath:
          return new Response(null, { status: 404 })
        default:
          return json(users)
      }
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    expect(await screen.findByText('Ann Invited')).toBeVisible()
    await vi.waitFor(() => {
      for (const path of [
        '/api/me',
        '/api/tenant/usage',
        hostsPath,
        '/api/tenant/model-key',
        applicationPackagesPath,
      ]) {
        expect(fetchMock).toHaveBeenCalledWith(path, expect.anything())
      }
    })
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)))

    expect(screen.queryByRole('region')).toBeNull()
    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('searches from page 1 and pages from the server', async () => {
    const fetchMock = stubServer(() => json(users))
    renderPage()
    await screen.findByText('Ann Invited')
    expect(requested(fetchMock).filter(isList)).toEqual(['/api/users'])

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'ann{Enter}')
    await vi.waitFor(() => expect(requested(fetchMock)).toContain('/api/users?search=ann'))

    await userEvent.click(await screen.findByTitle('2'))
    await vi.waitFor(() => expect(requested(fetchMock)).toContain('/api/users?search=ann&page=2'))
  })

  it('keeps a search typed in the Search box in the address and asks the server for it', async () => {
    const ann: UserList = { page: 1, pageSize: 20, totalCount: 1, items: [users.items[0]] }
    const fetchMock = stubServer((input) => (input.includes('search=ann') ? json(ann) : json(users)))
    renderPage()
    await screen.findByText('Bob Active')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'ann{Enter}')

    await vi.waitFor(() => expect(requested(fetchMock)).toContain('/api/users?search=ann'))
    expect(screen.getByTestId('location')).toHaveTextContent('search=ann')
    expect(await screen.findByText('Ann Invited')).toBeVisible()
    expect(screen.queryByText('Bob Active')).toBeNull()
  })

  it('opens at the search and the page of the address and shows the search in the box', async () => {
    const fetchMock = stubServer(() => json(users))
    renderPage('/users?search=ann&page=2')

    expect(await screen.findByText('Ann Invited')).toBeVisible()
    expect(requested(fetchMock).find(isList)).toBe('/api/users?search=ann&page=2')
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('ann')
  })

  it('keeps the search, filter, sort and page size in the address and the request', async () => {
    const filter = JSON.stringify({ all: [{ field: 'status', op: 'eq', value: 'invited' }] })
    const fetchMock = stubServer(() => json(users))
    renderPage(`/users?${new URLSearchParams({ search: 'ann', filter, sort: '-lastSignedInAt' })}`)

    expect(await screen.findByText('Ann Invited')).toBeVisible()
    const first = new URLSearchParams(requested(fetchMock).find(isList)!.split('?')[1])
    expect(first.get('search')).toBe('ann')
    expect(first.get('filter')).toBe(filter)
    expect(first.get('sort')).toBe('-lastSignedInAt')
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('ann')
    expect(await screen.findByText('1–20 of 45')).toBeVisible()

    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('50 / page'))

    await vi.waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('pageSize=50'))
    await vi.waitFor(() =>
      expect(
        requested(fetchMock)
          .filter(isList)
          .some((input) => input.includes('pageSize=50')),
      ).toBe(true),
    )
  })

  it('offers every column in the filter, each with the value control of its kind', async () => {
    stubServer(() => json(users))
    renderPage()
    await screen.findByText('Ann Invited')

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
    const column = () => within(dialog()).getByRole('combobox', { name: 'Column' })
    await userEvent.click(column())
    expect([...optionTexts(await openDropdown())].sort()).toEqual(
      ['Name', 'Email', 'Signs in with', 'Status', 'Last sign-in'].sort(),
    )
    // The list is closed again, so the picks below open it as a person does.
    await userEvent.click(column())

    await pick('Column', 0, 'Name')
    expect(within(dialog()).getByRole('textbox', { name: 'Value' })).toBeInTheDocument()
    await pick('Column', 0, 'Status')
    expect(within(dialog()).getByRole('combobox', { name: 'Value' })).toBeInTheDocument()
    await pick('Column', 0, 'Last sign-in')
    expect(dialog().querySelector('.ant-picker')).not.toBeNull()
  })

  it('sends the filter built in Filter to the server as filter, from page 1', async () => {
    const fetchMock = stubServer(() => json(users))
    renderPage('/users?page=2')
    await screen.findByText('Ann Invited')

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'Status')
    await pick('Value', 0, 'Invited')
    await clickInDialog('Apply')

    const filter = JSON.stringify({ all: [{ field: 'status', op: 'eq', value: 'invited' }] })
    await vi.waitFor(() => {
      const query = new URLSearchParams(requested(fetchMock).filter(isList).at(-1)!.split('?')[1])
      expect(query.get('filter')).toBe(filter)
      expect(query.has('page')).toBe(false)
    })
    expect(new URLSearchParams(screen.getByTestId('location').textContent!).get('filter')).toBe(filter)
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

  it('links the name to the page of the person and offers no Edit button', async () => {
    stubServer((input) => (isList(input) ? json(users) : json(roles)))
    renderPage()

    const link = await screen.findByRole('link', { name: 'Ann Invited' })

    expect(link).toHaveAttribute('href', '/users/u1')
    expect(screen.queryByRole('button', { name: /edit/i })).toBeNull()
  })

  it("follows the signed-in administrator's own name with (you) and no other name", async () => {
    stubServer(() =>
      json({
        ...users,
        items: [{ ...users.items[0], id: 'me', displayName: 'Admin', status: 'active' }, users.items[0]],
      }),
    )
    renderPage()

    const own = await screen.findByRole('link', { name: 'Admin' })
    expect(own.closest('td')).toHaveTextContent('Admin (you)')
    expect(screen.getByRole('link', { name: 'Ann Invited' }).closest('td')).not.toHaveTextContent('(you)')
  })

  it('shows the name as plain text, with no link, to a reader who may not administer users', async () => {
    stubServer(() => json(users), { ...administrator, users: false })
    renderPage()

    expect(await screen.findByText('Ann Invited')).toBeVisible()
    expect(screen.queryByRole('link', { name: 'Ann Invited' })).toBeNull()
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
    await vi.waitFor(() => expect(requested(fetchMock).filter(isList)).toHaveLength(2))
  })

  it('invites without roles when the roles cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer((input, init) => {
      if (init?.method === 'POST') {
        return json({ id: 'u9' }, 201)
      }
      return isList(input) ? json(users) : new Response(null, { status: 500 })
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

  it('lays the invite form out with each label above its input', async () => {
    stubInvitations()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Invite…' }))
    const form = await confirmation('Invite a person')

    expect(form.querySelector('form')).toHaveClass('ant-form-vertical')
  })

  it('gives the email and the display name of the invite form their help', async () => {
    stubInvitations()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Invite…' }))
    const form = await confirmation('Invite a person')

    expect(within(form).getByLabelText(/^Email\*?$/)).toHaveAccessibleDescription(
      'The invitation goes to this address.',
    )
    expect(within(form).getByLabelText(/^Display name\*?$/)).toHaveAccessibleDescription(
      'The name that shows for the person in the users list.',
    )
  })

  it('marks the email and the display name of the invite form required, and groups the roles with their help', async () => {
    stubInvitations()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Invite…' }))
    const form = await confirmation('Invite a person')

    expect(isRequiredLabel(within(form).getByText('Email'))).toBe(true)
    expect(isRequiredLabel(within(form).getByText('Display name'))).toBe(true)
    const group = await within(form).findByRole('group', { name: 'Roles' })
    expect(group).toHaveAccessibleDescription('Tick the roles to give the person. You can change them later.')
    expect(within(group).getByRole('checkbox', { name: 'approver' })).toBeInTheDocument()
  })

  it('asks for the fields of the form before it asks to invite', async () => {
    const fetchMock = stubInvitations()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Invite…' }))
    const form = await confirmation('Invite a person')
    await userEvent.type(within(form).getByLabelText(/^Email\*?$/), 'not-an-email')
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))

    await vi.waitFor(() => expect(form.querySelectorAll('.ant-form-item-explain-error').length).toBe(2))
    expect(within(form).getByText('Enter a display name.')).toBeInTheDocument()
    expect(screen.queryByText('Invite this person?')).toBeNull()
    expect(posts(fetchMock)).toHaveLength(0)
  })

  it('puts the focus on the empty email and sends nothing', async () => {
    const fetchMock = stubInvitations()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Invite…' }))
    const form = await confirmation('Invite a person')
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))

    expect(await within(form).findByText('Enter an email.')).toBeInTheDocument()
    expect(within(form).getByLabelText(/^Email\*?$/)).toHaveFocus()
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
    expect(within(form).getByLabelText(/^Email\*?$/)).toHaveValue('new@axis.local')
  })

  it('shows a generic text and loads the list again when the invite fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubInvitations(502)
    const form = await fillInvitation()
    await userEvent.click(within(form).getByRole('button', { name: 'Invite' }))
    const dialog = await confirmation('Invite this person?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Invite' }))

    expect(await screen.findByTestId('users-invite-problem')).toHaveTextContent('The invitation could not be sent.')
    await vi.waitFor(() => expect(requested(fetchMock).filter(isList)).toHaveLength(2))
  })

  it('offers Resend invitation on an invited account only and sends it after a confirmation', async () => {
    const fetchMock = stubInvitations()
    renderPage()
    const row = (await screen.findByText('Ann Invited')).closest('tr')!
    expect(screen.queryByRole('button', { name: 'Resend invitation' })).toBeNull()

    await chooseCommand(row, 'Resend invitation')
    const dialog = await confirmation('Resend the invitation?')
    expect(dialog).toHaveTextContent('Person: Ann Invited')
    expect(dialog).toHaveTextContent('Email: ann@axis.local')
    expect(posts(fetchMock)).toHaveLength(0)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Resend' }))

    expect(await screen.findByTestId('users-done')).toHaveTextContent('The invitation was sent again.')
    const [post] = posts(fetchMock)
    expect(post[0]).toBe('/api/users/u1/resend-invitation')
    expect(post[1]!.body).toBeUndefined()
    await vi.waitFor(() => expect(requested(fetchMock).filter(isList)).toHaveLength(2))
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
    await chooseCommand((await screen.findByText('Ann Invited')).closest('tr')!, 'Resend invitation')
    const dialog = await confirmation('Resend the invitation?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Resend' }))

    expect(await screen.findByTestId('users-command-problem')).toHaveTextContent(text)
    expect(screen.queryByTestId('users-done')).toBeNull()
  })

  it('offers Disable, Enable and Reset authenticator by status and by how the person signs in', async () => {
    stubInvitations()
    renderPage()
    await screen.findByText('Ann Invited')
    const names = async (person: string) => {
      await userEvent.click(within(screen.getByText(person).closest('tr')!).getByRole('button', { name: 'Actions' }))
      // A menu that was closed stays in the page, so the newest one is the open one.
      const menu = (await screen.findAllByRole('menu', { name: 'Actions' })).at(-1)!
      const entries = within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent)
      await userEvent.keyboard('{Escape}')
      return entries
    }

    expect(await names('Ann Invited')).toEqual(['Resend invitation', 'Disable', 'Reset authenticator'])
    expect(await names('Cid Disabled')).toEqual(['Enable', 'Reset authenticator'])
    expect(await names('Bob Active')).toEqual(['Disable'])
  })

  it.each([
    ['Ann Invited', 'u1', 'Disable', 'Disable this person?', 'disable', 'The person was disabled.'],
    ['Cid Disabled', 'u3', 'Enable', 'Enable this person?', 'enable', 'The person was enabled.'],
    [
      'Cid Disabled',
      'u3',
      'Reset authenticator',
      'Reset the authenticator?',
      'reset-authenticator',
      'The authenticator was reset.',
    ],
  ])(
    'asks before %s gets %s, then calls the route and loads the list again',
    async (person, id, button, title, route, done) => {
      const fetchMock = stubInvitations()
      renderPage()
      const row = (await screen.findByText(person)).closest('tr')!

      await chooseCommand(row, button)
      const dialog = await confirmation(title)
      expect(within(dialog).getByTestId('users-command-confirm')).toHaveTextContent(`Person: ${person}`)
      expect(dialog).toHaveTextContent(`Email: ${users.items.find((user) => user.id === id)!.email}`)
      expect(posts(fetchMock)).toHaveLength(0)
      await userEvent.click(
        within(dialog).getByRole('button', { name: button === 'Reset authenticator' ? 'Reset' : button }),
      )

      expect(await screen.findByTestId('users-done')).toHaveTextContent(done)
      const [post] = posts(fetchMock)
      expect(post[0]).toBe(`/api/users/${id}/${route}`)
      expect(post[1]!.body).toBeUndefined()
      await vi.waitFor(() => expect(requested(fetchMock).filter(isList)).toHaveLength(2))
    },
  )

  it.each([
    [403, 'Sign in again to disable a person.'],
    [409, 'An administrator cannot disable themselves.'],
    [502, 'The change was not made.'],
  ])('shows a fixed text when the disable answers %i', async (status, text) => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubInvitations(status)
    renderPage()
    const row = (await screen.findByText('Bob Active')).closest('tr')!
    await chooseCommand(row, 'Disable')
    const dialog = await confirmation('Disable this person?')
    expect(dialog).toHaveTextContent('Their sessions end and they cannot sign in until they are enabled.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Disable' }))

    expect(await screen.findByTestId('users-command-problem')).toHaveTextContent(text)
    expect(screen.queryByTestId('users-done')).toBeNull()
    await vi.waitFor(() => expect(requested(fetchMock).filter(isList)).toHaveLength(2))
  })

  it('shows the generic text when an enable or a reset is refused', async () => {
    stubInvitations(403)
    renderPage()
    const row = (await screen.findByText('Cid Disabled')).closest('tr')!
    await chooseCommand(row, 'Reset authenticator')
    const dialog = await confirmation('Reset the authenticator?')
    expect(dialog).toHaveTextContent(
      'Their sessions end, and their next sign-in asks them to add an authenticator again.',
    )
    await userEvent.click(within(dialog).getByRole('button', { name: 'Reset' }))

    expect(await screen.findByTestId('users-command-problem')).toHaveTextContent('The change was not made.')
  })

  it('offers the Vietnamese lock action on an active person', async () => {
    stubServer(() => json(users))
    const vietnamese: Platform = { ...platform, current: { locale: 'vi', texts: platformVietnamese } }
    render(
      <PlatformContext.Provider value={vietnamese}>
        <TextProvider catalogs={[{ texts: platformVietnamese, fallbackTexts: platformEnglish }]} development={false}>
          <MemoryRouter>
            <UsersPage />
          </MemoryRouter>
        </TextProvider>
      </PlatformContext.Provider>,
    )

    const row = (await screen.findByText('Bob Active')).closest('tr')!
    await userEvent.click(within(row).getByRole('button', { name: 'Thao tác' }))
    expect(await screen.findByRole('menuitem', { name: 'Khóa' })).toBeVisible()
  })
})
