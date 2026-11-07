import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { UserRoles } from '../platform/users'
import { TextProvider } from '../platform/TextProvider'
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
  platformRoles: ['author', 'publisher', 'operator', 'administrator'],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Answers the roles by their address, a GET with `answer`, and any POST with `change`. */
function stubRoles(
  change: () => Response = () => new Response(null, { status: 204 }),
  answer: () => Response = () => json(roles),
) {
  const fetchMock = vi.fn(async (_input: string, init?: RequestInit) => (init?.method === 'POST' ? change() : answer()))
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
  it('shows the name as title, the email and the roles, tags one that is not declared and lists the platform roles', async () => {
    const fetchMock = stubRoles()
    const page = await openAnn()

    expect(await within(page).findByText('Purchasing')).toBeVisible()
    expect(screen.getByRole('heading', { name: 'Ann Invited' })).toBeVisible()
    expect(within(page).getByTestId('user-email')).toHaveTextContent('Email: ann@axis.local')
    expect(fetchMock.mock.calls.map(([input]) => input)).toContain('/api/users/u1/roles')
    expect(within(roleRow(page, 'buyer')).getByText('Given')).toBeVisible()
    expect(within(roleRow(page, 'buyer')).queryByText('Not declared')).toBeNull()
    expect(within(roleRow(page, 'dropped')).getByText('Given')).toBeVisible()
    expect(within(roleRow(page, 'dropped')).getByText('Not declared')).toBeVisible()
    expect(within(roleRow(page, 'approver')).getByRole('button', { name: 'Give' })).toBeVisible()
    expect(within(page).getByText('Platform')).toBeVisible()
    expect(within(roleRow(page, 'administrator')).getByRole('button', { name: 'Give' })).toBeVisible()
  })

  it('shows Not set for a person without an email', async () => {
    stubRoles(undefined, () => json({ ...roles, email: null }))
    const page = await openAnn()

    expect(await within(page).findByTestId('user-email')).toHaveTextContent('Email: Not set')
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
})
