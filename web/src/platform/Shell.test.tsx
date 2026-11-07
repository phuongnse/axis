import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DatePicker, Table, theme } from 'antd'
import viVN from 'antd/locale/vi_VN'
import dayjs from 'dayjs'
import { useEffect, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { emptyDescription } from '../test/antd'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { PreferencesMenu } from './PreferencesMenu'
import { Shell } from './Shell'
import { ShellNavigation } from './ShellNavigation'
import { ShellSidebar } from './shellSidebar'
import { useShellSidebar } from './shellSidebarContext'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { narrowWidthQuery } from './useNarrowWidth'

const catalogs = [{ texts: {}, fallbackTexts: {} }]

const notesNavigation = [
  { key: 'home', path: '/', labelKey: 'nav.home' },
  { key: 'notes', path: '/e2e/notes', labelKey: 'nav.notes' },
  { key: 'categories', path: '/e2e/categories', labelKey: 'nav.categories' },
]
const navigationTexts = [
  {
    texts: {
      'nav.home': 'Home',
      'nav.notes': 'Notes',
      'nav.categories': 'Categories',
      'shell.navigation.menu': 'Menu',
      'shell.navigation.collapse': 'Collapse navigation',
      'shell.navigation.expand': 'Expand navigation',
      'shell.preferences.label': 'Language and theme',
      'site.title': 'Workspace',
    },
    fallbackTexts: {},
  },
]

/** Shows the container background token the theme gives components inside the shell. */
function Background() {
  const { token } = theme.useToken()
  return <span data-testid="background">{token.colorBgContainer}</span>
}

function shellFor(navigation: typeof notesNavigation, children: ReactNode, locale = 'en', initialEntries = ['/']) {
  return (
    <MemoryRouter initialEntries={initialEntries}>
      <ThemeModeProvider>
        <TextProvider catalogs={navigation.length > 0 ? navigationTexts : catalogs} development={false}>
          <Shell
            titleKey="site.title"
            navigation={navigation}
            locales={['en', 'vi']}
            locale={locale}
            onLocaleChange={() => {}}
          >
            {children}
          </Shell>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>
  )
}

function renderShell(
  locale: string,
  initialEntries = ['/'],
  navigation: typeof notesNavigation = [],
  children: ReactNode = <DatePicker />,
) {
  return render(shellFor(navigation, children, locale, initialEntries))
}

/** Answers the footer's GET /api/about with the version, or with 401 for nobody signed in. */
function stubAbout(version: string | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      if (input === '/api/about') {
        return version === null
          ? new Response(null, { status: 401 })
          : new Response(JSON.stringify({ version }), { status: 200 })
      }
      return new Response(JSON.stringify({ title: 'Not found', status: 404 }), { status: 404 })
    }),
  )
}

// The 401 answers the footer's GET /api/about, so the footer shows no version. The 404 answers the account
// button's GET /api/me, so the button shows nothing.
beforeEach(() => stubAbout(null))

afterEach(() => {
  dayjs.locale('en')
})

describe('Shell', () => {
  it('shows Ant Design components in English for en', () => {
    renderShell('en')

    expect(screen.getByRole('textbox')).toHaveAttribute('placeholder', 'Select date')
    expect(dayjs.locale()).toBe('en')
  })

  it('shows Ant Design components in Vietnamese for vi', () => {
    renderShell('vi')

    // The date placeholder of Ant Design's vi_VN locale.
    expect(screen.getByRole('textbox')).toHaveAttribute('placeholder', 'Chọn thời điểm')
    expect(dayjs.locale()).toBe('vi')
  })

  it('shows an empty table in Vietnamese for vi', () => {
    renderShell('vi', ['/'], [], <Table columns={[{ title: 'Name', dataIndex: 'name' }]} dataSource={[]} />)

    expect(emptyDescription(viVN.Empty!.description!)).toBeInTheDocument()
  })
})

describe.each(['light', 'dark'] as const)('Shell in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('gives components the background token of the mode', () => {
    renderShell('en', ['/'], [], <Background />)

    const expected = theme.getDesignToken({
      algorithm: mode === 'dark' ? theme.darkAlgorithm : theme.defaultAlgorithm,
    }).colorBgContainer
    expect(screen.getByTestId('background')).toHaveTextContent(expected)
  })
})

