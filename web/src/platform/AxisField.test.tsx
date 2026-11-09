import { ConfigProvider, Form, Input, InputNumber, Select, Upload } from 'antd'
import { render, screen, waitFor, within } from '@testing-library/react'
import { useState } from 'react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisField, type AxisFieldStatus } from './AxisField'
import { requiredMark } from './requiredMark'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { chooseOption, formItem, hasFieldError, hasFieldWarning, isRequiredLabel } from '../test/antd'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

function formTree(field: React.ReactNode) {
  return (
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <Form layout="vertical" requiredMark={requiredMark}>
            {field}
            <button type="submit">Submit</button>
          </Form>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>
  )
}

function renderForm(field: React.ReactNode) {
  return render(formTree(field))
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

  it('passes a filled number and refuses an empty one', async () => {
    renderForm(
      <AxisField label="Count" help="A whole number." name="count" requiredMessage="Enter a count.">
        <InputNumber />
      </AxisField>,
    )
    const input = screen.getByRole('spinbutton', { name: 'Count' })

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Enter a count.')).toBeInTheDocument()

    await userEvent.type(input, '25')

    await waitFor(() => expect(screen.queryByText('Enter a count.')).not.toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(screen.queryByText('Enter a count.')).not.toBeInTheDocument()
  })

  it('refuses an empty file list and passes a picked file', async () => {
    renderForm(
      <AxisField
        label="File"
        help="A CSV file."
        name="file"
        requiredMessage="Choose a file."
        valuePropName="fileList"
        getValueFromEvent={(event: { fileList: unknown[] }) => event.fileList}
      >
        <Upload beforeUpload={() => false}>
          <button type="button">Choose</button>
        </Upload>
      </AxisField>,
    )

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Choose a file.')).toBeInTheDocument()

    const input = document.querySelector('input[type=file]') as HTMLInputElement
    await userEvent.upload(input, new File(['x'], 'notes.csv', { type: 'text/csv' }))

    await waitFor(() => expect(screen.queryByText('Choose a file.')).not.toBeInTheDocument())
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

  it('requires a name for a requiredMessage', () => {
    expect(
      // @ts-expect-error A `requiredMessage` belongs to a field with a `name`, which is in an Ant Design form.
      <AxisField label="Name" help="Letters and digits." requiredMessage="Enter a name.">
        <Input />
      </AxisField>,
    ).toBeDefined()
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

describe('AxisField status', () => {
  const nameField = (status?: AxisFieldStatus) => (
    <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name." status={status}>
      <Input />
    </AxisField>
  )

  it('shows a running status as a busy input in a validating form item', () => {
    renderForm(nameField({ kind: 'running' }))

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(input).toHaveAttribute('aria-busy', 'true')
    expect(formItem(input)).toHaveClass('ant-form-item-is-validating')
  })

  it('announces a passed status and shows its text nowhere else, with no error on the field', () => {
    renderForm(nameField({ kind: 'passed', message: 'Name is free.' }))

    expect(within(screen.getByRole('status')).getByText('Name is free.')).toBeInTheDocument()
    expect(screen.getAllByText('Name is free.')).toHaveLength(1)
    expect(hasFieldError(screen.getByRole('textbox', { name: 'Name' }))).toBe(false)
  })

  it('shows a refused status as the error of the field', () => {
    renderForm(nameField({ kind: 'refused', message: 'That name is taken.' }))

    expect(screen.getByText('That name is taken.')).toBeInTheDocument()
    expect(hasFieldError(screen.getByRole('textbox', { name: 'Name' }))).toBe(true)
  })

  it('shows a status that could not run as the warning of the field', () => {
    renderForm(nameField({ kind: 'couldNotRun', message: 'Names could not be checked.' }))

    expect(screen.getByText('Names could not be checked.')).toBeInTheDocument()
    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(hasFieldWarning(input)).toBe(true)
    expect(hasFieldError(input)).toBe(false)
  })

  it('replaces a rule error with a refused status, and brings the rule error back when the status goes', async () => {
    const { rerender } = renderForm(nameField())
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Enter a name.')).toBeInTheDocument()

    rerender(formTree(nameField({ kind: 'refused', message: 'That name is taken.' })))
    expect(screen.getByText('That name is taken.')).toBeInTheDocument()
    expect(screen.queryByText('Enter a name.')).not.toBeInTheDocument()

    rerender(formTree(nameField()))
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Enter a name.')).toBeInTheDocument()
  })

  it('takes no status outside a form', () => {
    expect(
      // @ts-expect-error A field outside a form takes no `status`: its form item does not draw one.
      <AxisField label="Name" help="Letters and digits." status={{ kind: 'running' }}>
        <Input value="" onChange={() => {}} />
      </AxisField>,
    ).toBeDefined()
  })
})

function renderOutside(field: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          {field}
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

describe('AxisField outside a form', () => {
  it('names the input by its label and describes it by its help', () => {
    renderOutside(
      <AxisField label="Name" help="Letters and digits.">
        <Input value="Orders" onChange={() => {}} />
      </AxisField>,
    )

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(input).toHaveAccessibleDescription('Letters and digits.')
    expect(screen.getByText('Letters and digits.')).toBeVisible()
  })

  it('lets the caller hold the value: typing reaches its change handler', async () => {
    const changed = vi.fn()
    function Held() {
      const [value, setValue] = useState('')
      return (
        <AxisField label="Name" help="Letters and digits.">
          <Input
            value={value}
            onChange={(event) => {
              changed(event.target.value)
              setValue(event.target.value)
            }}
          />
        </AxisField>
      )
    }
    renderOutside(<Held />)

    await userEvent.type(screen.getByRole('textbox', { name: 'Name' }), 'ab')

    expect(changed).toHaveBeenLastCalledWith('ab')
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('ab')
  })

  it('keeps the id the input has', () => {
    renderOutside(
      <AxisField label="Name" help="Letters and digits.">
        <Input id="own-id" value="" onChange={() => {}} />
      </AxisField>,
    )

    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAttribute('id', 'own-id')
  })

  it('draws no help and gives no description when the help is empty', () => {
    renderOutside(
      <AxisField label="Name" help="">
        <Input value="" onChange={() => {}} />
      </AxisField>,
    )

    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAccessibleDescription('')
  })

  it('keeps the description the input has beside its help', () => {
    renderOutside(
      <>
        <span id="own-note">Shown in the list.</span>
        <AxisField label="Name" help="Letters and digits.">
          <Input aria-describedby="own-note" value="" onChange={() => {}} />
        </AxisField>
      </>,
    )

    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveAccessibleDescription(
      'Letters and digits. Shown in the list.',
    )
  })
})
