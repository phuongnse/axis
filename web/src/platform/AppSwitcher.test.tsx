import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { AppSwitcher } from './AppSwitcher'
import type { CurrentUser } from './currentUser'
import type { SiteListItem } from './site'
import { TextProvider } from './TextProvider'

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
  users: false,
}
const author: CurrentUser = { ...requester, id: 'author', displayName: 'Author User', roles: ['author'], studio: true }
const olga: CurrentUser = { ...requester, id: 'olga', displayName: 'Olga Operator', roles: ['operator'], console: true }

const una: CurrentUser = {
  ...requester,
  id: 'una',
  displayName: 'Una Administrator',
  roles: ['tenant-administrator'],
  console: true,
  users: true,
}

const sites: SiteListItem[] = [
  {
    path: 'e2e',
    titleKey: 'site.title',
    titles: { en: 'E2E notes', vi: 'Ghi chú E2E' },
    application: { name: 'e2e', titles: { en: 'E2E notes', vi: 'Ghi chú E2E' } },
  },
  {
    path: 'purchasing',
    titleKey: 'site.title',
    titles: { en: 'Purchase requests' },
    application: { name: 'purchasing', titles: { en: 'Purchase requests' } },
  },
]

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs `GET /api/me` and `GET /api/sites`. `current` is `null` for nobody, who gets 401 from both. */
function stubServer(current: CurrentUser | null, listed: SiteListItem[] = sites) {
  const fetchMock = vi.fn(async (input: string) => {
    if (current === null) {
      return json({ title: 'Nobody is signed in.' }, 401)
    }
    if (input === '/api/me') {
      return json(current)
    }
    if (input === '/api/sites') {
      return json({ sites: listed })
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderSwitcher(locale = 'en', texts: Record<string, string> = platformEnglish) {
  render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <MemoryRouter>
        <AppSwitcher locale={locale} />
      </MemoryRouter>
    </TextProvider>,
  )
}

async function openSwitcher(name = 'Switch to') {
  await userEvent.click(await screen.findByRole('button', { name }))
  return within(await screen.findByRole('menu', { name }))
}

beforeEach(() => {
  vi.unstubAllGlobals()
})

describe('AppSwitcher', () => {
  it('shows no button when nobody is signed in', async () => {
    const fetchMock = stubServer(null)
    renderSwitcher()

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything()))
    await waitFor(() => expect(screen.queryByRole('button')).not.toBeInTheDocument())
    expect(fetchMock).not.toHaveBeenCalledWith('/api/sites', expect.anything())
  })

  it('lists the sites of an employee, with neither Studio nor the operator console', async () => {
    stubServer(requester)
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getByRole('link', { name: 'E2E notes' })).toHaveAttribute('href', '/e2e')
    expect(menu.getByRole('link', { name: 'Purchase requests' })).toHaveAttribute('href', '/purchasing')
    expect(menu.queryByRole('link', { name: 'Studio' })).not.toBeInTheDocument()
    expect(menu.queryByRole('link', { name: 'Operator console' })).not.toBeInTheDocument()
  })

  it('lists Studio after the sites for an author', async () => {
    stubServer(author)
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getByRole('link', { name: 'Studio' })).toHaveAttribute('href', '/studio')
    expect(menu.getByRole('link', { name: 'E2E notes' })).toBeInTheDocument()
    expect(menu.queryByRole('link', { name: 'Operator console' })).not.toBeInTheDocument()
    expect(menu.getAllByRole('menuitem').map((item) => item.textContent)).toEqual([
      'E2E notes',
      'Purchase requests',
      'Studio',
    ])
  })

  it('lists the operator console for a user with console access', async () => {
    stubServer(olga)
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getByRole('link', { name: 'Operator console' })).toHaveAttribute('href', '/console')
    expect(menu.queryByRole('link', { name: 'Studio' })).not.toBeInTheDocument()
  })

  it('lists Users after the operator console for an administrator, and only then', async () => {
    stubServer(una)
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getByRole('link', { name: 'Users' })).toHaveAttribute('href', '/users')
    expect(
      menu
        .getAllByRole('menuitem')
        .slice(-2)
        .map((item) => item.textContent),
    ).toEqual(['Operator console', 'Users'])
  })

  it('lists no Users link for a user who is not an administrator', async () => {
    stubServer(olga)
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getByRole('link', { name: 'Operator console' })).toBeInTheDocument()
    expect(menu.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument()
  })

  it('shows the site titles of the locale, and the first title when a site lacks it', async () => {
    stubServer(requester)
    renderSwitcher('vi', platformVietnamese)

    const menu = await openSwitcher('Chuyển đến')

    expect(menu.getByRole('link', { name: 'Ghi chú E2E' })).toHaveAttribute('href', '/e2e')
    expect(menu.getByRole('link', { name: 'Purchase requests' })).toBeInTheDocument()
  })

  it('lists only the tools when loading the sites fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer(author)
    fetchMock.mockImplementation(async (input: string) =>
      input === '/api/me' ? json(author) : new Response(null, { status: 500 }),
    )
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getAllByRole('menuitem').map((item) => item.textContent)).toEqual(['Studio'])
    expect(warn).toHaveBeenCalled()
    warn.mockRestore()
  })

  it('names the button in a tooltip on keyboard focus', async () => {
    stubServer(requester)
    renderSwitcher()

    await screen.findByRole('button', { name: 'Switch to' })
    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Switch to' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Switch to')
  })

  it('shows the sites under the heading Sites and the tools under the heading Tools', async () => {
    stubServer(author)
    renderSwitcher()

    const menu = await openSwitcher()
    const sites = menu.getByText('Sites')
    const tools = menu.getByText('Tools')

    expect(sites.compareDocumentPosition(menu.getByRole('link', { name: 'E2E notes' }))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )
    expect(tools.compareDocumentPosition(menu.getByRole('link', { name: 'Studio' }))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )
    expect(sites.compareDocumentPosition(tools) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('shows no Tools heading when the person has no tools', async () => {
    stubServer(requester)
    renderSwitcher()

    const menu = await openSwitcher()

    expect(menu.getByText('Sites')).toBeInTheDocument()
    expect(menu.queryByText('Tools')).not.toBeInTheDocument()
  })

  it('renders nothing when loading the user fails', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer(requester)
    fetchMock.mockImplementation(async () => new Response(null, { status: 500 }))
    renderSwitcher()

    await waitFor(() => expect(warn).toHaveBeenCalled())
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    warn.mockRestore()
  })
})
