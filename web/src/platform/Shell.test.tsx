import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DatePicker, Table, theme } from 'antd'
import viVN from 'antd/locale/vi_VN'
import dayjs from 'dayjs'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { emptyDescription } from '../test/antd'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { Shell } from './Shell'
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
    },
    fallbackTexts: {},
  },
]

/** Shows the container background token the theme gives components inside the shell. */
function Background() {
  const { token } = theme.useToken()
  return <span data-testid="background">{token.colorBgContainer}</span>
}

function renderShell(
  locale: string,
  initialEntries = ['/'],
  navigation: typeof notesNavigation = [],
  children: ReactNode = <DatePicker />,
) {
  return render(
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
    </MemoryRouter>,
  )
}

// The 404 answers the account button's GET /api/me, so the button shows nothing.
beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ title: 'Not found', status: 404 }), { status: 404 })),
  )
})

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

  it('moves the locale switch and the theme toggle into the panel', async () => {
    renderShell('en', ['/'], notesNavigation)
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Menu' }))

    expect(screen.getByRole('switch')).toBeVisible()
  })
})

describe('Shell above narrow width', () => {
  it('shows the navigation in the header without a Menu button', () => {
    renderShell('en', ['/'], notesNavigation)

    expect(screen.queryByRole('button', { name: 'Menu' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Notes' })).toBeVisible()
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
