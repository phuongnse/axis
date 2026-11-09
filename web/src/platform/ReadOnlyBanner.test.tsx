import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import type { CurrentUser } from './currentUser'
import { Shell } from './Shell'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const billingUrl = 'https://account.vn.example.test/tenants/quiz'
const navigation = [
  { key: 'home', path: '/', labelKey: 'nav.home' },
  { key: 'notes', path: '/e2e/notes', labelKey: 'nav.notes' },
]
const user: CurrentUser = {
  id: 'rita',
  displayName: 'Rita Reader',
  roles: ['reader'],
  studio: false,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  console: false,
  users: false,
  tenantStatus: 'readOnly',
  billingUrl: null,
  tenantExports: false,
}

/** Stubs `GET /api/me` with `current`. Every other request is a 404. */
function stubMe(current: CurrentUser) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) =>
      input === '/api/me'
        ? new Response(JSON.stringify(current), { status: 200, headers: { 'Content-Type': 'application/json' } })
        : new Response(null, { status: 404 }),
    ),
  )
}

function renderShell(path: string, locale = 'en', texts: Record<string, string> = platformEnglish) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <ThemeModeProvider>
        <TextProvider
          catalogs={[
            {
              texts: { ...texts, 'site.title': 'Workspace', 'nav.home': 'Home', 'nav.notes': 'Notes' },
              fallbackTexts: texts,
            },
          ]}
          development={false}
        >
          <Shell
            titleKey="site.title"
            navigation={navigation}
            locales={['en', 'vi']}
            locale={locale}
            onLocaleChange={() => {}}
          >
            <p>Page</p>
          </Shell>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.unstubAllGlobals()
})

describe('ReadOnlyBanner', () => {
  it.each(['/', '/e2e/notes'])('shows the banner with the billing link on %s for an administrator', async (path) => {
    stubMe({ ...user, users: true, billingUrl })
    renderShell(path)

    const banner = await screen.findByRole('alert')

    expect(banner).toHaveTextContent('This workspace is read-only.')
    expect(banner).toHaveTextContent('nothing can change or run until it is paid')
    expect(screen.getByRole('link', { name: 'Open billing' })).toHaveAttribute('href', billingUrl)
  })

  it.each(['/', '/e2e/notes'])(
    'shows the banner without a link on %s when there is no billing address',
    async (path) => {
      stubMe(user)
      renderShell(path)

      expect(await screen.findByRole('alert')).toHaveTextContent('This workspace is read-only.')
      expect(screen.queryByRole('link', { name: 'Open billing' })).not.toBeInTheDocument()
    },
  )

  it.each(['/', '/e2e/notes'])('shows no banner on %s while the tenant is active', async (path) => {
    const fetchMock = vi.fn(
      async () => new Response(JSON.stringify({ ...user, tenantStatus: 'active' }), { status: 200 }),
    )
    vi.stubGlobal('fetch', fetchMock)
    renderShell(path)

    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything()))
    await screen.findByText('Page')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('writes the banner in the locale', async () => {
    stubMe({ ...user, users: true, billingUrl })
    renderShell('/', 'vi', platformVietnamese)

    expect(await screen.findByRole('alert')).toHaveTextContent('Không gian làm việc này đang ở chế độ chỉ xem.')
    expect(screen.getByRole('link', { name: 'Mở trang thanh toán' })).toHaveAttribute('href', billingUrl)
  })
})
