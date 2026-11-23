import { ConfigProvider, Input } from 'antd'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { withAntdStyles } from '../test/antdStyles'
import { AxisButton } from './AxisButton'
import { AxisField } from './AxisField'
import { AxisForm, type AxisFormProps } from './AxisForm'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const catalogs = [{ texts: {}, fallbackTexts: {} }]

function renderForm(props: Pick<AxisFormProps<unknown>, 'unmarked'> = {}) {
  render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <AxisForm {...props}>
            <AxisField name="first" label="First" help="The first." requiredMessage="Enter the first">
              <Input />
            </AxisField>
            <AxisField name="second" label="Second" help="The second.">
              <Input />
            </AxisField>
            <AxisButton role="finish" htmlType="submit">
              Save
            </AxisButton>
          </AxisForm>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('AxisForm', () => {
  withAntdStyles()

  it('stacks its fields spacing-lg apart and lays its labels above the inputs', () => {
    renderForm()
    const first = screen.getByRole('textbox', { name: 'First' }).closest('.ant-form-item') as HTMLElement
    const second = screen.getByRole('textbox', { name: 'Second' }).closest('.ant-form-item') as HTMLElement
    const { spacing } = designTokensFor('light')

    expect(first.parentElement).toBe(second.parentElement)
    expect(first.parentElement).toHaveStyle({ gap: `${spacing.lg}px` })
    expect(first.closest('form')).toHaveClass('ant-form-vertical')
  })

  it('marks a required field with the shared required mark', () => {
    renderForm()

    const label = screen.getByText('First').closest('label') as HTMLElement
    expect(label.querySelector('span[aria-hidden="true"]')).toHaveTextContent('*')
  })

  it('marks no field when it is unmarked', () => {
    renderForm({ unmarked: true })

    const label = screen.getByText('First').closest('label') as HTMLElement
    expect(label.querySelector('span[aria-hidden="true"]')).toBeNull()
  })

  it('shows the message of an empty required field on submit and takes the focus to the field', async () => {
    renderForm()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Enter the first')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'First' })).toHaveFocus()
  })
})
