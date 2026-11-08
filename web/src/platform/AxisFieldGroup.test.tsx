import { ConfigProvider, Input } from 'antd'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AxisFieldGroup } from './AxisFieldGroup'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

function renderGroup(group: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          {group}
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('AxisFieldGroup', () => {
  it('names its group by its title, describes it by its help and shows the help', () => {
    renderGroup(
      <AxisFieldGroup title="Address" help="Where to send it.">
        <Input aria-label="Street" />
      </AxisFieldGroup>,
    )

    const group = screen.getByRole('group', { name: 'Address' })
    expect(group).toHaveAccessibleDescription('Where to send it.')
    expect(screen.getByText('Where to send it.')).toBeVisible()
    expect(within(group).getByRole('textbox', { name: 'Street' })).toBeInTheDocument()
  })

  it('draws no help and gives no description when the help is empty', () => {
    renderGroup(
      <AxisFieldGroup title="Rows" help="">
        <Input aria-label="Row" />
      </AxisFieldGroup>,
    )

    expect(screen.getByRole('group', { name: 'Rows' })).toHaveAccessibleDescription('')
  })
})