it('gives dark mode a different background token from light mode', () => {
  expect(theme.getDesignToken({ algorithm: theme.darkAlgorithm }).colorBgContainer).not.toBe(
    theme.getDesignToken({ algorithm: theme.defaultAlgorithm }).colorBgContainer,
  )
})

describe('Shell navigation', () => {
  it('marks the entry above a form page as the current page', () => {
    renderShell('en', ['/e2e/notes/new'], notesNavigation)

    expect(screen.getByRole('link', { name: 'Notes' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Categories' })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('link', { name: 'Home' })).not.toHaveAttribute('aria-current')
  })

  it('matches the site path ignoring letter case', () => {
    renderShell('en', ['/E2E/Notes'], notesNavigation)

    expect(screen.getByRole('link', { name: 'Notes' })).toHaveAttribute('aria-current', 'page')
  })

  it('marks Home only on the home path', () => {
    renderShell('en', ['/studio'], notesNavigation)

    expect(screen.getByRole('link', { name: 'Home' })).not.toHaveAttribute('aria-current')
  })

  it('marks Home on the home path', () => {
    renderShell('en', ['/'], notesNavigation)

    expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('aria-current', 'page')
  })
})

describe('Shell at narrow width', () => {
  beforeEach(() => {
    vi.stubGlobal('matchMedia', (query: string) => ({
      matches: query === narrowWidthQuery,
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent: () => false,
    }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('hides the navigation behind a closed Menu button', () => {
    renderShell('en', ['/'], notesNavigation)

    expect(screen.getByRole('button', { name: 'Menu' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('link', { name: 'Notes' })).not.toBeInTheDocument()
  })

  it('opens the navigation with Enter and closes it with Escape, returning focus to the button', async () => {
    renderShell('en', ['/'], notesNavigation)
    const button = screen.getByRole('button', { name: 'Menu' })

    button.focus()
    await userEvent.keyboard('{Enter}')
    expect(button).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('link', { name: 'Notes' })).toBeVisible()

    await userEvent.keyboard('{Escape}')
    expect(button).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('link', { name: 'Notes' })).not.toBeInTheDocument()
    expect(button).toHaveFocus()
  })

  it('closes the navigation with Escape from inside the panel', async () => {
    renderShell('en', ['/'], notesNavigation)
    const button = screen.getByRole('button', { name: 'Menu' })
    await userEvent.click(button)

    screen.getByRole('link', { name: 'Notes' }).focus()
    await userEvent.keyboard('{Escape}')

    expect(button).toHaveAttribute('aria-expanded', 'false')
    expect(button).toHaveFocus()
  })

  it('closes the navigation after a link is followed', async () => {
    renderShell('en', ['/'], notesNavigation)
    const button = screen.getByRole('button', { name: 'Menu' })
    await userEvent.click(button)

    await userEvent.click(screen.getByRole('link', { name: 'Notes' }))

    expect(button).toHaveAttribute('aria-expanded', 'false')
  })

  it('moves the language and theme menu into the panel', async () => {
    renderShell('en', ['/'], notesNavigation)
    expect(screen.queryByRole('button', { name: 'Language and theme' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Menu' }))

    const panel = document.getElementById('shell-menu')!
    expect(within(panel).getByRole('button', { name: 'Language and theme' })).toBeVisible()
    expect(within(screen.getByRole('banner')).queryByRole('button', { name: 'Language and theme' })).toBeNull()
  })
})

describe('PreferencesMenu', () => {
  const preferenceTexts = [
    {
      texts: {
        'shell.preferences.label': 'Language and theme',
        'shell.locale.label': 'Language',
        'shell.locale.en': 'English',
        'shell.locale.vi': 'Tiếng Việt',
        'shell.theme.label': 'Theme',
        'shell.theme.system': 'System',
        'shell.theme.light': 'Light',
        'shell.theme.dark': 'Dark',
      },
      fallbackTexts: {},
    },
  ]

  function renderPreferences(onLocaleChange = vi.fn()) {
    render(
      <ThemeModeProvider>
        <TextProvider catalogs={preferenceTexts} development={false}>
          <PreferencesMenu locales={['en', 'vi']} locale="en" onLocaleChange={onLocaleChange} />
        </TextProvider>
      </ThemeModeProvider>,
    )
    return onLocaleChange
  }

  async function openMenu() {
    await userEvent.click(screen.getByRole('button', { name: 'Language and theme' }))
    return screen.findByRole('menu', { name: 'Language and theme' })
  }

  it('lists the languages and the themes in two groups, with the current ones marked', async () => {
    localStorage.setItem('axis.themeMode', 'light')
    renderPreferences()

    const menu = within(await openMenu())

    expect(menu.getByText('Language')).toBeInTheDocument()
    expect(menu.getByText('Theme')).toBeInTheDocument()
    expect(menu.getByRole('menuitemradio', { name: 'English' })).toHaveAttribute('aria-checked', 'true')
    expect(menu.getByRole('menuitemradio', { name: 'Tiếng Việt' })).toHaveAttribute('aria-checked', 'false')
    expect(menu.getByRole('menuitemradio', { name: 'System' })).toHaveAttribute('aria-checked', 'false')
    expect(menu.getByRole('menuitemradio', { name: 'Light' })).toHaveAttribute('aria-checked', 'true')
    expect(menu.getByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'false')
  })

  it('marks System as the current theme when nothing is stored', async () => {
    localStorage.clear()
    renderPreferences()

    const menu = within(await openMenu())

    expect(menu.getByRole('menuitemradio', { name: 'System' })).toHaveAttribute('aria-checked', 'true')
    expect(menu.getByRole('menuitemradio', { name: 'Light' })).toHaveAttribute('aria-checked', 'false')
    expect(menu.getByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'false')
  })

  it('picks the system setting, stores it and marks it as current', async () => {
    localStorage.setItem('axis.themeMode', 'dark')
    renderPreferences()

    await userEvent.click(within(await openMenu()).getByRole('menuitemradio', { name: 'System' }))

    expect(localStorage.getItem('axis.themeMode')).toBe('system')
    const menu = within(await openMenu())
    expect(menu.getByRole('menuitemradio', { name: 'System' })).toHaveAttribute('aria-checked', 'true')
    expect(menu.getByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'false')
  })

  it('asks for the picked language, and not for the current one', async () => {
    const onLocaleChange = renderPreferences()

    await userEvent.click(within(await openMenu()).getByRole('menuitemradio', { name: 'English' }))
    expect(onLocaleChange).not.toHaveBeenCalled()

    await userEvent.click(within(await openMenu()).getByRole('menuitemradio', { name: 'Tiếng Việt' }))
    expect(onLocaleChange).toHaveBeenCalledExactlyOnceWith('vi')
  })

  it('marks the current language and theme as selected, with a divider between the groups', async () => {
    localStorage.setItem('axis.themeMode', 'light')
    renderPreferences()

    const menu = within(await openMenu())
    const selected = 'ant-dropdown-menu-item-selected'

    expect(menu.getByRole('menuitemradio', { name: 'English' })).toHaveClass(selected)
    expect(menu.getByRole('menuitemradio', { name: 'Light' })).toHaveClass(selected)
    expect(menu.getByRole('menuitemradio', { name: 'Tiếng Việt' })).not.toHaveClass(selected)
    expect(menu.getByRole('menuitemradio', { name: 'Dark' })).not.toHaveClass(selected)
    expect(menu.getAllByRole('separator')).toHaveLength(1)
  })

  it('switches to the dark theme, stores it and marks it as current', async () => {
    localStorage.setItem('axis.themeMode', 'light')
    renderPreferences()

    await userEvent.click(within(await openMenu()).getByRole('menuitemradio', { name: 'Dark' }))

    expect(localStorage.getItem('axis.themeMode')).toBe('dark')
    const menu = within(await openMenu())
    expect(menu.getByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true')
    expect(menu.getByRole('menuitemradio', { name: 'Light' })).toHaveAttribute('aria-checked', 'false')
  })
})

describe('Shell above narrow width', () => {
  it('shows the navigation in a sidebar without a Menu button', () => {
    renderShell('en', ['/'], notesNavigation)

    expect(screen.queryByRole('button', { name: 'Menu' })).not.toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Menu' })).toBeVisible()
    expect(screen.getByRole('link', { name: 'Notes' })).toBeVisible()
    expect(screen.getByRole('button', { name: 'Collapse navigation' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('links the mark and the title to the first navigation page', () => {
    renderShell('en', ['/e2e/notes'], notesNavigation)

    const home = within(screen.getByRole('banner')).getByRole('link', { name: 'Workspace' })
    expect(home).toHaveAttribute('href', '/')
    expect(home).toHaveTextContent('W')
  })

  it('collapses the sidebar to icons and opens it again, naming what the button does', async () => {
    renderShell('en', ['/'], notesNavigation)

    await userEvent.click(screen.getByRole('button', { name: 'Collapse navigation' }))

    const expand = screen.getByRole('button', { name: 'Expand navigation' })
    expect(expand).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('button', { name: 'Collapse navigation' })).not.toBeInTheDocument()
    await userEvent.hover(screen.getByRole('menuitem', { name: 'Notes' }))
    await waitFor(() => expect(screen.getAllByRole('tooltip').map((tooltip) => tooltip.textContent)).toContain('Notes'))

    await userEvent.click(expand)

    expect(screen.getByRole('button', { name: 'Collapse navigation' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('names the collapse button in a tooltip on keyboard focus', async () => {
    renderShell('en', ['/'], notesNavigation)

    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Collapse navigation' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Collapse navigation')
  })

  it('names the language and theme button in a tooltip on keyboard focus', async () => {
    renderShell('en', ['/'], notesNavigation)

    await userEvent.tab()
    await userEvent.tab()
    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Language and theme' })).toHaveFocus()
    await waitFor(() =>
      expect(screen.getAllByRole('tooltip').map((tooltip) => tooltip.textContent)).toContain('Language and theme'),
    )
  })

  it('stays collapsed while the person follows a navigation link', async () => {
    renderShell('en', ['/'], notesNavigation)
    await userEvent.click(screen.getByRole('button', { name: 'Collapse navigation' }))
    // The collapsed entry's tooltip repeats its link outside the navigation, so look inside the navigation.
    const navigation = within(screen.getByRole('navigation', { name: 'Menu' }))

    await userEvent.click(navigation.getByRole('link', { name: 'Categories' }))

    expect(navigation.getByRole('link', { name: 'Categories' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Expand navigation' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('shows an open entry as its label alone, with no icon and no count', () => {
    renderShell('en', ['/'], notesNavigation)

    const entries = screen.getAllByRole('menuitem')
    expect(entries.map((entry) => entry.textContent)).toEqual(['Home', 'Notes', 'Categories'])
    for (const entry of entries) {
      expect(entry.querySelector('.anticon')).toBeNull()
    }
  })

  it('shows a collapsed entry as an icon, with the label in a tooltip', async () => {
    renderShell('en', ['/'], notesNavigation)

    await userEvent.click(screen.getByRole('button', { name: 'Collapse navigation' }))

    for (const entry of screen.getAllByRole('menuitem')) {
      expect(entry.querySelector('.anticon')).toHaveAttribute('aria-hidden', 'true')
    }
    await userEvent.hover(screen.getByRole('menuitem', { name: 'Categories' }))
    await waitFor(() =>
      expect(screen.getAllByRole('tooltip').map((tooltip) => tooltip.textContent)).toContain('Categories'),
    )
  })

  it('has no sidebar and no collapse button on a one-page site', () => {
    renderShell('en', ['/'], notesNavigation.slice(0, 1))

    expect(screen.queryByRole('navigation', { name: 'Menu' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Collapse navigation' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Expand navigation' })).not.toBeInTheDocument()
  })
})

describe('Shell sidebar claimed by a page', () => {
  const claimed = [
    { key: 'entities', path: '/studio/entities', label: 'Entities' },
    { key: 'releases', path: '/studio/releases', label: 'Releases' },
  ]

  function ClaimedNavigation() {
    const { collapsed } = useShellSidebar()
    return <ShellNavigation entries={claimed} activeKey="entities" collapsed={collapsed} />
  }

  function claim() {
    return (
      <ShellSidebar>
        <ClaimedNavigation />
      </ShellSidebar>
    )
  }

  it("replaces the site's entries and shows the collapse button on a one-page site", async () => {
    renderShell('en', ['/'], notesNavigation.slice(0, 1), claim())

    const navigation = within(await screen.findByRole('navigation', { name: 'Menu' }))
    expect(navigation.getByRole('link', { name: 'Entities' })).toHaveAttribute('aria-current', 'page')
    expect(navigation.getByRole('link', { name: 'Releases' })).toBeVisible()
    expect(screen.queryByRole('link', { name: 'Home' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Collapse navigation' })).toBeVisible()
  })

  it("replaces the site's entries on a site with several", async () => {
    renderShell('en', ['/'], notesNavigation, claim())

    await screen.findByRole('link', { name: 'Entities' })
    expect(screen.queryByRole('link', { name: 'Notes' })).toBeNull()
  })

  it("gives the sidebar back to the site's entries when the page stops claiming it", async () => {
    const view = renderShell('en', ['/'], notesNavigation.slice(0, 1), claim())
    await screen.findByRole('link', { name: 'Entities' })

    view.rerender(shellFor(notesNavigation.slice(0, 1), <p>No claim</p>))

    expect(screen.queryByRole('navigation', { name: 'Menu' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Collapse navigation' })).toBeNull()
  })

  it('collapses the claimed entries to icons with the collapse button', async () => {
    renderShell('en', ['/'], notesNavigation.slice(0, 1), claim())
    await screen.findByRole('link', { name: 'Entities' })

    await userEvent.click(screen.getByRole('button', { name: 'Collapse navigation' }))

    for (const entry of screen.getAllByRole('menuitem')) {
      expect(entry.querySelector('.anticon')).toHaveAttribute('aria-hidden', 'true')
    }
    await userEvent.hover(screen.getByRole('menuitem', { name: 'Releases' }))
    await waitFor(() =>
      expect(screen.getAllByRole('tooltip').map((tooltip) => tooltip.textContent)).toContain('Releases'),
    )
  })

  it('mounts a page that claims the sidebar once', async () => {
    let mounts = 0
    function Counted() {
      useEffect(() => {
        mounts += 1
      }, [])
      return claim()
    }
    renderShell('en', ['/'], notesNavigation.slice(0, 1), <Counted />)

    await screen.findByRole('link', { name: 'Entities' })

    expect(mounts).toBe(1)
  })
})

describe('ShellNavigation groups', () => {
  const groups = [
    {
      key: 'model',
      label: 'Model',
      entries: [{ key: 'entities', path: '/studio/entities', label: 'Entities' }],
    },
  ]

  it('shows the title of an open group above its entries, with no rule', () => {
    render(
      <MemoryRouter>
        <ShellNavigation entries={groups} />
      </MemoryRouter>,
    )

    expect(screen.getByText('Model')).toBeVisible()
    expect(screen.getByRole('link', { name: 'Entities' })).toBeVisible()
    expect(screen.queryByRole('separator')).toBeNull()
  })

  it('replaces the title of a collapsed group with a thin rule', () => {
    render(
      <MemoryRouter>
        <ShellNavigation entries={groups} collapsed />
      </MemoryRouter>,
    )

    expect(screen.queryByText('Model')).toBeNull()
    expect(screen.getByRole('separator')).toBeInTheDocument()
  })
})

describe('Shell scrolling', () => {
  it('keeps the header at the top while the page scrolls', () => {
    renderShell('en', ['/'], notesNavigation)

    expect(screen.getByRole('banner')).toHaveStyle({ position: 'sticky', top: '0px' })
  })

  describe.each(['light', 'dark'] as const)('in %s mode', (mode) => {
    it('sets the colour scheme of the page, so scrollbars follow the mode', () => {
      localStorage.setItem('axis.themeMode', mode)
      const { container } = renderShell('en')

      expect(document.documentElement.style.colorScheme).toBe(mode)
      expect(container.querySelector('[data-theme-mode]')).toHaveStyle({ colorScheme: mode })
    })
  })
})

describe('Shell footer', () => {
  it('sits under the content column beside a sidebar, and not in the sidebar', () => {
    renderShell('en', ['/'], notesNavigation)

    const parent = screen.getByRole('contentinfo').parentElement!
    expect(parent).toContainElement(screen.getByRole('main'))
    expect(parent).not.toContainElement(document.getElementById('shell-sidebar'))
  })

  it('runs across the page, beside the header, on a one-page site', () => {
    renderShell('en', ['/'], notesNavigation.slice(0, 1))

    expect(screen.getByRole('contentinfo').parentElement).toBe(screen.getByRole('banner').parentElement)
  })

  it('shows the version to a signed-in person', async () => {
    stubAbout('0.1.0')
    renderShell('en', ['/'], notesNavigation)

    await waitFor(() => expect(within(screen.getByRole('contentinfo')).getByText('v0.1.0')).toBeInTheDocument())
    expect(within(screen.getByRole('contentinfo')).getByText(`© ${new Date().getFullYear()} Axis`)).toBeInTheDocument()
  })

  it('shows no version to an anonymous visitor', async () => {
    renderShell('en', ['/'], notesNavigation)

    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/about', expect.anything()))
    const footer = screen.getByRole('contentinfo')
    expect(within(footer).getByText(`© ${new Date().getFullYear()} Axis`)).toBeInTheDocument()
    expect(within(footer).queryByText(/^v\d/)).toBeNull()
  })

  it('shows the footer without a server status', async () => {
    renderShell('en', ['/'], notesNavigation)

    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/about', expect.anything()))
    const footer = screen.getByRole('contentinfo')
    expect(footer).toHaveTextContent(`© ${new Date().getFullYear()} Axis`)
    expect(footer).not.toHaveTextContent(/server status/i)
    expect(vi.mocked(fetch).mock.calls.map(([input]) => input)).not.toContain('/health/ready')
  })
})

describe('Shell account slot', () => {
  const signedInUser = { id: 'u', displayName: 'Ada', roles: [], studio: true, console: false, users: false }
  const accountCatalogs = [
    {
      texts: {
        'shell.appSwitcher.label': 'Switch to',
        'shell.appSwitcher.studio': 'Studio',
        'site.title': 'Workspace',
      },
      fallbackTexts: {},
    },
  ]

  function renderWith(account?: ReactNode) {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input === '/api/me'
          ? new Response(JSON.stringify(signedInUser))
          : new Response(JSON.stringify([]), { status: 404 }),
      ),
    )
    return render(
      <MemoryRouter>
        <ThemeModeProvider>
          <TextProvider catalogs={accountCatalogs} development={false}>
            <Shell
              titleKey="site.title"
              navigation={[]}
              locales={['en']}
              locale="en"
              onLocaleChange={() => {}}
              account={account}
            >
              <p>page</p>
            </Shell>
          </TextProvider>
        </ThemeModeProvider>
      </MemoryRouter>,
    )
  }

  it('shows the app switcher without an account', async () => {
    renderWith()

    expect(await screen.findByRole('button', { name: 'Switch to' })).toBeInTheDocument()
  })

  it('shows the account in place of the app switcher and the account button', async () => {
    renderWith(<span data-testid="account">Ada Operator</span>)

    expect(await screen.findByTestId('account')).toHaveTextContent('Ada Operator')
    await waitFor(() => expect(fetch).not.toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(screen.queryByRole('button', { name: 'Switch to' })).not.toBeInTheDocument()
  })
})

describe('antLocaleFor', () => {
  it('maps en and vi in any letter case, and anything else to en_US', () => {
    expect(antLocaleFor('en').locale).toBe('en')
    expect(antLocaleFor('VI').locale).toBe('vi')
    expect(antLocaleFor('fr').locale).toBe('en')
    expect(dayjsLocaleFor('Vi')).toBe('vi')
    expect(dayjsLocaleFor('fr')).toBe('en')
  })
})
