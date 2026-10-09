import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { App } from './App'
import type { ApplicationSite, EntityMetadata, PageMetadata, SiteListItem, SiteMetadata } from './platform/site'
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
    'shell.locale.loadFailed': 'The texts for this language could not be loaded.',
    'shell.home.sites.title': 'Sites',
    'shell.home.sites.empty': 'No application is active.',
    'shell.home.sites.loadFailed': 'The sites could not be loaded.',
    'shell.notFound.title': 'Page not found',
    'shell.notFound.message': 'There is nothing at this address.',
    'shell.notFound.home': 'Go to the home page',
    'shell.form.save': 'Save',
    'shell.form.cancel': 'Cancel',
    'shell.table.create': 'New',
    'shell.table.empty': 'No records yet.',
  },
  vi: {
    'shell.nav.home': 'Trang chủ',
    'shell.home.title': 'Chào mừng đến với Axis',
    'shell.locale.label': 'Ngôn ngữ',
    'shell.locale.en': 'English',
    'shell.locale.vi': 'Tiếng Việt',
  },
}

const sites: SiteListItem[] = [{ path: 'e2e', titleKey: 'site.title', titles: { en: 'E2E notes', vi: 'Ghi chú E2E' } }]

const e2eSite: ApplicationSite = {
  path: 'e2e',
  titleKey: 'site.title',
  locales: { default: 'en', fallback: 'en', available: ['en', 'vi'] },
  navigation: [
    { page: 'Notes', labelKey: 'nav.notes' },
    { page: 'Categories', labelKey: 'nav.categories' },
  ],
}

// The site texts carry their own 'shell.theme.dark', which wins over the platform text inside the site.
const siteTexts: Record<string, Record<string, string>> = {
  en: {
    'site.title': 'E2E notes',
    'nav.notes': 'Notes',
    'nav.categories': 'Categories',
    'pages.notes.title': 'All notes',
    'pages.noteForm.title': 'Note',
    'shell.theme.dark': 'Night mode',
  },
  vi: {
    'site.title': 'Ghi chú E2E',
    'nav.notes': 'Ghi chú',
    'nav.categories': 'Danh mục',
    'pages.notes.title': 'Tất cả ghi chú',
    'pages.noteForm.title': 'Ghi chú',
    'shell.theme.dark': 'Chế độ đêm',
  },
}

const noteEntity: EntityMetadata = {
  name: 'Note',
  labelKey: null,
  displayField: null,
  recordsPath: '/api/apps/E2eApp/entities/Note/records',
  fields: [
    {
      name: 'title',
      type: 'text',
      labelKey: null,
      required: true,
      unique: false,
      computed: false,
      maxLength: 200,
      precision: null,
      scale: null,
      values: null,
      target: null,
      fields: null,
    },
  ],
}

const pages: Record<string, PageMetadata> = {
  notes: {
    name: 'Notes',
    titleKey: 'pages.notes.title',
    widgets: [{ type: 'table', formPage: 'NoteForm', entity: noteEntity }],
  },
  noteform: {
    name: 'NoteForm',
    titleKey: 'pages.noteForm.title',
    widgets: [{ type: 'form', formPage: null, entity: noteEntity }],
  },
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Stubs `fetch` with the server's routes. */
function stubServer({ ready = true, siteStatus = 200, sitesStatus = 200, failingLocale = '' } = {}) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input === '/health/ready') {
      return json({}, ready ? 200 : 503)
    }
    if (input === '/api/site') {
      return json(siteStatus === 200 ? site : {}, siteStatus)
    }
    if (input === '/api/sites') {
      return sitesStatus === 200 ? json({ sites }) : json({}, sitesStatus)
    }
    if (input.startsWith('/api/sites/')) {
      return siteRoute(input.replace('/api/sites/', ''), failingLocale)
    }
    if (input.startsWith('/api/apps/')) {
      // A list of records has no id segment. No record exists, so a record by id is not found.
      return input.startsWith(`${noteEntity.recordsPath}/`)
        ? json({ title: 'Not found', status: 404 }, 404)
        : json({ items: [], page: 1, pageSize: 20, totalCount: 0 })
    }
    const locale = input.replace('/api/texts/', '')
    if (locale === failingLocale) {
      return json({}, 500)
    }
    return locale in texts ? json({ locale, texts: texts[locale] }) : json({}, 404)
  })
  vi.stubGlobal('fetch', fetchMock)
}

/** Answers the application site endpoints for the site `e2e`, and 404 for anything else. */
function siteRoute(path: string, failingLocale: string) {
  const notFound = json({ title: 'Not found', status: 404 }, 404)
  const [sitePath, kind, name, ...rest] = path.split('/')
  if (sitePath !== 'e2e' || rest.length > 0) {
    return notFound
  }
  if (kind === undefined) {
    return json(e2eSite)
  }
  if (kind === 'texts' && name !== undefined && name in siteTexts) {
    return name === failingLocale ? json({}, 500) : json({ locale: name, texts: siteTexts[name] })
  }
  if (kind === 'pages' && name !== undefined && name.toLowerCase() in pages) {
    return json(pages[name.toLowerCase()])
  }
  return notFound
}

function CurrentPath() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

