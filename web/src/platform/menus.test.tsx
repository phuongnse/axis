import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider, Dropdown, theme, type ConfigProviderProps } from 'antd'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { withAntdStyles } from '../test/antdStyles'
import { menuPadding, menuPopup, shownMenu } from '../test/antd'
import { RowActions } from './AxisTable'
import { AxisSplitButton } from './AxisButton'
import type { AxisAction } from './menuActions'
import { antConfigFor, antThemeFor } from './antTheme'
import { designTokensFor, sizeTokens } from './designTokens'
import { PreferencesMenu } from './PreferencesMenu'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { UserMenu } from './UserMenu'

// Every dropdown menu has one look from the shared theme, see docs/reference/actions-and-controls.md#menus.

const requester = {
  id: 'requester',
  displayName: 'Requester User',
  roles: ['employee'],
  studio: false,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  studioAi: false,
  studioDeveloper: false,
  console: false,
  users: false,
  tenantStatus: 'active',
  billingUrl: null,
  tenantExports: false,
}

const texts = {
  ...platformEnglish,
  'test.open': 'Open',
  'test.copy': 'Copy',
  'test.rename': 'Rename',
  'test.save': 'Save',
  'test.copyAsDraft': 'Copy as draft',
  'test.remove': 'Remove',
}

beforeEach(() => {
  // The account menu reads the signed-in person from the server.
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      if (input === '/api/me') {
        return new Response(JSON.stringify(requester), { headers: { 'Content-Type': 'application/json' } })
      }
      return new Response(null, { status: 404 })
    }),
  )
})

