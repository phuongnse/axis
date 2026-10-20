import { render, screen, waitFor } from '@testing-library/react'
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

const anna: CurrentUser = {
  id: 'anna',
  displayName: 'Anna Employee',
  roles: ['employee'],
  studio: false,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
}
const hoa: CurrentUser = {
  id: 'hoa',
  displayName: 'Hoa Author',
  roles: ['author'],
  studio: true,
  studioPublisher: true,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
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

function renderMenu(texts: Record<string, string> = platformEnglish) {
  render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <MemoryRouter>
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
    stubServer(anna)
    renderMenu(texts)

    await userEvent.click(await screen.findByRole('button', { name: 'Anna Employee' }))

    expect(await screen.findByRole('menu', { name: texts['shell.account.label'] })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: texts['shell.account.signOut'] })).toBeInTheDocument()
  })

  it('has its texts in every platform locale', () => {
    for (const { texts } of locales) {
      expect(texts).toHaveProperty(['shell.account.label'])
      expect(texts).toHaveProperty(['shell.account.signIn'])
      expect(texts).toHaveProperty(['shell.account.signOut'])
      expect(texts).toHaveProperty(['shell.studio.link'])
    }
  })

  it.each(locales)('links to Studio for an author, with the platform $name text', async ({ texts }) => {
    stubServer(hoa)
    renderMenu(texts)

    const link = await screen.findByRole('link', { name: texts['shell.studio.link'] })

    expect(link).toHaveAttribute('href', '/studio')
    expect(screen.getByRole('button', { name: 'Hoa Author' })).toBeInTheDocument()
  })

  it('shows no Studio link to someone who may not use Studio', async () => {
    stubServer(anna)
    renderMenu()

    await screen.findByRole('button', { name: 'Anna Employee' })

    expect(screen.queryByRole('link', { name: 'Studio' })).not.toBeInTheDocument()
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
    const fetchMock = stubServer(anna)
    renderMenu()

    await userEvent.click(await screen.findByRole('button', { name: 'Anna Employee' }))
    await userEvent.click(await screen.findByRole('menuitem', { name: 'Sign out' }))

    await waitFor(() => expect(assignLocation).toHaveBeenCalledExactlyOnceWith(signOutUrl))
    const [, init] = fetchMock.mock.calls.find(([input]) => input === '/api/auth/sign-out')!
    expect(init?.method).toBe('POST')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
  })

  it('renders nothing when loading the user fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer(anna)
    fetchMock.mockImplementation(async () => new Response(null, { status: 500 }))
    renderMenu()

    await waitFor(() => expect(warn).toHaveBeenCalled())
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    warn.mockRestore()
  })
})
