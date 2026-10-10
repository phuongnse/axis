import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { TestUser } from './testUsers'
import { TestUserPicker } from './TestUserPicker'
import { TextProvider } from './TextProvider'

const catalogs = [
  {
    texts: {
      'shell.user.label': 'Test user',
      'shell.user.signIn': 'Sign in',
      'shell.user.signOut': 'Sign out',
    },
    fallbackTexts: {},
  },
]

const anna: TestUser = { id: 'anna', displayName: 'Anna Employee', roles: ['employee'] }
const binh: TestUser = { id: 'binh', displayName: 'Binh Engineering Manager', roles: ['department-manager'] }
const dung: TestUser = { id: 'dung', displayName: 'Dung Finance Reviewer', roles: ['finance-reviewer'] }

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Stubs `fetch` with the test user endpoints. `current` is the signed-in user, or `null` for nobody. */
function stubServer({ current = null as TestUser | null, listStatus = 200 } = {}) {
  const fetchMock = vi.fn(async (input: string, _init?: RequestInit) => {
    if (input === '/api/test-users') {
      return listStatus === 200 ? json({ users: [anna, binh, dung] }) : json({ title: 'Not found' }, listStatus)
    }
    if (input === '/api/me') {
      return current === null ? json({ title: 'Nobody is signed in.' }, 401) : json(current)
    }
    if (input === '/api/test-users/sign-in') {
      return json(anna)
    }
    return new Response(null, { status: 204 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderPicker(reload = vi.fn()) {
  render(
    <TextProvider catalogs={catalogs} development={false}>
      <TestUserPicker reload={reload} />
    </TextProvider>,
  )
  return reload
}

describe('TestUserPicker', () => {
  it('lists every test user and marks only the signed-in one', async () => {
    stubServer({ current: binh })
    renderPicker()

    await userEvent.click(await screen.findByRole('button', { name: 'Binh Engineering Manager' }))

    const menu = await screen.findByRole('menu', { name: 'Test user' })
    expect(menu).toBeInTheDocument()
    const items = screen.getAllByRole('menuitemradio')
    expect(items.map((item) => item.textContent)).toEqual([anna.displayName, binh.displayName, dung.displayName])
    expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['false', 'true', 'false'])
    expect(screen.getByRole('menuitem', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('shows a sign-in prompt and no sign out when nobody is signed in', async () => {
    stubServer()
    renderPicker()

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    await screen.findByRole('menu', { name: 'Test user' })
    expect(screen.getAllByRole('menuitemradio')).toHaveLength(3)
    expect(screen.getAllByRole('menuitemradio').every((item) => item.getAttribute('aria-checked') === 'false')).toBe(
      true,
    )
    expect(screen.queryByRole('menuitem', { name: 'Sign out' })).not.toBeInTheDocument()
  })

  it('renders nothing when the server does not offer the test user list', async () => {
    const fetchMock = stubServer({ current: binh, listStatus: 404 })
    renderPicker()

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    // Let the requests settle, so a late render would show.
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('signs in as the picked user with a JSON body and reloads', async () => {
    const fetchMock = stubServer()
    const reload = renderPicker()

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await userEvent.click(await screen.findByRole('menuitemradio', { name: 'Anna Employee' }))

    await waitFor(() => expect(reload).toHaveBeenCalledOnce())
    const [, init] = fetchMock.mock.calls.find(([input]) => input === '/api/test-users/sign-in')!
    expect(init?.method).toBe('POST')
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' })
    expect(init?.body).toBe('{"id":"anna"}')
  })

  it('signs out with a JSON body and reloads', async () => {
    const fetchMock = stubServer({ current: binh })
    const reload = renderPicker()

    await userEvent.click(await screen.findByRole('button', { name: 'Binh Engineering Manager' }))
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Sign out' }))

    await waitFor(() => expect(reload).toHaveBeenCalledOnce())
    const [, init] = fetchMock.mock.calls.find(([input]) => input === '/api/test-users/sign-out')!
    expect(init?.method).toBe('POST')
    expect(init?.headers).toEqual({ 'Content-Type': 'application/json' })
    expect(init?.body).toBe('{}')
  })
})
