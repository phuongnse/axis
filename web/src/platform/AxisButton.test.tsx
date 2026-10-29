import { PlusOutlined } from '@ant-design/icons'
import { ConfigProvider } from 'antd'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { AxisButton } from './AxisButton'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

const texts = { 'test.add': 'Add a row' }
const catalogs = [{ texts, fallbackTexts: texts }]

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

  it('appends its suffix to the accessible name but shows the name alone as a tooltip', async () => {
    renderIn('light', <AxisButton role="icon" icon={<PlusOutlined />} nameKey="test.add" nameSuffix="Category 2" />)

    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'Add a row Category 2' })).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent(/^Add a row$/)
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
