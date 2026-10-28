import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../src/Axis.Presentation/Texts/vi.json'
import { App } from './App'
import { apiFetch, signInUrl } from './platform/api'
import { assignLocation } from './platform/navigation'
import type {
  ApplicationSite,
  EntityMetadata,
  PageMetadata,
  SiteListItem,
  SiteMetadata,
  WidgetAccess,
} from './platform/site'
import { ThemeModeProvider } from './platform/theme'
import { isBlurred } from './test/antd'

vi.mock('./platform/navigation')

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
    'shell.home.sites.signIn': 'Sign in to see the sites.',
    'shell.home.signInFailed': 'Sign-in did not complete. Try again.',
    'shell.account.signIn': 'Sign in',
    'shell.signInPrompt.message': 'Sign in to continue.',
    'shell.notFound.title': 'Page not found',
    'shell.notFound.message': 'There is nothing at this address.',
    'shell.notFound.home': 'Go to the home page',
    'shell.form.save': 'Save',
    'shell.form.cancel': 'Cancel',
    'shell.table.empty': 'No records yet.',
  },
  vi: {
    'shell.nav.home': 'Trang chủ',
    'shell.home.title': 'Chào mừng đến với Axis',
    'shell.serverStatus.title': 'Trạng thái máy chủ',
    'shell.locale.label': 'Ngôn ngữ',
    'shell.locale.en': 'English',
    'shell.locale.vi': 'Tiếng Việt',
  },
}

const sites: SiteListItem[] = [
  {
    path: 'e2e',
    titleKey: 'site.title',
    titles: { en: 'E2E notes', vi: 'Ghi chú E2E' },
    application: { name: 'E2eNotes', titles: { en: 'E2E notes', vi: 'Ghi chú E2E' } },
  },
]

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
    'notes.new': 'New',
    'pages.nestedForm.section': 'Nested note',
    'shell.theme.dark': 'Night mode',
  },
  vi: {
    'site.title': 'Ghi chú E2E',
    'nav.notes': 'Ghi chú',
    'nav.categories': 'Danh mục',
    'pages.notes.title': 'Tất cả ghi chú',
    'pages.noteForm.title': 'Ghi chú',
    'notes.new': 'Tạo mới',
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
      sequence: false,
      creator: false,
      maxLength: 200,
      precision: null,
      scale: null,
      min: null,
      max: null,
      minLength: null,
      values: null,
      target: null,
      fields: null,
    },
  ],
}

const tasksPath = '/api/apps/E2eApp/tasks'
const instancesPath = '/api/apps/E2eApp/processes/NoteReview/instances'
const deadLettersPath = '/api/apps/E2eApp/processes/NoteReview/dead-letters'

const noteFields = noteEntity.fields.map((field) => field.name)
const noteAccess: WidgetAccess = {
  actions: ['read', 'create', 'update', 'delete'],
  createFields: noteFields,
  updateFields: noteFields,
}

const pages: Record<string, PageMetadata> = {
  notes: {
    name: 'Notes',
    titleKey: 'pages.notes.title',
    parameters: [],
    widgets: [
      {
        type: 'table',
        entity: noteEntity,
        dataSource: null,
        form: null,
        tasksPath: null,
        process: null,
        instancesPath: null,
        deadLettersPath: null,
        actions: [],
        rowActions: [],
        toolbarActions: [
          {
            type: 'navigate',
            page: 'NoteForm',
            labelKey: 'notes.new',
            parameters: {},
            record: null,
            opensForm: true,
            paths: {},
            visibleWhen: null,
          },
        ],
        parameters: {},
        access: noteAccess,
        detail: null,
        kpi: null,
        chart: null,
      },
    ],
  },
  noteform: {
    name: 'NoteForm',
    titleKey: 'pages.noteForm.title',
    parameters: [],
    widgets: [
      {
        type: 'form',
        entity: noteEntity,
        dataSource: null,
        form: null,
        tasksPath: null,
        process: null,
        instancesPath: null,
        deadLettersPath: null,
        actions: [],
        rowActions: [],
        toolbarActions: [],
        parameters: {},
        access: noteAccess,
        detail: null,
        kpi: null,
        chart: null,
      },
    ],
  },
  notedetail: {
    name: 'NoteDetail',
    titleKey: 'pages.noteForm.title',
    parameters: [
      {
        name: 'note',
        type: 'reference',
        required: true,
        values: null,
        target: { entity: 'Note', displayField: 'title', recordsPath: noteEntity.recordsPath },
      },
    ],
    widgets: [
      {
        type: 'detail',
        entity: noteEntity,
        dataSource: null,
        form: null,
        tasksPath: null,
        process: null,
        instancesPath: null,
        deadLettersPath: null,
        actions: [],
        rowActions: [],
        toolbarActions: [],
        parameters: {},
        access: noteAccess,
        detail: { record: 'note' },
        kpi: null,
        chart: null,
      },
    ],
  },
  tasks: {
    name: 'Tasks',
    titleKey: 'pages.tasks.title',
    parameters: [],
    widgets: [
      {
        type: 'taskInbox',
        entity: null,
        dataSource: null,
        form: null,
        tasksPath,
        process: null,
        instancesPath: null,
        deadLettersPath: null,
        actions: [],
        rowActions: [],
        toolbarActions: [],
        parameters: {},
        access: null,
        detail: null,
        kpi: null,
        chart: null,
      },
    ],
  },
  inspector: {
    name: 'Inspector',
    titleKey: 'pages.inspector.title',
    parameters: [],
    widgets: [
      {
        type: 'processInspector',
        entity: null,
        dataSource: null,
        form: null,
        tasksPath: null,
        process: 'NoteReview',
        instancesPath,
        deadLettersPath,
        actions: [],
        rowActions: [],
        toolbarActions: [],
        parameters: {},
        access: null,
        detail: null,
        kpi: null,
        chart: null,
      },
    ],
  },
}

