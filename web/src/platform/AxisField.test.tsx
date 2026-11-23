import { Checkbox, ConfigProvider, Form, Input, InputNumber, Select, Upload } from 'antd'
import { render, screen, waitFor, within } from '@testing-library/react'
import { useState } from 'react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisField, type AxisFieldStatus } from './AxisField'
import { requiredMark } from './requiredMark'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { withAntdStyles } from '../test/antdStyles'
import { chooseOption, formItem, formMessages, hasFieldError, hasFieldWarning, isRequiredLabel } from '../test/antd'

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

  it('counts an unticked box as empty, also after it was ticked', async () => {
    renderForm(
      <AxisField
        label="Agree"
        help="Tick to agree."
        name="agree"
        requiredMessage="Tick the box."
        valuePropName="checked"
      >
        <Checkbox />
      </AxisField>,
    )
    const box = screen.getByRole('checkbox', { name: 'Agree' })

    await userEvent.click(box)
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(screen.queryByText('Tick the box.')).not.toBeInTheDocument()

    await userEvent.click(box)

    expect(await screen.findByText('Tick the box.')).toBeInTheDocument()
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

describe('AxisField message look', () => {
  withAntdStyles()

  async function renderEmptyRequired() {
    renderForm(
      <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name.">
        <Input />
      </AxisField>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    return { input: screen.getByRole('textbox', { name: 'Name' }), message: await screen.findByText('Enter a name.') }
  }

  it('draws a rule error after its input, 4px under it, in a 14px font on a 20px line', async () => {
    const { input, message } = await renderEmptyRequired()

    expect(formMessages(input)).toHaveStyle({ marginTop: '4px', fontSize: '14px', lineHeight: '20px' })
    expect(formMessages(input).contains(message)).toBe(true)
    expect(input.compareDocumentPosition(message) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('grows an item with an error: it keeps no bottom margin and nothing cancels the message height', async () => {
    const { input } = await renderEmptyRequired()

    const item = formItem(input)
    expect(getComputedStyle(item).getPropertyValue('--ant-form-item-margin-bottom')).toBe('0px')
    for (const element of [item, ...item.querySelectorAll<HTMLElement>('*')]) {
      expect(Number.parseFloat(getComputedStyle(element).marginBottom || '0')).not.toBeLessThan(0)
    }
  })
})

describe('AxisField status', () => {
  const nameField = (status?: AxisFieldStatus) => (
    <AxisField label="Name" help="Letters and digits." name="name" requiredMessage="Enter a name." status={status}>
      <Input />
    </AxisField>
  )

  it('shows a running status as a busy input in a validating form item', () => {
    renderForm(nameField({ state: 'running' }))

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(input).toHaveAttribute('aria-busy', 'true')
    expect(formItem(input)).toHaveClass('ant-form-item-is-validating')
    expect(formItem(input).querySelector('.ant-form-item-feedback-icon-validating .anticon-loading')).not.toBeNull()
  })

  it('keeps the same input as its status changes, so typing into it is not interrupted', () => {
    const { rerender } = renderForm(nameField({ state: 'idle' }))
    const input = screen.getByRole('textbox', { name: 'Name' })

    rerender(formTree(nameField({ state: 'running' })))
    rerender(formTree(nameField({ state: 'passed', message: 'Name is free.' })))
    rerender(formTree(nameField({ state: 'refused', message: 'That name is taken.' })))

    expect(screen.getByRole('textbox', { name: 'Name' })).toBe(input)
  })

  it('announces a passed status and shows its text nowhere else, with no error on the field', () => {
    renderForm(nameField({ state: 'passed', message: 'Name is free.' }))

    expect(within(screen.getByRole('status')).getByText('Name is free.')).toBeInTheDocument()
    expect(screen.getAllByText('Name is free.')).toHaveLength(1)
    expect(hasFieldError(screen.getByRole('textbox', { name: 'Name' }))).toBe(false)
  })

  it('shows a refused status as the error of the field', () => {
    renderForm(nameField({ state: 'refused', message: 'That name is taken.' }))

    expect(screen.getByText('That name is taken.')).toBeInTheDocument()
    expect(hasFieldError(screen.getByRole('textbox', { name: 'Name' }))).toBe(true)
  })

  it('shows a failed status as the warning of the field', () => {
    renderForm(nameField({ state: 'failed', message: 'Names could not be checked.' }))

    expect(screen.getByText('Names could not be checked.')).toBeInTheDocument()
    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(hasFieldWarning(input)).toBe(true)
    expect(hasFieldError(input)).toBe(false)
  })

  it('replaces a rule error with a refused status, and brings the rule error back when the status goes', async () => {
    const { rerender } = renderForm(nameField())
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Enter a name.')).toBeInTheDocument()

    rerender(formTree(nameField({ state: 'refused', message: 'That name is taken.' })))
    expect(screen.getByText('That name is taken.')).toBeInTheDocument()
    expect(screen.queryByText('Enter a name.')).not.toBeInTheDocument()

    rerender(formTree(nameField()))
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Enter a name.')).toBeInTheDocument()
  })

  it('starts at its initial value and returns to it on reset', async () => {
    const finish = vi.fn()
    render(
      <ThemeModeProvider initialMode="light">
        <ConfigProvider theme={{ token: { motion: false } }}>
          <TextProvider catalogs={catalogs} development={false}>
            <Form layout="vertical" onFinish={finish}>
              <AxisField label="Name" help="Letters and digits." name="name" initialValue="Contoso">
                <Input />
              </AxisField>
              <button type="submit">Submit</button>
              <Form.Item shouldUpdate noStyle>
                {(form) => (
                  <button type="button" onClick={() => form.resetFields()}>
                    Reset
                  </button>
                )}
              </Form.Item>
            </Form>
          </TextProvider>
        </ConfigProvider>
      </ThemeModeProvider>,
    )
    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(input).toHaveValue('Contoso')

    await userEvent.clear(input)
    await userEvent.type(input, 'Fabrikam')
    await userEvent.click(screen.getByRole('button', { name: 'Reset' }))
    expect(screen.getByRole('textbox', { name: 'Name' })).toHaveValue('Contoso')

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))
    await waitFor(() => expect(finish).toHaveBeenCalledTimes(1))
    expect(finish.mock.calls[0][0]).toEqual({ name: 'Contoso' })
  })

  it('takes no status outside a form', () => {
    expect(
      // @ts-expect-error A field outside a form takes no `status`: its form item does not draw one.
      <AxisField label="Name" help="Letters and digits." status={{ state: 'running' }}>
        <Input value="" onChange={() => {}} />
      </AxisField>,
    ).toBeDefined()
  })
})

function outsideTree(field: React.ReactNode) {
  return (
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          {field}
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>
  )
}

function renderOutside(field: React.ReactNode) {
  return render(outsideTree(field))
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

  it('marks a required field with * after its label and aria-required on its input, and the label still names it', () => {
    renderOutside(
      <AxisField label="Name" help="Letters and digits." required>
        <Input value="" onChange={() => {}} />
      </AxisField>,
    )

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(input).toHaveAttribute('aria-required', 'true')
    expect(screen.getByText('*')).toBeInTheDocument()
    expect(screen.getByText('*')).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByText('*').closest('label')).toHaveTextContent(/^Name\*$/)
  })

  it('marks no field required unless it says so', () => {
    renderOutside(
      <AxisField label="Name" help="Letters and digits.">
        <Input value="" onChange={() => {}} />
      </AxisField>,
    )

    expect(screen.getByRole('textbox', { name: 'Name' })).not.toHaveAttribute('aria-required')
    expect(screen.queryByText('*')).not.toBeInTheDocument()
  })

  it('leaves the marking of a field in a form to its requiredMessage', () => {
    expect(
      // @ts-expect-error A field with a `name` is marked required by its `requiredMessage`, not by `required`.
      <AxisField label="Name" help="Letters and digits." name="name" required>
        <Input />
      </AxisField>,
    ).toBeDefined()
  })
})

describe('AxisField error outside a form', () => {
  it('draws the error under the input, marks the input in error and puts the error first in its description', () => {
    renderOutside(
      <AxisField label="Name" help="Letters and digits." error="Enter a name.">
        <Input value="" onChange={() => {}} />
      </AxisField>,
    )

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(screen.getByText('Enter a name.')).toBeVisible()
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveClass('ant-input-status-error')
    expect(input).toHaveAccessibleDescription('Enter a name. Letters and digits.')
  })

  it('marks no input and draws no error while the field has none', () => {
    renderOutside(
      <AxisField label="Name" help="Letters and digits.">
        <Input value="" onChange={() => {}} />
      </AxisField>,
    )

    const input = screen.getByRole('textbox', { name: 'Name' })
    expect(input).not.toHaveAttribute('aria-invalid')
    expect(input).not.toHaveClass('ant-input-status-error')
    expect(input).toHaveAccessibleDescription('Letters and digits.')
  })

  it('takes the error and its mark away once the caller drops the error', () => {
    const field = (error?: string) => (
      <AxisField label="Name" help="Letters and digits." error={error}>
        <Input value="Orders" onChange={() => {}} />
      </AxisField>
    )
    const { rerender } = renderOutside(field('Use a shorter name.'))
    expect(screen.getByText('Use a shorter name.')).toBeVisible()

    rerender(outsideTree(field()))

    expect(screen.queryByText('Use a shorter name.')).toBeNull()
    expect(screen.getByRole('textbox', { name: 'Name' })).not.toHaveAttribute('aria-invalid')
  })
})

describe('AxisField input width', () => {
  // Each input, the role that finds it and the root that fills the field.
  const inputs = [
    {
      kind: 'a select',
      input: <Select options={[{ value: 'open', label: 'Open' }]} onChange={() => {}} />,
      role: 'combobox',
      root: '.ant-select',
    },
    { kind: 'a number box', input: <InputNumber onChange={() => {}} />, role: 'spinbutton', root: '.ant-input-number' },
    { kind: 'a text box', input: <Input onChange={() => {}} />, role: 'textbox', root: 'input' },
  ]

  it.each(inputs)('makes $kind as wide as the field inside a form', ({ input, role, root }) => {
    renderForm(
      <AxisField label="Status" help="Pick one." name="status">
        {input}
      </AxisField>,
    )

    expect(screen.getByRole(role, { name: 'Status' }).closest(root)).toHaveStyle({ width: '100%' })
  })

  it.each(inputs)('makes $kind as wide as the field outside a form', ({ input, role, root }) => {
    renderOutside(
      <AxisField label="Status" help="Pick one.">
        {input}
      </AxisField>,
    )

    expect(screen.getByRole(role, { name: 'Status' }).closest(root)).toHaveStyle({ width: '100%' })
  })
})
