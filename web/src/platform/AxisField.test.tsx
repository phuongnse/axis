import { ConfigProvider, Form, Input, Select } from 'antd'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { AxisField } from './AxisField'
import { requiredMark } from './requiredMark'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { chooseOption, formItem, hasFieldError, isRequiredLabel } from '../test/antd'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

function renderForm(field: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <Form layout="vertical" requiredMark={requiredMark}>
            {field}
            <button type="submit">Submit</button>
          </Form>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('AxisField', () => {
  it('draws its label, its required mark and its help as the description of the input', () => {
    renderForm(
      <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name.">
        <Input />
      </AxisField>,
    )

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(isRequiredLabel(screen.getByText('Name'))).toBe(true)
    expect(screen.getByText('*')).toBeInTheDocument()
    expect(input).toHaveAccessibleDescription('Letters and digits.')
    expect(screen.getByText('Letters and digits.')).toBeInTheDocument()
  })

  it('shows its message under the input once the empty field is checked, and drops it when the field is filled', async () => {
    renderForm(
      <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name.">
        <Input />
      </AxisField>,
    )
    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(hasFieldError(input)).toBe(false)

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    expect(await screen.findByText('Enter a name.')).toBeInTheDocument()
    expect(hasFieldError(input)).toBe(true)

    await userEvent.type(input, 'Orders')

    await waitFor(() => expect(screen.queryByText('Enter a name.')).not.toBeInTheDocument())
    expect(hasFieldError(input)).toBe(false)
  })

  it('refuses a value of only spaces as empty', async () => {
    renderForm(
      <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name.">
        <Input />
      </AxisField>,
    )

    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), '   ')
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    expect(await screen.findByText('Enter a name.')).toBeInTheDocument()
  })

  it('checks its other rules too', async () => {
    renderForm(
      <AxisField
        label="Name"
        help="Letters and digits."
        name="name"
        requiredMessage="Enter a name."
        rules={[{ pattern: /^[A-Za-z]+$/, message: 'Use letters only.' }]}
      >
        <Input />
      </AxisField>,
    )

    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'a1')
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    expect(await screen.findByText('Use letters only.')).toBeInTheDocument()
  })

  it('has no mark and no check without a requiredMessage', async () => {
    renderForm(
      <AxisField label="Note" help="Anything to add." name="note">
        <Input />
      </AxisField>,
    )
    const input = screen.getByRole('textbox', { name: 'Note' })

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    expect(isRequiredLabel(screen.getByText('Note'))).toBe(false)
    expect(screen.queryByText('*')).not.toBeInTheDocument()
    expect(hasFieldError(input)).toBe(false)
    expect(input).toHaveAccessibleDescription('Anything to add.')
  })

  it('keeps the help in the description while the error shows', async () => {
    renderForm(
      <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name.">
        <Input />
      </AxisField>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    await screen.findByText('Enter a name.')

    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAccessibleDescription(
      expect.stringContaining('Letters and digits.'),
    )
  })

  it('checks a multiple select: empty it asks for a choice, and with a choice it is right', async () => {
    renderForm(
      <AxisField label="States" help="The states." name="states" requiredMessage="Choose a state.">
        <Select mode="multiple" aria-label="States" virtual={false} options={[{ value: 'open', label: 'open' }]} />
      </AxisField>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Choose a state.')).toBeInTheDocument()

    await chooseOption('open', formItem(screen.getByRole('combobox', { name: 'States' })))
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(screen.queryByText('Choose a state.')).not.toBeInTheDocument())
  })

  it('requires help', () => {
    expect(
      // @ts-expect-error `help` is required on a platform field, see docs/reference/frontend.md#platform-field-help.
      <AxisField label="Name" name="name">
        <Input />
      </AxisField>,
    ).toBeDefined()
  })
})
