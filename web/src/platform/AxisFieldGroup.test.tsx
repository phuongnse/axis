import { ConfigProvider, Input } from 'antd'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AxisButton } from './AxisButton'
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

  it('draws its error under the fields and starts its description with the error', () => {
    renderGroup(
      <AxisFieldGroup title="Address" help="Where to send it." error="Enter a street.">
        <Input aria-label="Street" aria-invalid />
      </AxisFieldGroup>,
    )

    const group = screen.getByRole('group', { name: 'Address' })
    const message = within(group).getByText('Enter a street.')
    expect(message).toBeVisible()
    expect(within(group).getByRole('textbox', { name: 'Street' }).compareDocumentPosition(message)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )
    expect(group).toHaveAccessibleDescription('Enter a street. Where to send it.')
  })

  it('draws no error and leaves the error out of its description while it has none', () => {
    renderGroup(
      <AxisFieldGroup title="Address" help="Where to send it.">
        <Input aria-label="Street" />
      </AxisFieldGroup>,
    )

    expect(screen.getByRole('group', { name: 'Address' })).toHaveAccessibleDescription('Where to send it.')
    expect(screen.queryByText('Enter a street.')).toBeNull()
  })

  it('draws its secondary action on the title row with its border, and names the group by its title alone', () => {
    renderGroup(
      <AxisFieldGroup title="Rows" help="" action={<AxisButton role="secondary">Add row</AxisButton>}>
        <Input aria-label="Row" />
      </AxisFieldGroup>,
    )

    const group = screen.getByRole('group', { name: 'Rows' })
    const add = within(group).getByRole('button', { name: 'Add row' })
    expect(add).toHaveClass('ant-btn-variant-outlined')
    expect(add).not.toHaveClass('ant-btn-variant-text')
    expect(within(group).getByRole('textbox', { name: 'Row' })).toBeInTheDocument()
  })
})
