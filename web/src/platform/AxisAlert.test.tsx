import { ConfigProvider } from 'antd'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { antThemeFor, fieldRules } from './antTheme'
import { AxisAlert, type AlertKind } from './AxisAlert'
import { designTokensFor } from './designTokens'
import { withAntdStyles } from '../test/antdStyles'

const icons: Record<AlertKind, string> = {
  info: 'info-circle',
  success: 'check-circle',
  warning: 'exclamation-circle',
  error: 'close-circle',
}

describe.each(Object.entries(icons) as [AlertKind, string][])('the %s message', (kind, icon) => {
  it('is the alert role, holds its message and shows the kind icon', () => {
    render(<AxisAlert kind={kind} message="Saved" />)

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent('Saved')
    expect(within(alert).getByRole('img', { name: icon, hidden: true })).toBeInTheDocument()
  })
})

describe.each(['light', 'dark'] as const)('the icon colour in %s mode', (mode) => {
  withAntdStyles()

  it('draws the icon of each kind in that kind text colour', () => {
    const { colour } = designTokensFor(mode)
    const textColour: Record<AlertKind, string> = {
      info: colour.infoText,
      success: colour.successText,
      warning: colour.warningText,
      error: colour.dangerText,
    }
    for (const kind of Object.keys(icons) as AlertKind[]) {
      const { unmount } = render(
        <>
          <style>{fieldRules(mode)}</style>
          <ConfigProvider theme={antThemeFor(mode)}>
            <AxisAlert kind={kind} message="Saved" />
          </ConfigProvider>
        </>,
      )
      const alert = screen.getByRole('alert')
      expect(within(alert).getByRole('img', { name: icons[kind], hidden: true })).toHaveStyle({
        color: textColour[kind],
      })
      unmount()
    }
  })
})
