import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { designTokensFor } from '../platform/designTokens'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { UserRoles } from '../platform/users'
import { TextProvider } from '../platform/TextProvider'
import { expectTitleAloneInBand } from '../test/pageBand'
import { PlatformContext, type Platform } from './context'
import { UserPage } from './UserPage'

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

const applicationId = '5b0d1f52-8d0e-4a47-9c1b-2f7e5a9d4c01'

const roles: UserRoles = {
  displayName: 'Ann Invited',
  email: 'ann@axis.local',
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

/** Answers the roles by their address, a GET with `answer`, any POST with `change` and `/api/me` with `me`. */
function stubRoles(
  change: () => Response = () => new Response(null, { status: 204 }),
  answer: () => Response = () => json(roles),
  me: { studio: boolean } = { studio: false },
) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) =>
    input === '/api/me' ? json({ id: 'admin', ...me }) : init?.method === 'POST' ? change() : answer(),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function roleRow(page: HTMLElement, name: string) {
  return within(page)
    .getAllByTestId('user-role')
    .find((row) => within(row).queryByText(name) !== null)!
}

/** The confirmation dialog with this title. */
async function confirmation(title: string) {
  return (await screen.findByText(title)).closest<HTMLElement>('[role="dialog"]')!
}

function renderPage() {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={['/users/u1']}>
          <Routes>
            <Route path="users/:id" element={<UserPage />} />
          </Routes>
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

async function openAnn() {
  renderPage()
  return await screen.findByTestId('user-roles')
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('UserPage', () => {
  it('holds the name alone in the band', async () => {
    stubRoles()
    await openAnn()

    expectTitleAloneInBand('Ann Invited')
  })

  it('shows the name as title, the email and the roles, tags one that is not declared and lists the platform roles', async () => {
    const fetchMock = stubRoles()
    const page = await openAnn()

    expect(await within(page).findByText('Purchasing')).toBeVisible()
    const heading = within(screen.getByTestId('page-heading'))
    expect(heading.getByRole('heading', { level: 1, name: 'Ann Invited' })).toBeVisible()
    expect(heading.getByText('User')).toBeVisible()
    const email = within(within(page).getByTestId('user-email'))
    expect(email.getByText('Email', { selector: 'dt' })).toBeVisible()
    expect(email.getByText('ann@axis.local', { selector: 'dd' })).toBeVisible()
    expect(fetchMock.mock.calls.map(([input]) => input)).toContain('/api/users/u1/roles')
    expect(within(roleRow(page, 'buyer')).getByText('Given')).toBeVisible()
    expect(within(roleRow(page, 'buyer')).getByText('Given').closest('[data-tone]')).toHaveAttribute(
      'data-tone',
      'neutral',
    )
    expect(within(roleRow(page, 'buyer')).queryByText('Not declared')).toBeNull()
    expect(within(roleRow(page, 'dropped')).getByText('Given')).toBeVisible()
    expect(within(roleRow(page, 'dropped')).getByText('Not declared')).toBeVisible()
    expect(within(roleRow(page, 'dropped')).getByText('Not declared').closest('[data-tone]')).toHaveAttribute(
      'data-tone',
      'attention',
    )
    expect(within(roleRow(page, 'approver')).getByRole('button', { name: 'Give' })).toBeVisible()
    expect(within(page).getByText('Platform')).toBeVisible()
    expect(within(roleRow(page, 'administrator')).getByRole('button', { name: 'Give' })).toBeVisible()
  })

  it('shows each place where roles are given as a part named by its heading, with a rule between', async () => {
    stubRoles()
    const page = await openAnn()
    await within(page).findByText('Purchasing')

    for (const name of ['Purchasing', 'Platform']) {
      const region = within(page).getByRole('region', { name })
      expect(within(region).getByRole('heading', { level: 2, name })).toBeVisible()
    }
    expect(within(within(page).getByRole('region', { name: 'Purchasing' })).getByText('buyer')).toBeVisible()
    // The email is the first part, so a rule falls between it and each section.
    expect(within(page).getAllByRole('separator')).toHaveLength(2)
    expect(within(page).getByTestId('user-email')).toBeVisible()
  })

  it('offers the developer role and shows it on a person who holds it', async () => {
    stubRoles()
    const page = await openAnn()

    expect(await within(page).findByText('Purchasing')).toBeVisible()
    expect(within(roleRow(page, 'developer')).getByRole('button', { name: 'Give' })).toBeVisible()
  })

  it('shows Given and Remove on the developer role of a person who holds it', async () => {
    stubRoles(undefined, () =>
      json({ ...roles, given: [...roles.given, { applicationId: null, role: 'developer', declared: true }] }),
    )
    const page = await openAnn()

    expect(await within(page).findByText('Purchasing')).toBeVisible()
    expect(within(roleRow(page, 'developer')).getByText('Given')).toBeVisible()
    expect(within(roleRow(page, 'developer')).getByRole('button', { name: 'Remove' })).toBeVisible()
  })

  it('shows Not set for a person without an email', async () => {
    stubRoles(undefined, () => json({ ...roles, email: null }))
    const page = await openAnn()

    expect(await within(page).findByTestId('user-email')).toHaveTextContent('EmailNot set')
  })

  it('gives a role after a confirmation and loads the roles again', async () => {
    const fetchMock = stubRoles()
    const page = await openAnn()
    await within(page).findByText('Purchasing')

    await userEvent.click(within(roleRow(page, 'approver')).getByRole('button', { name: 'Give' }))
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

  it('finishes Remove in solid red and Give in the usual solid primary', async () => {
    stubRoles()
    const page = await openAnn()
    await within(page).findByText('Purchasing')

    await userEvent.click(within(roleRow(page, 'buyer')).getByRole('button', { name: 'Remove' }))
    const remove = await confirmation('Remove this role?')
    expect(within(remove).getByRole('button', { name: 'Remove' })).toHaveStyle({
      background: designTokensFor('light').colour.danger,
    })
    await userEvent.click(within(remove).getByRole('button', { name: 'Cancel' }))

    await userEvent.click(within(roleRow(page, 'approver')).getByRole('button', { name: 'Give' }))
    const give = await confirmation('Give this role?')
    expect(within(give).getByRole('button', { name: 'Give' }).style.background).toBe('')
  })

  it('shows that an administrator cannot remove their own administrator role on a 409', async () => {
    stubRoles(() => new Response(null, { status: 409 }))
    const page = await openAnn()
    await within(page).findByText('Purchasing')

    await userEvent.click(within(roleRow(page, 'buyer')).getByRole('button', { name: 'Remove' }))
    const dialog = await confirmation('Remove this role?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove' }))

    expect(await screen.findByTestId('user-roles-problem')).toHaveTextContent(
      'An administrator cannot remove their own administrator role.',
    )
  })

  it('asks for a new sign-in on a 403', async () => {
    stubRoles(() => new Response(null, { status: 403 }))
    const page = await openAnn()
    await within(page).findByText('Purchasing')

    await userEvent.click(within(roleRow(page, 'administrator')).getByRole('button', { name: 'Give' }))
    const dialog = await confirmation('Give this role?')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Give' }))

    expect(await screen.findByTestId('user-roles-problem')).toHaveTextContent(
      'Sign in again to change a platform role.',
    )
  })

  it('says the page is for administrators when the roles route answers 403', async () => {
    stubRoles(undefined, () => new Response(null, { status: 403 }))
    renderPage()

    expect(await screen.findByTestId('users-forbidden')).toHaveTextContent('This page is for administrators.')
    expect(screen.queryByTestId('user-roles')).toBeNull()
  })

  it('shows the not-found page when the roles route answers 404', async () => {
    stubRoles(undefined, () => new Response(null, { status: 404 }))
    renderPage()

    expect(await screen.findByTestId('not-found')).toBeVisible()
  })

  it("shows an active application's section title as plain text, with an Open in Studio button on its heading for a reader who may use Studio", async () => {
    stubRoles(undefined, undefined, { studio: true })
    const page = await openAnn()

    const section = within(await within(page).findByRole('region', { name: 'Purchasing' }))
    expect(section.getByRole('heading', { name: 'Purchasing' })).toBeVisible()
    expect(within(page).queryByRole('link', { name: 'Purchasing' })).not.toBeInTheDocument()
    // The button has an address, so it is a link element, which Ctrl+click opens in a new tab.
    expect(section.getByRole('link', { name: 'Open in Studio' })).toHaveAttribute('href', '/studio?app=Purchasing')
    // The platform's section has no application to open, so it has no button.
    const platform = within(page).getByRole('region', { name: 'Platform' })
    expect(within(platform).queryByRole('link', { name: 'Open in Studio' })).not.toBeInTheDocument()
    expect(within(page).getAllByRole('link', { name: 'Open in Studio' })).toHaveLength(1)
  })

  it('leaves the section titles plain text, with no Open in Studio button, for a reader who may not use Studio', async () => {
    const fetchMock = stubRoles(undefined, undefined, { studio: false })
    const page = await openAnn()

    expect(await within(page).findByRole('heading', { name: 'Purchasing' })).toBeVisible()
    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(within(page).queryByRole('link')).not.toBeInTheDocument()
    expect(within(page).queryByRole('link', { name: 'Open in Studio' })).not.toBeInTheDocument()
  })

  it('leaves the section of an application that is not active plain text, even for a reader who may use Studio', async () => {
    const gone = 'gone-application'
    stubRoles(undefined, () => json({ ...roles, given: [{ applicationId: gone, role: 'buyer', declared: false }] }), {
      studio: true,
    })
    const page = await openAnn()

    expect(await within(page).findByRole('heading', { name: gone })).toBeVisible()
    expect(within(page).queryByRole('link', { name: gone })).not.toBeInTheDocument()
    // The active application keeps its button, and the section of this one has none.
    expect(
      within(within(page).getByRole('region', { name: gone })).queryByRole('link', { name: 'Open in Studio' }),
    ).not.toBeInTheDocument()
  })
})
