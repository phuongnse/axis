import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { PlusOutlined } from '@ant-design/icons'
import { ConfigProvider } from 'antd'
import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { AxisActionRow, AxisButton } from './AxisButton'
import { buttonRoles } from './buttonRoles'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'
import { withAntdStyles } from '../test/antdStyles'

const texts = { 'test.add': 'Add a row' }
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
    renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />)

    expect(screen.getByRole('button', { name: 'Add a row' })).toBeInTheDocument()
    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Add a row' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('shows its tooltip again after Enter, when focus leaves and returns by Tab', async () => {
    renderIn(
      'light',
      <>
        <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />
        <AxisButton role="plain">Next</AxisButton>
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
    renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />)

    await userEvent.hover(screen.getByRole('button', { name: 'Add a row' }))

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('shows its tooltip on hover when disabled, which takes no focus', async () => {
    renderIn('light', <AxisButton role="icon" disabled icon={<PlusOutlined />} nameKey="test.add" />)

    await userEvent.hover(screen.getByRole('button', { name: 'Add a row' }))

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  describe('with antd styles', () => {
    withAntdStyles()

    it('closes its tooltip on click and keeps it closed while the pointer stays on it', async () => {
      renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />)
      const button = screen.getByRole('button', { name: 'Add a row' })
      await userEvent.hover(button)
      await screen.findByRole('tooltip')

      await userEvent.click(button)
      await pause(150)

      expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
    })
  })

  it('opens no tooltip on focus that a script gives', async () => {
    renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />)

    act(() => screen.getByRole('button', { name: 'Add a row' }).focus())
    await pause(150)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('shows its tooltip again when the pointer leaves after a click and comes back', async () => {
    renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />)
    const button = screen.getByRole('button', { name: 'Add a row' })
    await userEvent.click(button)

    await userEvent.unhover(button)
    await userEvent.hover(button)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Add a row')
  })

  it('appends its suffix to the accessible name but shows the name alone as a tooltip', async () => {
    renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" nameSuffix="Category 2" />)

    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Add a row Category 2' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/^Add a row$/)
  })

  it('draws a plain icon borderless and a joined icon as a bordered default button', () => {
    renderIn(
      'light',
      <>
        <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" />
        <AxisButton role="icon" joined icon={<PlusOutlined />} nameKey="test.add" />
      </>,
    )

    const [plain, joined] = screen.getAllByRole('button', { name: 'Add a row' })
    expect(plain).toHaveClass('ant-btn-variant-text')
    expect(joined).toHaveClass('ant-btn-color-default', 'ant-btn-variant-outlined')
  })

  it('draws a destructive icon in dangerText', () => {
    renderIn('dark', <AxisButton role="icon" destructive icon={<PlusOutlined />} nameKey="test.add" />)

    expect(screen.getByRole('button', { name: 'Add a row' })).toHaveStyle({
      color: designTokensFor('dark').colour.dangerText,
    })
  })
})

describe.each(['light', 'dark'] as const)('the roles of the button in %s mode', (mode) => {
  const { colour } = designTokensFor(mode)

  function renderRoles() {
    renderIn(
      mode,
      <>
        <AxisButton role="finish">Finish</AxisButton>
        <AxisButton role="main">Main</AxisButton>
        <AxisButton role="secondary">Secondary</AxisButton>
        <AxisButton role="destructive">Destructive</AxisButton>
      </>,
    )
  }

  it('draws main text in primaryText and destructive text in dangerText', () => {
    renderRoles()

    expect(screen.getByRole('button', { name: 'Main' })).toHaveStyle({
      color: colour.primaryText,
      borderColor: colour.primaryText,
    })
    expect(screen.getByRole('button', { name: 'Destructive' })).toHaveStyle({
      color: colour.dangerText,
      borderColor: colour.dangerText,
    })
  })

  it('draws finish as solid primary and secondary as the default outline', () => {
    renderRoles()

    const finish = screen.getByRole('button', { name: 'Finish' })
    expect(finish).toHaveClass('ant-btn-color-primary', 'ant-btn-variant-solid')
    const secondary = screen.getByRole('button', { name: 'Secondary' })
    expect(secondary).toHaveClass('ant-btn-color-default', 'ant-btn-variant-outlined')
    expect(secondary.style.color).toBe('')
  })

  it('does not set the role on the DOM', () => {
    renderRoles()

    expect(screen.getByRole('button', { name: 'Main' })).not.toHaveAttribute('role')
  })

  it('draws link as a link button with its text in primaryText', () => {
    renderIn(mode, <AxisButton role="link">Open</AxisButton>)

    const link = screen.getByRole('button', { name: 'Open' })
    expect(link).toHaveClass('ant-btn-variant-link')
    expect(link).toHaveStyle({ color: colour.primaryText })
    expect(link).not.toHaveAttribute('role')
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

    expect(new Set(documented)).toEqual(new Set(buttonRoles))
    expect(documented).toHaveLength(buttonRoles.length)
  })
})

describe('the text button with a caret', () => {
  it('ends in an aria-hidden down caret and keeps its name', () => {
    renderIn(
      'light',
      <AxisButton role="plain" caret>
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
        <AxisButton role="destructive">Destructive</AxisButton>
      </AxisActionRow>,
    )
  }

  it('keeps finish solid and draws every other role as a text button', () => {
    renderRow()

    expect(screen.getByRole('button', { name: 'Finish' })).toHaveClass('ant-btn-variant-solid')
    for (const name of ['Main', 'Secondary', 'Destructive']) {
      const button = screen.getByRole('button', { name })
      expect(button).toHaveClass('ant-btn-variant-text')
      expect(button.style.borderColor).toBe('')
    }
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
