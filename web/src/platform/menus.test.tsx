import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider, Dropdown, type ConfigProviderProps } from 'antd'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { withAntdStyles } from '../test/antdStyles'
import { menuPadding, menuPopup, shownMenu } from '../test/antd'
import { RowActions, type AxisRowAction } from './AxisTable'
import { AxisSplitButton } from './AxisButton'
import { antConfigFor } from './antTheme'
import { sizeTokens } from './designTokens'
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

const texts = { ...platformEnglish, 'test.open': 'Open', 'test.copy': 'Copy', 'test.rename': 'Rename' }

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

function renderInTheme(element: ReactElement) {
  render(
    <ThemeModeProvider initialMode="light">
      <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
        <MemoryRouter>{element}</MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

const rowActions: AxisRowAction[] = ['open', 'copy', 'rename'].map((key) => ({
  key,
  labelKey: `test.${key}`,
  onClick: vi.fn(),
}))

const splitAction = { key: 'save', label: 'Save', onClick: vi.fn() }
const splitMenu = [
  { key: 'copy', label: 'Copy as draft', onClick: vi.fn() },
  { key: 'remove', label: 'Remove', destructive: true, onClick: vi.fn() },
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