function renderInTheme(element: ReactElement, path = '/') {
  render(
    <ThemeModeProvider initialMode="light">
      <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
        <MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

const rowActions: AxisAction[] = ['open', 'copy', 'rename'].map((key) => ({
  key,
  labelKey: `test.${key}`,
  onClick: vi.fn(),
}))

const splitAction: AxisAction = { key: 'save', labelKey: 'test.save', onClick: vi.fn() }
const splitMenu: AxisAction[] = [
  { key: 'copy', labelKey: 'test.copyAsDraft', onClick: vi.fn() },
  { key: 'remove', labelKey: 'test.remove', destructive: true, onClick: vi.fn() },
]

describe('every dropdown menu is at least the menu token wide', () => {
  it.each([
    ['the account menu', <UserMenu key="account" />, 'Requester User', 'Account'],
    [
      'the language and theme menu',
      <PreferencesMenu key="preferences" locales={['en', 'vi']} locale="en" onLocaleChange={vi.fn()} />,
      'Language and theme',
      'Language and theme',
    ],
    ['a row’s “…” menu', <RowActions key="row" actions={rowActions} />, 'Actions', 'Actions'],
    [
      'a More actions menu',
      <AxisSplitButton key="split" action={splitAction} menu={splitMenu} />,
      'More actions',
      'More actions',
    ],
  ])('gives %s the menu size as its least width', async (_name, element, trigger, menuName) => {
    renderInTheme(element)

    await userEvent.click(await screen.findByRole('button', { name: trigger }))

    expect(menuPopup(await shownMenu(menuName)).style.minWidth).toBe(`${sizeTokens.menu}px`)
  })
})

/** The opening tags of the `<Dropdown` elements in `text`. A `>` inside braces is part of an attribute, not the tag's end. */
function dropdownOpenings(text: string): string[] {
  const openings: string[] = []
  for (const match of text.matchAll(/<Dropdown\b/g)) {
    const start = match.index
    let depth = 0
    let end = start
    for (; end < text.length; end++) {
      const char = text[end]
      if (char === '{') depth++
      else if (char === '}') depth--
      else if (char === '>' && depth === 0) break
    }
    openings.push(text.slice(start, end))
  }
  return openings
}

/** The files with a dropdown that passes its own styles, class names or overlay, which would set its width or look apart. */
function ownLook(files: Record<string, string>) {
  const attribute = /\s(styles|classNames|overlayStyle|overlayClassName|rootClassName)=/
  return Object.entries(files)
    .filter(([, text]) => dropdownOpenings(text).some((opening) => attribute.test(opening)))
    .map(([file]) => file)
}

describe('no dropdown sets its own look', () => {
  const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  it('finds the dropdowns', () => {
    const count = Object.values(sources).reduce((total, text) => total + dropdownOpenings(text).length, 0)
    expect(count).toBeGreaterThan(0)
  })

  it('passes no dropdown its own styles, class names or overlay, so the shared width reaches every menu', () => {
    expect(ownLook(sources)).toEqual([])
  })

  it('names a dropdown that passes its own overlay style', () => {
    expect(
      ownLook({ '../pages/Sample.tsx': "<Dropdown\n  trigger={['click']}\n  overlayStyle={{ minWidth: 200 }}\n>" }),
    ).toEqual(['../pages/Sample.tsx'])
  })

  it('looks past the braces of a menu that holds an arrow function', () => {
    const clean = "<Dropdown trigger={['click']} menu={{ items, onClick: () => close() }}>"
    expect(ownLook({ '../pages/Sample.tsx': clean })).toEqual([])
  })
})

/** The padding tokens of an open two-item menu under `config`. */
async function insetUnder(config: ConfigProviderProps) {
  const { unmount } = render(
    <ConfigProvider {...config}>
      <Dropdown
        trigger={['click']}
        menu={{
          items: [
            { key: 'first', label: 'First' },
            { key: 'second', label: 'Second' },
          ],
          'aria-label': 'Inset',
        }}
      >
        <button type="button">Open</button>
      </Dropdown>
    </ConfigProvider>,
  )
  await userEvent.click(screen.getByRole('button', { name: 'Open' }))
  const tokens = menuPadding(await shownMenu('Inset'))
  unmount()
  return tokens
}

describe('an Axis menu keeps the inset Ant Design draws', () => {
  withAntdStyles()

  it('pads its menu and its items with the tokens a plain Ant Design menu uses', async () => {
    const axis = await insetUnder(antConfigFor('light'))
    const plain = await insetUnder({})

    expect(Object.keys(plain).length).toBeGreaterThan(0)
    expect(axis).toEqual(plain)
  })
})

/**
 * The tint and text colour an item draws when it is selected. jsdom leaves Ant Design's `var()` unresolved, so the test
 * reads the variable each one names, which Ant Design sets on the menu.
 */
function selectedColours(item: HTMLElement) {
  const style = getComputedStyle(item)
  return {
    background: style.getPropertyValue('--ant-control-item-bg-active'),
    color: style.getPropertyValue('--ant-color-primary'),
  }
}

describe('a menu marks its current choice on the selected tint', () => {
  withAntdStyles()

  const { colour } = designTokensFor('light')
  const tint = theme.getDesignToken(antThemeFor('light')).controlItemBgActive

  it('marks Your account on the account page as a link to it, with Sign out after a divider', async () => {
    renderInTheme(<UserMenu />, '/account')

    await userEvent.click(await screen.findByRole('button', { name: 'Requester User' }))
    const menu = await shownMenu('Account')
    const account = within(menu).getByRole('menuitem', { name: 'Your account' })
    expect(account).toHaveClass('ant-dropdown-menu-item-selected')
    expect(selectedColours(account)).toEqual({ background: tint, color: colour.primaryText })
    expect(within(account).getByRole('link', { name: 'Your account' })).toHaveAttribute('aria-current', 'page')
    const entries = [...menu.querySelectorAll('[role="menuitem"], [role="separator"]')].map((entry) =>
      entry.getAttribute('role') === 'separator' ? 'divider' : entry.textContent,
    )
    expect(entries).toEqual(['Your account', 'divider', 'Sign out'])
  })

  it('does not mark Your account away from the account page', async () => {
    renderInTheme(<UserMenu />, '/studio')

    await userEvent.click(await screen.findByRole('button', { name: 'Requester User' }))
    const account = within(await shownMenu('Account')).getByRole('menuitem', { name: 'Your account' })
    expect(account).not.toHaveClass('ant-dropdown-menu-item-selected')
    expect(within(account).getByRole('link', { name: 'Your account' })).not.toHaveAttribute('aria-current')
  })

  it('marks the current language and theme as checked entries on the selected tint', async () => {
    renderInTheme(<PreferencesMenu locales={['en', 'vi']} locale="en" onLocaleChange={vi.fn()} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Language and theme' }))
    const menu = await shownMenu('Language and theme')
    for (const name of ['English', 'Light']) {
      const entry = within(menu).getByRole('menuitemradio', { name })
      expect(entry).toHaveAttribute('aria-checked', 'true')
      expect(entry).toHaveClass('ant-dropdown-menu-item-selected')
      expect(selectedColours(entry)).toEqual({ background: tint, color: colour.primaryText })
    }
    const dark = within(menu).getByRole('menuitemradio', { name: 'Dark' })
    expect(dark).toHaveAttribute('aria-checked', 'false')
    expect(dark).not.toHaveClass('ant-dropdown-menu-item-selected')
  })
})