function renderApp(path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ThemeModeProvider>
        <App development={false} />
      </ThemeModeProvider>
      <CurrentPath />
    </MemoryRouter>,
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

  it('shows an error and keeps the current locale when the chosen texts cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer({ failingLocale: 'vi' })
    renderApp()
    await screen.findByText('Welcome to Axis')

    const language = screen.getByRole('radiogroup', { name: 'Language' })
    await userEvent.click(within(language).getByText('Tiếng Việt'))

    expect(await screen.findByTestId('locale-error')).toHaveTextContent(
      'The texts for this language could not be loaded.',
    )
    expect(screen.getByText('Welcome to Axis')).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'English' })).toBeChecked()
    expect(localStorage.getItem('axis.locale')).toBeNull()
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

  it('lists the sites of the tenant on the home page', async () => {
    stubServer()

    renderApp()

    await screen.findByRole('link', { name: 'E2E notes' })
    // Spin keeps the list blurred, with pointer-events: none, for one commit after loading ends.
    const link = await waitFor(() => {
      const found = screen.getByRole('link', { name: 'E2E notes' })
      expect(found.closest('.ant-spin-blur')).toBeNull()
      return found
    })
    expect(link).toHaveAttribute('href', '/e2e')
    await userEvent.click(link)
    expect(await screen.findByText('All notes')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/notes')
  })

  it('keeps the sites card and shows an error when the sites cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer({ sitesStatus: 500 })

    renderApp()

    const error = await screen.findByTestId('sites-error')
    expect(error).toHaveTextContent('The sites could not be loaded.')
    expect(screen.getByText('Sites')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'E2E notes' })).not.toBeInTheDocument()
  })

  it('shows site titles in the current locale', async () => {
    localStorage.setItem('axis.locale', 'vi')
    stubServer()

    renderApp()

    expect(await screen.findByRole('link', { name: 'Ghi chú E2E' })).toBeInTheDocument()
  })

  it('shows a page title inside the site shell with the site navigation', async () => {
    stubServer()

    renderApp('/e2e/notes')

    expect(await screen.findByText('All notes')).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Notes' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Categories' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Home' })).not.toBeInTheDocument()
    expect(screen.getByText('E2E notes')).toBeInTheDocument()
  })

  it('resolves a key both catalogs have to the site text, and other keys to the platform text', async () => {
    stubServer()

    renderApp('/e2e/notes')

    expect(await screen.findByRole('switch', { name: 'Night mode' })).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Language' })).toBeInTheDocument()
  })

  it('switches the locale inside a site and remembers the choice', async () => {
    stubServer()
    renderApp('/e2e/notes')
    await screen.findByText('All notes')

    const language = screen.getByRole('radiogroup', { name: 'Language' })
    await userEvent.click(within(language).getByText('Tiếng Việt'))

    expect(await screen.findByText('Tất cả ghi chú')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/api/sites/e2e/texts/vi', expect.anything())
    expect(screen.getByRole('menuitem', { name: 'Danh mục' })).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'Chế độ đêm' })).toBeInTheDocument()
    expect(screen.getByRole('radiogroup', { name: 'Ngôn ngữ' })).toBeInTheDocument()
    expect(localStorage.getItem('axis.locale')).toBe('vi')
  })

  it('keeps the site locale and shows an error when the chosen site texts cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer({ failingLocale: 'vi' })
    renderApp('/e2e/notes')
    await screen.findByText('All notes')

    const language = screen.getByRole('radiogroup', { name: 'Language' })
    await userEvent.click(within(language).getByText('Tiếng Việt'))

    expect(await screen.findByTestId('locale-error')).toBeInTheDocument()
    expect(screen.getByText('All notes')).toBeInTheDocument()
    expect(localStorage.getItem('axis.locale')).toBeNull()
  })

  it('opens the first navigation entry of a site', async () => {
    stubServer()

    renderApp('/e2e')

    expect(await screen.findByText('All notes')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/notes')
  })

  it('shows the records of a table page', async () => {
    stubServer()

    renderApp('/e2e/notes')

    expect(await screen.findByText('No records yet.')).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'title' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'New' })).toHaveAttribute('href', '/e2e/noteform/new')
    expect(fetch).toHaveBeenCalledWith('/api/apps/E2eApp/entities/Note/records', expect.anything())
  })

  it('shows the form of a form page to create a record', async () => {
    stubServer()

    renderApp('/e2e/noteform/new')

    expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'title' })).toBeInTheDocument()
    expect(screen.getByText('Note')).toBeInTheDocument()
  })

  it('shows the not-found page for a record id the form page has no record for, or that is not an id', async () => {
    stubServer()

    for (const path of ['/e2e/noteform/6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7', '/e2e/noteform/42']) {
      const { unmount } = renderApp(path)

      expect(await screen.findByTestId('not-found')).toBeInTheDocument()
      unmount()
    }
    expect(fetch).toHaveBeenCalledWith(
      `${noteEntity.recordsPath}/6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7`,
      expect.anything(),
    )
    expect(fetch).not.toHaveBeenCalledWith(`${noteEntity.recordsPath}/42`, expect.anything())
  })

  it('shows the not-found page in the platform shell for an unknown site', async () => {
    stubServer()

    renderApp('/no-such-site')

    expect(await screen.findByTestId('not-found')).toHaveTextContent('Page not found')
    expect(screen.getByRole('menuitem', { name: 'Home' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toHaveAttribute('href', '/')
  })

  it('shows the not-found page in the site shell for an unknown page, a bare form page and form routes of a table', async () => {
    stubServer()

    for (const path of ['/e2e/no-such-page', '/e2e/noteform', '/e2e/notes/new', '/e2e/notes/42']) {
      const { unmount } = renderApp(path)

      expect(await screen.findByTestId('not-found')).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Notes' })).toBeInTheDocument()
      unmount()
    }
  })

  it('shows the not-found page in the platform shell for a deeper unknown address', async () => {
    stubServer()

    renderApp('/e2e/notes/42/edit')

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Home' })).toBeInTheDocument()
  })
})
