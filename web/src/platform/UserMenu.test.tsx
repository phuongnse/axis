import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import type { CurrentUser } from './currentUser'
import { assignLocation } from './navigation'
import { TextProvider } from './TextProvider'
import { UserMenu } from './UserMenu'

vi.mock('./navigation')

const requester: CurrentUser = {
  id: 'requester',
  displayName: 'Requester User',
  roles: ['employee'],
  studio: false,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  console: false,
}
const author: CurrentUser = {
  id: 'author',
  displayName: 'Author User',
  roles: ['author'],
  studio: true,
  studioPublisher: true,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  console: false,
}
const signOutUrl = 'http://127.0.0.1:8080/realms/axis/protocol/openid-connect/logout?client_id=axis'

const locales = [
  { name: 'en', texts: platformEnglish },
  { name: 'vi', texts: platformVietnamese },
]

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Stubs `fetch` with `GET /api/me` and `POST /api/auth/sign-out`. `current` is `null` for nobody. */
function stubServer(current: CurrentUser | null) {
  const fetchMock = vi.fn(async (input: string, _init?: RequestInit) => {
    if (input === '/api/me') {
      return current === null ? json({ title: 'Nobody is signed in.' }, 401) : json(current)
    }
    if (input === '/api/auth/sign-out') {
      return json({ signOutUrl })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderMenu(texts: Record<string, string> = platformEnglish, path = '/e2e/notes') {
  render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <MemoryRouter initialEntries={[path]}>
        <UserMenu />
      </MemoryRouter>
    </TextProvider>,
  )
}

beforeEach(() => {
  vi.mocked(assignLocation).mockReset()
  history.pushState(null, '', '/e2e/notes?pageSize=100')
})

describe('UserMenu', () => {
  it.each(locales)(
    'shows the sign-in button from the platform $name texts when nobody is signed in',
    async ({ texts }) => {
      stubServer(null)
      renderMenu(texts)

      expect(await screen.findByRole('button', { name: texts['shell.account.signIn'] })).toBeInTheDocument()
    },
  )

  it.each(locales)('shows the name with a sign-out menu from the platform $name texts', async ({ texts }) => {
    stubServer(requester)
    renderMenu(texts)

    await userEvent.click(await screen.findByRole('button', { name: 'Requester User' }))

    expect(await screen.findByRole('menu', { name: texts['shell.account.label'] })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: texts['shell.account.signOut'] })).toBeInTheDocument()
  })

  it.each(locales)(
    'has a Sessions entry that links to the account screen, with the platform $name text',
    async ({ texts }) => {
      stubServer(requester)
      renderMenu(texts)

      await userEvent.click(await screen.findByRole('button', { name: 'Requester User' }))

      const link = within(await screen.findByRole('menu', { name: texts['shell.account.label'] })).getByRole('link', {
        name: texts['shell.account.sessions'],
      })
      expect(link).toHaveAttribute('href', '/account')
    },
  )

  it('has its texts in every platform locale', () => {
    for (const { texts } of locales) {
      expect(texts).toHaveProperty(['shell.account.label'])
      expect(texts).toHaveProperty(['shell.account.signIn'])
      expect(texts).toHaveProperty(['shell.account.signOut'])
      expect(texts).toHaveProperty(['shell.account.sessions'])
    }
  })

  it('shows no Studio link beside the name of an author', async () => {
    stubServer(author)
    renderMenu()

    expect(await screen.findByRole('button', { name: 'Author User' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Studio' })).not.toBeInTheDocument()
  })

  it('shows no sign-in button on the home page, which is the sign-in screen, when nobody is signed in', async () => {
    const fetchMock = stubServer(null)
    renderMenu(platformEnglish, '/')

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything()))
    // Let the answer settle, so the absence is not just the loading state.
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20))
    })
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows the name of a signed-in person on the home page', async () => {
    stubServer(requester)
    renderMenu(platformEnglish, '/')

    expect(await screen.findByRole('button', { name: 'Requester User' })).toBeInTheDocument()
  })

  it('sends the browser to sign in and back to the current page', async () => {
    stubServer(null)
    renderMenu()

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(assignLocation).toHaveBeenCalledExactlyOnceWith(
      '/api/auth/sign-in?returnUrl=%2Fe2e%2Fnotes%3FpageSize%3D100',
    )
  })

  it('ends the session with the CSRF header, then goes to the sign-out address', async () => {
    const fetchMock = stubServer(requester)
    renderMenu()

    await userEvent.click(await screen.findByRole('button', { name: 'Requester User' }))
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Sign out' }))

    await waitFor(() => expect(assignLocation).toHaveBeenCalledExactlyOnceWith(signOutUrl))
    const [, init] = fetchMock.mock.calls.find(([input]) => input === '/api/auth/sign-out')!
    expect(init?.method).toBe('POST')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
  })

  it('renders nothing when loading the user fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer(requester)
    fetchMock.mockImplementation(async () => new Response(null, { status: 500 }))
    renderMenu()

    await waitFor(() => expect(warn).toHaveBeenCalled())
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    warn.mockRestore()
  })
})
