import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import type { SiteMetadata } from './platform/site'
import { ThemeModeProvider } from './platform/theme'

const site: SiteMetadata = {
  name: 'platform',
  titleKey: 'shell.title',
  locales: { default: 'en', fallback: 'en', available: ['en', 'vi'] },
  navigation: [{ key: 'home', path: '/', labelKey: 'shell.nav.home' }],
}

const texts: Record<string, Record<string, string>> = {
  en: {
    'shell.title': 'Axis',
    'shell.nav.home': 'Home',
    'shell.home.title': 'Welcome to Axis',
    'shell.serverStatus.title': 'Server status',
    'shell.serverStatus.checking': 'Checking',
    'shell.serverStatus.ready': 'Ready',
    'shell.serverStatus.unavailable': 'Unavailable',
    'shell.theme.dark': 'Dark mode',
    'shell.theme.darkShort': 'Dark',
    'shell.theme.lightShort': 'Light',
    'shell.locale.label': 'Language',
    'shell.locale.en': 'English',
    'shell.locale.vi': 'Tiếng Việt',
  },
  vi: {
    'shell.nav.home': 'Trang chủ',
    'shell.home.title': 'Chào mừng đến với Axis',
    'shell.locale.label': 'Ngôn ngữ',
    'shell.locale.en': 'English',
    'shell.locale.vi': 'Tiếng Việt',
  },
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Stubs `fetch` with the server's routes. */
function stubServer({ ready = true, siteStatus = 200 } = {}) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input === '/health/ready') {
      return json({}, ready ? 200 : 503)
    }
    if (input === '/api/site') {
      return json(siteStatus === 200 ? site : {}, siteStatus)
    }
    const locale = input.replace('/api/texts/', '')
    return locale in texts ? json({ locale, texts: texts[locale] }) : json({}, 404)
  })
  vi.stubGlobal('fetch', fetchMock)
}

function renderApp() {
  return render(
    <ThemeModeProvider>
      <App development={false} />
    </ThemeModeProvider>,
  )
}

async function findShellRoot(mode: 'light' | 'dark') {
  const heading = await screen.findByText('Welcome to Axis')
  const root = heading.closest<HTMLElement>(`[data-theme-mode="${mode}"]`)
  expect(root).not.toBeNull()
  return root!
}

describe('App', () => {
  it('shows the server as ready when the readiness check succeeds', async () => {
    stubServer()

    renderApp()

    expect(await screen.findByText('Ready')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/health/ready', expect.anything())
  })

  it('shows the server as unavailable when the readiness check fails', async () => {
    stubServer({ ready: false })

    renderApp()

    expect(await screen.findByText('Unavailable')).toBeInTheDocument()
  })

  it('switches between light and dark mode and remembers the choice', async () => {
    stubServer()
    renderApp()
    const toggle = await screen.findByRole('switch', { name: 'Dark mode' })
    expect(toggle).not.toBeChecked()

    await userEvent.click(toggle)

    expect(toggle).toBeChecked()
    expect(localStorage.getItem('axis.themeMode')).toBe('dark')
  })

  it('renders the navigation and the page content in both themes', async () => {
    stubServer()
    renderApp()

    const light = await findShellRoot('light')
    expect(within(light).getByRole('menuitem', { name: 'Home' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('switch', { name: 'Dark mode' }))

    const dark = await findShellRoot('dark')
    expect(within(dark).getByRole('menuitem', { name: 'Home' })).toBeInTheDocument()
    expect(within(dark).getByText('Welcome to Axis')).toBeInTheDocument()
  })

  it('switches the locale and remembers the choice', async () => {
    stubServer()
    renderApp()
    await screen.findByText('Welcome to Axis')

    // The radio input itself takes no pointer events: people click its label.
    const language = screen.getByRole('radiogroup', { name: 'Language' })
    await userEvent.click(within(language).getByText('Tiếng Việt'))

    expect(await screen.findByText('Chào mừng đến với Axis')).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Trang chủ' })).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Ngôn ngữ' })).toBeInTheDocument()
    expect(localStorage.getItem('axis.locale')).toBe('vi')
  })

  it('starts in the stored locale', async () => {
    localStorage.setItem('axis.locale', 'vi')
    stubServer()

    renderApp()

    expect(await screen.findByText('Chào mừng đến với Axis')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Tiếng Việt' })).toBeChecked()
  })

  it('ignores a stored locale the site does not offer', async () => {
    localStorage.setItem('axis.locale', 'xx')
    stubServer()

    renderApp()

    expect(await screen.findByText('Welcome to Axis')).toBeInTheDocument()
    expect(fetch).not.toHaveBeenCalledWith('/api/texts/xx', expect.anything())
  })

  it('shows an error when the site cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer({ siteStatus: 500 })

    renderApp()

    expect(await screen.findByTestId('shell-error')).toHaveTextContent('Could not load the site.')
  })
})
