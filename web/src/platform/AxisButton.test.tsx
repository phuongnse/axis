import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { PlusOutlined } from '@ant-design/icons'
import { ConfigProvider, theme } from 'antd'
import { useState } from 'react'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AxisActionRow, AxisButton, AxisButtonGroup, AxisSplitButton } from './AxisButton'
import { antThemeFor, textButtonClass } from './antTheme'
import { buttonRoles } from './buttonRoles'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'
import { withAntdStyles } from '../test/antdStyles'

const texts = {
  'test.add': 'Add a row',
  'test.added': 'Added',
  'test.validate': 'Validate',
  'test.preview': 'Preview',
  'test.runTests': 'Run tests',
  'test.discard': 'Discard',
  'shell.actionRow.more': 'More actions',
  'test.decision': 'Decision',
}
const catalogs = [{ texts, fallbackTexts: texts }]

/** Waits past Ant Design's hover delay, so a tooltip that should open has opened. */
function pause(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function renderIn(mode: ThemeMode, ui: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode={mode}>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          {ui}
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('the icon-only button', () => {
  it('has its accessible name and shows it as a tooltip on keyboard focus', async () => {
    renderIn('light', <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />)

    expect(screen.getByRole('button', { name: 'Add a row' })).toBeInTheDocument()
    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Add a row' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('shows its tooltip again after Enter, when focus leaves and returns by Tab', async () => {
    renderIn(
      'light',
      <>
        <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />
        <AxisButton role="subtle">Next</AxisButton>
      </>,
    )
    const button = screen.getByRole('button', { name: 'Add a row' })
    await userEvent.tab()
    await userEvent.keyboard('{Enter}')

    await userEvent.tab()
    expect(button).not.toHaveFocus()
    await userEvent.tab({ shift: true })

    expect(button).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('shows its tooltip on hover', async () => {
    renderIn('light', <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />)

    await userEvent.hover(screen.getByRole('button', { name: 'Add a row' }))

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('shows its tooltip on hover when disabled, which takes no focus', async () => {
    renderIn('light', <AxisButton role="subtle" disabled icon={<PlusOutlined />} nameKey="test.add" />)

    await userEvent.hover(screen.getByRole('button', { name: 'Add a row' }))

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  describe('with antd styles', () => {
    withAntdStyles()

    it('closes its tooltip on click and keeps it closed while the pointer stays on it', async () => {
      renderIn('light', <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />)
      const button = screen.getByRole('button', { name: 'Add a row' })
      await userEvent.hover(button)
      await screen.findByRole('tooltip')

      await userEvent.click(button)
      await pause(150)

      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
    })
  })

  it('opens no tooltip on focus that a script gives', async () => {
    renderIn('light', <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />)

    act(() => screen.getByRole('button', { name: 'Add a row' }).focus())
    await pause(150)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('shows its tooltip again when the pointer leaves after a click and comes back', async () => {
    renderIn('light', <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />)
    const button = screen.getByRole('button', { name: 'Add a row' })
    await userEvent.click(button)

    await userEvent.unhover(button)
    await userEvent.hover(button)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('says its done text in the tooltip right after a pointer click, and keeps its name', async () => {
    function Copy() {
      const [done, setDone] = useState(false)
      return (
        <AxisButton
          role="subtle"
          icon={<PlusOutlined />}
          nameKey="test.add"
          doneKey={done ? 'test.added' : undefined}
          onClick={() => setDone(true)}
        />
      )
    }
    renderIn('light', <Copy />)
    const button = screen.getByRole('button', { name: 'Add a row' })
    await userEvent.hover(button)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')

    await userEvent.click(button)

    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Added'))
    expect(screen.getByRole('button', { name: 'Add a row' })).toBeInTheDocument()
  })

  it('appends its suffix to the accessible name but shows the name alone as a tooltip', async () => {
    renderIn('light', <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" nameSuffix="Category 2" />)

    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Add a row Category 2' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/^Add a row$/)
  })

  it('draws a plain icon borderless and a joined icon as a bordered default button', () => {
    renderIn(
      'light',
      <>
        <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />
        <AxisButton role="subtle" joined icon={<PlusOutlined />} nameKey="test.add" />
      </>,
    )

    const [plain, joined] = screen.getAllByRole('button', { name: 'Add a row' })
    expect(plain).toHaveClass('ant-btn-variant-text')
    expect(joined).toHaveClass('ant-btn-color-default', 'ant-btn-variant-outlined')
  })

  it('draws a destructive icon in dangerText', () => {
    renderIn('dark', <AxisButton role="subtle" destructive icon={<PlusOutlined />} nameKey="test.add" />)

    expect(screen.getByRole('button', { name: 'Add a row' })).toHaveStyle({
      color: designTokensFor('dark').colour.dangerText,
    })
  })
})

describe.each(['light', 'dark'] as const)('the roles of the button in %s mode', (mode) => {
  const { colour } = designTokensFor(mode)
  const tint = theme.getDesignToken(antThemeFor(mode)).controlItemBgActive

  function renderRoles() {
    renderIn(
      mode,
      <>
        <AxisButton role="finish">Finish</AxisButton>
        <AxisButton role="main">Main</AxisButton>
        <AxisButton role="secondary">Secondary</AxisButton>
        <AxisButton role="subtle">Subtle</AxisButton>
        <AxisButton role="finish" destructive>
          Finish destructive
        </AxisButton>
        <AxisButton role="main" destructive>
          Main destructive
        </AxisButton>
        <AxisButton role="secondary" destructive>
          Secondary destructive
        </AxisButton>
        <AxisButton role="subtle" destructive>
          Subtle destructive
        </AxisButton>
      </>,
    )
  }

  it('draws main in primaryText with a border in primaryText', () => {
    renderRoles()

    const main = screen.getByRole('button', { name: 'Main' })
    expect(main).toHaveClass('ant-btn-variant-outlined')
    expect(main).toHaveStyle({ color: colour.primaryText, borderColor: colour.primaryText })
  })

  it('draws finish as solid primary, secondary as the default outline and subtle as borderless text', () => {
    renderRoles()

    const finish = screen.getByRole('button', { name: 'Finish' })
    expect(finish).toHaveClass('ant-btn-color-primary', 'ant-btn-variant-solid')
    expect(finish.style.background).toBe('')
    const secondary = screen.getByRole('button', { name: 'Secondary' })
    expect(secondary).toHaveClass('ant-btn-color-default', 'ant-btn-variant-outlined')
    expect(secondary.style.color).toBe('')
    const subtle = screen.getByRole('button', { name: 'Subtle' })
    expect(subtle).toHaveClass('ant-btn-variant-text')
    expect(subtle.style.color).toBe('')
    expect(subtle.style.borderColor).toBe('')
  })

  it('draws a main or secondary button in the destructive tone in dangerText with a border in dangerText', () => {
    renderRoles()

    for (const name of ['Main destructive', 'Secondary destructive']) {
      const button = screen.getByRole('button', { name })
      expect(button).toHaveClass('ant-btn-variant-outlined')
      expect(button).toHaveStyle({ color: colour.dangerText, borderColor: colour.dangerText })
    }
  })

  it('draws a subtle button in the destructive tone as borderless text in dangerText', () => {
    renderRoles()

    const button = screen.getByRole('button', { name: 'Subtle destructive' })
    expect(button).toHaveClass('ant-btn-variant-text')
    expect(button).toHaveStyle({ color: colour.dangerText })
    expect(button.style.borderColor).toBe('')
  })

  it('draws a finish button in the destructive tone as a solid danger fill with white text', () => {
    renderRoles()

    const button = screen.getByRole('button', { name: 'Finish destructive' })
    expect(button).toHaveClass('ant-btn-variant-solid')
    expect(button).toHaveStyle({
      background: colour.danger,
      borderColor: colour.danger,
      color: colour.onPrimary,
    })
    expect(colour.onPrimary).toBe('#ffffff')
  })

  it('keeps no inline colour on a disabled button in the destructive tone', () => {
    renderIn(
      mode,
      <>
        <AxisButton role="finish" destructive disabled>
          Finish
        </AxisButton>
        <AxisButton role="secondary" destructive disabled>
          Secondary
        </AxisButton>
      </>,
    )

    expect(screen.getByRole('button', { name: 'Finish' }).style.background).toBe('')
    expect(screen.getByRole('button', { name: 'Secondary' }).style.color).toBe('')
  })

  it('does not set the role on the DOM', () => {
    renderRoles()

    expect(screen.getByRole('button', { name: 'Main' })).not.toHaveAttribute('role')
  })

  describe('with selected', () => {
    it('draws a subtle button on the selected tint in primaryText with one aria-pressed', () => {
      renderIn(
        mode,
        <AxisButton role="subtle" selected>
          Cell
        </AxisButton>,
      )

      const button = screen.getByRole('button', { name: 'Cell' })
      expect(button).toHaveStyle({ background: tint, color: colour.primaryText })
      expect(button).toHaveAttribute('aria-pressed', 'true')
      expect(button).not.toHaveAttribute('aria-current')
    })

    it('draws a subtle button that is not selected with no tint and aria-pressed false', () => {
      renderIn(
        mode,
        <AxisButton role="subtle" selected={false}>
          Cell
        </AxisButton>,
      )

      const button = screen.getByRole('button', { name: 'Cell' })
      expect(button).toHaveAttribute('aria-pressed', 'false')
      expect(button.style.background).toBe('')
      expect(button.style.color).toBe('')
    })

    it('sets aria-current for a page or a step, and no aria-pressed', () => {
      renderIn(
        mode,
        <>
          <AxisButton role="subtle" selected="page">
            Page
          </AxisButton>
          <AxisButton role="subtle" selected="step">
            Step
          </AxisButton>
        </>,
      )

      const page = screen.getByRole('button', { name: 'Page' })
      expect(page).toHaveAttribute('aria-current', 'page')
      expect(page).not.toHaveAttribute('aria-pressed')
      expect(page).toHaveStyle({ background: tint, color: colour.primaryText })
      expect(screen.getByRole('button', { name: 'Step' })).toHaveAttribute('aria-current', 'step')
    })

    it('drops the attributes the caller passes, so there is exactly one', () => {
      renderIn(
        mode,
        <>
          <AxisButton role="subtle" selected="page" aria-current="true" aria-pressed>
            Page
          </AxisButton>
          <AxisButton role="subtle" selected aria-current="true" aria-pressed="false">
            Chosen
          </AxisButton>
        </>,
      )

      const page = screen.getByRole('button', { name: 'Page' })
      expect(page).toHaveAttribute('aria-current', 'page')
      expect(page).not.toHaveAttribute('aria-pressed')
      const chosen = screen.getByRole('button', { name: 'Chosen' })
      expect(chosen).toHaveAttribute('aria-pressed', 'true')
      expect(chosen).not.toHaveAttribute('aria-current')
    })

    it('keeps the attributes of the caller without selected', () => {
      renderIn(
        mode,
        <AxisButton role="subtle" aria-pressed>
          Cell
        </AxisButton>,
      )

      expect(screen.getByRole('button', { name: 'Cell' })).toHaveAttribute('aria-pressed', 'true')
    })

    it('draws an icon-only subtle button on the selected tint with its one attribute', () => {
      renderIn(
        mode,
        <AxisButton role="subtle" selected aria-current="true" icon={<PlusOutlined />} nameKey="test.add" />,
      )

      const button = screen.getByRole('button', { name: 'Add a row' })
      expect(button).toHaveStyle({ background: tint, color: colour.primaryText })
      expect(button).toHaveAttribute('aria-pressed', 'true')
      expect(button).not.toHaveAttribute('aria-current')
    })
  })
})

describe('the roles of the button in the docs', () => {
  it('are the roles AxisButton defines', () => {
    // Vitest runs from the web folder, as `npm test --prefix web` does.
    const doc = readFileSync(join(process.cwd(), '..', 'docs/reference/actions-and-controls.md'), 'utf8')
    const start = doc.indexOf('## Buttons')
    const end = doc.indexOf('\n## ', start + 1)
    const documented = doc
      .slice(start, end === -1 ? undefined : end)
      .split('\n')
      .filter((line) => line.startsWith('|'))
      .slice(2)
      .map((line) => line.split('|')[1].trim().toLowerCase())

    expect(documented).toEqual([...buttonRoles])
  })
})

describe('the text button with a caret', () => {
  it('ends in an aria-hidden down caret and keeps its name', () => {
    renderIn(
      'light',
      <AxisButton role="subtle" caret>
        Account
      </AxisButton>,
    )

    const button = screen.getByRole('button', { name: 'Account' })
    expect(button.lastElementChild).toHaveClass('anticon-down')
    expect(button.lastElementChild).toHaveAttribute('aria-hidden', 'true')
    expect(button).toHaveClass('ant-btn-variant-text')
    expect(button).not.toHaveAttribute('caret')
  })
})

describe.each(['light', 'dark'] as const)('the roles of the button inside an action row in %s mode', (mode) => {
  const { colour } = designTokensFor(mode)

  function renderRow() {
    renderIn(
      mode,
      <AxisActionRow>
        <AxisButton role="finish">Finish</AxisButton>
        <AxisButton role="main">Main</AxisButton>
        <AxisButton role="secondary">Secondary</AxisButton>
        <AxisButton role="secondary" destructive>
          Destructive
        </AxisButton>
      </AxisActionRow>,
    )
  }

  it('keeps finish solid and draws secondary and destructive as text buttons', () => {
    renderRow()

    expect(screen.getByRole('button', { name: 'Finish' })).toHaveClass('ant-btn-variant-solid')
    for (const name of ['Secondary', 'Destructive']) {
      const button = screen.getByRole('button', { name })
      expect(button).toHaveClass('ant-btn-variant-text')
      expect(button.style.borderColor).toBe('')
    }
  })

  it('keeps main outlined with its border in primaryText beside a finish button', () => {
    renderRow()

    const main = screen.getByRole('button', { name: 'Main' })
    expect(main).toHaveClass('ant-btn-variant-outlined')
    expect(main).toHaveStyle({ color: colour.primaryText, borderColor: colour.primaryText })
  })

  it('draws main in primaryText, destructive in dangerText and secondary in the default colour', () => {
    renderRow()

    expect(screen.getByRole('button', { name: 'Main' })).toHaveStyle({ color: colour.primaryText })
    expect(screen.getByRole('button', { name: 'Destructive' })).toHaveStyle({ color: colour.dangerText })
    expect(screen.getByRole('button', { name: 'Secondary' }).style.color).toBe('')
  })
})

describe('a disabled button', () => {
  it('keeps no inline colour', () => {
    renderIn(
      'light',
      <AxisButton role="main" disabled>
        Main
      </AxisButton>,
    )

    expect(screen.getByRole('button', { name: 'Main' }).style.color).toBe('')
  })
})

// jsdom has no layout, so the widths are faked: the row is `box` wide, and its content `content` wide.
describe('an action row that folds', () => {
  let box = 0
  let content = 0
  let resize: () => void = () => {}

  function fakeWidths(boxWidth: number, contentWidth: number) {
    box = boxWidth
    content = contentWidth
    const onRow = (element: HTMLElement, width: number) => (element.hasAttribute('data-fold-row') ? width : 0)
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
      return onRow(this, box)
    })
    vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockImplementation(function (this: HTMLElement) {
      return onRow(this, Math.max(box, content))
    })
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(callback: () => void) {
          resize = () => act(callback)
        }
        observe() {}
        unobserve() {}
        disconnect() {}
      },
    )
  }

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  const actions = (disabled = false, onClick: () => void = () => {}) => [
    { key: 'validate', labelKey: 'test.validate', disabled, onClick },
    { key: 'preview', labelKey: 'test.preview', disabled, onClick },
    { key: 'run', labelKey: 'test.runTests', disabled, onClick },
  ]
  const renderFolding = (fold = actions()) =>
    renderIn(
      'light',
      <AxisActionRow fold={fold} end={<AxisButton role="finish">Publish…</AxisButton>}>
        <AxisButton role="main">New resource…</AxisButton>
      </AxisActionRow>,
    )

  it('wraps a row whose fold names one action, with no More actions menu, at narrow width', () => {
    fakeWidths(300, 400)
    renderIn(
      'light',
      <AxisActionRow fold={actions().slice(0, 1)} end={<AxisButton role="finish">Publish…</AxisButton>}>
        <AxisButton role="main">New resource…</AxisButton>
      </AxisActionRow>,
    )

    const validate = screen.getByRole('button', { name: 'Validate' })
    expect(validate.parentElement).toHaveClass('ant-flex-wrap-wrap')
    expect(validate.parentElement).not.toHaveAttribute('data-fold-row')
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
  })

  it('keeps one line, and a row without fold still wraps', () => {
    fakeWidths(500, 400)
    const { container } = renderFolding()

    expect(container.querySelector('[data-fold-row]')).not.toHaveClass('ant-flex-wrap-wrap')
    renderIn('light', <AxisActionRow>Row</AxisActionRow>)
    expect(screen.getByText('Row')).toHaveClass('ant-flex-wrap-wrap')
    expect(screen.getByText('Row')).not.toHaveAttribute('data-fold-row')
  })

  it('shows every action as a button, in order, while they fit', () => {
    fakeWidths(500, 400)
    renderFolding()

    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'New resource…',
      'Validate',
      'Preview',
      'Run tests',
      'Publish…',
    ])
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
  })

  it('folds the named actions, in order, into one More actions menu when they do not fit, and unfolds them when they do', async () => {
    fakeWidths(300, 400)
    renderFolding()

    expect(screen.getAllByRole('button').map((button) => button.textContent ?? button.ariaLabel)).toEqual([
      'New resource…',
      '',
      'Publish…',
    ])
    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent)).toEqual([
      'Validate',
      'Preview',
      'Run tests',
    ])
    expect(screen.queryByRole('button', { name: 'Validate' })).not.toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    // The row is only as wide as the content was before it folded, so it unfolds once it is that wide.
    box = 399
    resize()
    expect(screen.getByRole('button', { name: 'More actions' })).toBeInTheDocument()
    box = 400
    resize()
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual([
      'New resource…',
      'Validate',
      'Preview',
      'Run tests',
      'Publish…',
    ])
  })

  it('draws a destructive fold action in dangerText as a button, and as a danger entry of the menu after a divider', async () => {
    const discard = { key: 'discard', labelKey: 'test.discard', destructive: true, disabled: false, onClick: () => {} }
    fakeWidths(300, 400)
    renderFolding([...actions().slice(0, 1), discard])

    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const menu = await screen.findByRole('menu', { name: 'More actions' })
    expect(within(menu).getByRole('menuitem', { name: 'Discard' })).toHaveClass('ant-dropdown-menu-item-danger')
    expect(within(menu).getByRole('separator')).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')

    box = 1000
    resize()
    expect(screen.getByRole('button', { name: 'Discard' })).toHaveStyle({
      color: designTokensFor('light').colour.dangerText,
    })
  })

  it('folds when the row shrinks after it drew', () => {
    fakeWidths(500, 400)
    renderFolding()
    expect(screen.queryByRole('button', { name: 'More actions' })).not.toBeInTheDocument()

    box = 200
    resize()

    expect(screen.getByRole('button', { name: 'More actions' })).toBeInTheDocument()
  })

  it('keeps the disabled state of a folded entry', async () => {
    fakeWidths(300, 400)
    const onClick = vi.fn()
    renderFolding(actions(true, onClick))

    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const entry = await screen.findByRole('menuitem', { name: 'Validate' })
    expect(entry).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(entry)

    expect(onClick).not.toHaveBeenCalled()
  })

  it('opens from the More actions button and runs an entry by keyboard alone', async () => {
    fakeWidths(300, 400)
    const onClick = vi.fn()
    renderFolding(actions(false, onClick))

    await userEvent.tab()
    await userEvent.tab()
    expect(screen.getByRole('button', { name: 'More actions' })).toHaveFocus()
    await userEvent.keyboard('{Enter}')
    expect(await screen.findByRole('menu', { name: 'More actions' })).toBeInTheDocument()
    // The menu takes focus once it has opened, on its first entry.
    await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Validate' })).toHaveFocus())
    // jsdom gives user-event's keys no key code, which the menu reads, so Enter is fired with it.
    fireEvent.keyDown(document.activeElement!, { key: 'Enter', keyCode: 13 })

    expect(onClick).toHaveBeenCalledTimes(1)
  })
})

describe('the button group', () => {
  const group = (labels: string[], onClick = vi.fn()) => (
    <AxisButtonGroup
      role="main"
      nameKey="test.decision"
      actions={labels.map((label) => ({ key: label, label, onClick: () => onClick(label) }))}
    />
  )

  it('is one named group of its buttons, in order, in the role of the group', async () => {
    const onClick = vi.fn()
    renderIn('light', group(['Approve', 'Return', 'Reject'], onClick))

    const named = screen.getByRole('group', { name: 'Decision' })
    expect(
      within(named)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Approve', 'Return', 'Reject'])
    expect(within(named).getByRole('button', { name: 'Approve' })).toHaveClass('ant-btn-variant-outlined')
    await userEvent.click(within(named).getByRole('button', { name: 'Return' }))
    expect(onClick).toHaveBeenCalledWith('Return')
  })

  it('is a plain button when it holds one action', () => {
    renderIn('light', group(['Approve']))

    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument()
    expect(screen.queryByRole('group')).not.toBeInTheDocument()
  })
})

describe('the split button', () => {
  const split = (onClick = vi.fn()) => (
    <AxisSplitButton
      nameSuffix="Mine"
      action={{ key: 'overwrite', label: 'Overwrite', onClick: () => onClick('overwrite') }}
      menu={[
        { key: 'rename', label: 'Rename', onClick: () => onClick('rename') },
        { key: 'delete', label: 'Delete', destructive: true, onClick: () => onClick('delete') },
      ]}
    />
  )

  it('does the main action from its text and opens no menu', async () => {
    const onClick = vi.fn()
    renderIn('light', split(onClick))

    await userEvent.click(screen.getByRole('button', { name: 'Overwrite Mine' }))

    expect(onClick.mock.calls).toEqual([['overwrite']])
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('opens the other actions from its caret in their order, and does the one chosen', async () => {
    const onClick = vi.fn()
    renderIn('light', split(onClick))

    await userEvent.click(screen.getByRole('button', { name: 'More actions Mine' }))

    const menu = await screen.findByRole('menu', { name: 'More actions' })
    expect(
      within(menu)
        .getAllByRole('menuitem')
        .map((item) => item.textContent),
    ).toEqual(['Rename', 'Delete'])
    expect(onClick).not.toHaveBeenCalled()
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Delete' }))
    expect(onClick.mock.calls).toEqual([['delete']])
  })

  it('is one joined shape of the text and the caret', () => {
    const { container } = renderIn('light', split())

    const compact = container.querySelector('.ant-space-compact')
    expect(compact).not.toBeNull()
    expect(within(compact as HTMLElement).getAllByRole('button')).toHaveLength(2)
  })

  it('draws a destructive action of its menu as a danger entry, which the theme draws in dangerText', async () => {
    renderIn('dark', split())

    await userEvent.click(screen.getByRole('button', { name: 'More actions Mine' }))

    const menu = await screen.findByRole('menu', { name: 'More actions' })
    expect(within(menu).getByRole('menuitem', { name: 'Delete' })).toHaveClass('ant-dropdown-menu-item-danger')
    expect(within(menu).getByRole('menuitem', { name: 'Rename' })).not.toHaveClass('ant-dropdown-menu-item-danger')
  })
})

describe('the class of a text button', () => {
  it('is on every text button AxisButton draws, and on no solid, outlined or joined one', () => {
    const { container } = renderIn(
      'light',
      <>
        <AxisButton role="subtle">Subtle</AxisButton>
        <AxisButton role="subtle" destructive>
          Subtle destructive
        </AxisButton>
        <AxisButton role="subtle" caret>
          Subtle caret
        </AxisButton>
        <AxisButton role="subtle" selected>
          Subtle selected
        </AxisButton>
        <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" />
        <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.validate" joined />
        <AxisButton role="finish">Finish</AxisButton>
        <AxisButton role="finish" destructive>
          Finish destructive
        </AxisButton>
        <AxisButton role="main">Main</AxisButton>
        <AxisButton role="secondary">Outlined secondary</AxisButton>
        <AxisButton role="secondary" destructive>
          Outlined destructive
        </AxisButton>
        <AxisActionRow>
          <AxisButton role="secondary">Row secondary</AxisButton>
          <AxisButton role="secondary" destructive>
            Row destructive
          </AxisButton>
        </AxisActionRow>
      </>,
    )

    const buttons = [...container.querySelectorAll('button')]
    expect(buttons).toHaveLength(13)
    const text = buttons.filter((button) => button.classList.contains('ant-btn-variant-text'))
    expect(text).toHaveLength(7)
    for (const button of buttons) {
      expect(button.classList.contains(textButtonClass)).toBe(text.includes(button))
    }
  })

  it('keeps a class the caller passes beside it', () => {
    renderIn(
      'light',
      <>
        <AxisButton role="subtle" className="mine">
          Subtle
        </AxisButton>
        <AxisButton role="finish" className="finish-mine">
          Finish
        </AxisButton>
        <AxisButton role="subtle" icon={<PlusOutlined />} nameKey="test.add" className="icon-mine" />
      </>,
    )

    expect(screen.getByRole('button', { name: 'Subtle' })).toHaveClass('mine', textButtonClass)
    expect(screen.getByRole('button', { name: 'Finish' })).toHaveClass('finish-mine')
    expect(screen.getByRole('button', { name: 'Add a row' })).toHaveClass('icon-mine', textButtonClass)
  })
})