// A form nested in a section: the page's route checks find it anywhere in the tree.
pages.nestedform = {
  name: 'NestedForm',
  titleKey: 'pages.noteForm.title',
  parameters: [],
  widgets: [
    { type: 'section', titleKey: 'pages.nestedForm.section', collapsed: false, widgets: pages.noteform.widgets },
  ],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Stubs `fetch` with the server's routes. */
function stubServer({
  ready = true,
  siteStatus = 200,
  sitesStatus = 200,
  failingLocale = '',
  e2eSiteStatus = 200,
  recordsStatus = 200,
} = {}) {
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
    if (input === '/api/sites/e2e' && e2eSiteStatus !== 200) {
      return json({ title: 'Sign in to read or write this data.', status: e2eSiteStatus }, e2eSiteStatus)
    }
    if (input.startsWith('/api/sites/')) {
      return siteRoute(input.replace('/api/sites/', ''), failingLocale)
    }
    if (input.startsWith('/api/apps/')) {
      if (recordsStatus !== 200) {
        return json({ title: 'Sign in to read or write this data.', status: recordsStatus }, recordsStatus)
      }
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

    expect(await within(await screen.findByTestId('server-status')).findByText('Ready')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith('/health/ready', expect.anything())
  })

  it('shows the server as unavailable when the readiness check fails', async () => {
    stubServer({ ready: false })

    renderApp()

    expect(await within(await screen.findByTestId('server-status')).findByText('Unavailable')).toBeInTheDocument()
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
    expect(within(light).getByRole('link', { name: 'Axis' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('switch', { name: 'Dark mode' }))

    const dark = await findShellRoot('dark')
    expect(within(dark).getByRole('link', { name: 'Axis' })).toBeInTheDocument()
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
    expect(within(screen.getByRole('contentinfo')).getByText('Trạng thái máy chủ')).toBeInTheDocument()
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
      expect(isBlurred(found)).toBe(false)
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

  it('shows a sign-in prompt in place of the sites without starting sign-in when nobody is signed in', async () => {
    vi.mocked(assignLocation).mockReset()
    stubServer({ sitesStatus: 401 })

    renderApp()

    expect(await screen.findByTestId('sites-sign-in')).toHaveTextContent('Sign in to see the sites.')
    expect(screen.queryByTestId('sign-in-failed')).not.toBeInTheDocument()
    expect(screen.getByText('Sites')).toBeInTheDocument()
    expect(screen.queryByTestId('sites-error')).not.toBeInTheDocument()
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('shows a sign-in-did-not-complete alert above the sign-in prompt when the address has the marker', async () => {
    stubServer({ sitesStatus: 401 })

    renderApp('/?signIn=failed')

    const alert = await screen.findByTestId('sign-in-failed')
    expect(alert).toHaveTextContent('Sign-in did not complete. Try again.')
    expect(alert).toHaveAttribute('role', 'alert')
    expect(await screen.findByTestId('sites-sign-in')).toBeInTheDocument()
  })

  it('has the sign-in-did-not-complete text in every platform locale', () => {
    for (const texts of [platformEnglish, platformVietnamese]) {
      expect(texts).toHaveProperty(['shell.home.signInFailed'])
    }
  })

  it('has the sign-in prompt text in every platform locale', () => {
    for (const texts of [platformEnglish, platformVietnamese]) {
      expect(texts).toHaveProperty(['shell.signInPrompt.message'])
    }
  })

  it('shows the sign-in prompt, and does not start sign-in, when a site page opens and nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    stubServer({ e2eSiteStatus: 401 })

    renderApp('/e2e/notes?x=1')

    const prompt = await screen.findByTestId('sign-in-prompt')
    expect(prompt).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(within(prompt).getByText('Sign in to continue.')).toBeInTheDocument()
    expect(screen.queryByTestId('shell-error')).not.toBeInTheDocument()
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('starts sign-in with the current path and query string when the prompt Sign in is clicked', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', '/e2e/notes?x=1')
    stubServer({ e2eSiteStatus: 401 })

    renderApp('/e2e/notes?x=1')

    const prompt = await screen.findByTestId('sign-in-prompt')
    expect(assignLocation).not.toHaveBeenCalled()
    await userEvent.click(within(prompt).getByRole('button', { name: 'Sign in' }))

    expect(assignLocation).toHaveBeenCalledExactlyOnceWith(signInUrl('/e2e/notes?x=1'))
  })

  it('shows the sign-in prompt over the page when a call answers 401 during use', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    stubServer()
    renderApp('/e2e/notes')
    expect(await screen.findByText('No records yet.')).toBeInTheDocument()
    expect(screen.queryByTestId('sign-in-prompt')).not.toBeInTheDocument()

    stubServer({ recordsStatus: 401 })
    await userEvent.click(screen.getByRole('button', { name: 'New' }))
    await apiFetch('/api/apps/E2eApp/entities/Note/records')

    expect(await screen.findByTestId('sign-in-prompt')).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Notes' })).toBeInTheDocument()
    expect(assignLocation).not.toHaveBeenCalled()
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
    await userEvent.click(screen.getByRole('button', { name: 'New' }))
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/noteform/new')
    expect(fetch).toHaveBeenCalledWith('/api/apps/E2eApp/entities/Note/records', expect.anything())
  })

  it('opens a form nested in a section at the new-record address of its page', async () => {
    stubServer()

    renderApp('/e2e/nestedform/new')

    expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Nested note' })).toBeInTheDocument()
    expect(screen.queryByTestId('not-found')).not.toBeInTheDocument()
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

  it('shows the not-found page for a detail page whose record parameter is missing or malformed, and requests no record', async () => {
    stubServer()

    for (const path of [
      '/e2e/notedetail',
      '/e2e/notedetail?note=42',
      '/e2e/notedetail/new?note=6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7',
    ]) {
      const { unmount } = renderApp(path)

      expect(await screen.findByTestId('not-found')).toBeInTheDocument()
      unmount()
    }
    const requested = vi.mocked(fetch).mock.calls.map(([url]) => String(url))
    expect(requested.filter((url) => url.startsWith(`${noteEntity.recordsPath}/`))).toEqual([])
  })

  it('requests the record a detail page names and shows the not-found page when there is none', async () => {
    stubServer()

    renderApp('/e2e/notedetail?note=6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7')

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
    expect(fetch).toHaveBeenCalledWith(
      `${noteEntity.recordsPath}/6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7`,
      expect.anything(),
    )
  })

  it('shows the not-found page for new or a malformed task id under a task inbox page, and requests no task', async () => {
    stubServer()

    for (const path of ['/e2e/tasks/new', '/e2e/tasks/not-a-uuid']) {
      const { unmount } = renderApp(path)

      expect(await screen.findByTestId('not-found')).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Notes' })).toBeInTheDocument()
      unmount()
    }
    const requested = vi.mocked(fetch).mock.calls.map(([url]) => String(url))
    expect(requested.filter((url) => url.startsWith(tasksPath))).toEqual([])
  })

  it('shows the not-found page for new or a malformed id under a process inspector page, and requests no instance', async () => {
    stubServer()

    for (const path of ['/e2e/inspector/new', '/e2e/inspector/not-an-id']) {
      const { unmount } = renderApp(path)

      expect(await screen.findByTestId('not-found')).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Notes' })).toBeInTheDocument()
      unmount()
    }
    const requested = vi.mocked(fetch).mock.calls.map(([url]) => String(url))
    expect(requested.filter((url) => url.startsWith(instancesPath))).toEqual([])
  })

  it('shows the not-found page in the platform shell for an unknown site', async () => {
    stubServer()

    renderApp('/no-such-site')

    expect(await screen.findByTestId('not-found')).toHaveTextContent('Page not found')
    expect(within(screen.getByRole('banner')).getByRole('link', { name: 'Axis' })).toBeInTheDocument()
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
    expect(within(screen.getByRole('banner')).getByRole('link', { name: 'Axis' })).toBeInTheDocument()
  })
})
