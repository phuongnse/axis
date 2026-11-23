import { ConfigProvider, Form } from 'antd'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisChoiceField } from './AxisChoiceField'
import { designTokensFor } from './designTokens'
import { requiredMark } from './requiredMark'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

const options = [
  { value: 'small', label: 'Small', help: 'Up to ten people.' },
  { value: 'large', label: 'Large', help: 'Ten people or more.', mark: 'Popular' },
  { value: 'none', label: 'None', help: '' },
]

function renderForm(field: React.ReactNode, finish = vi.fn()) {
  render(
    <ThemeModeProvider initialMode="light">
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <Form layout="vertical" requiredMark={requiredMark} onFinish={finish}>
            {field}
            <button type="submit">Submit</button>
          </Form>
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
  return finish
}

describe('AxisChoiceField', () => {
  it('draws each option with its help, which is the radio description through aria-describedby', () => {
    renderForm(<AxisChoiceField label="Size" help="Pick one." name="size" options={options} />)

    for (const option of options.slice(0, 2)) {
      const radio = screen.getByRole('radio', { name: new RegExp(option.label) })
      const help = screen.getByText(option.help)
      expect(radio).toHaveAttribute('aria-describedby', help.id)
      expect(radio).toHaveAccessibleDescription(option.help)
    }
    expect(screen.getByText('Popular')).toBeInTheDocument()
  })

  it('adds no description to an option without help', () => {
    renderForm(<AxisChoiceField label="Size" help="Pick one." name="size" options={options} />)

    expect(screen.getByRole('radio', { name: 'None' })).not.toHaveAttribute('aria-describedby')
  })

  it('sends the chosen value, starting at its initial one', async () => {
    const finish = renderForm(
      <AxisChoiceField label="Size" help="Pick one." name="size" options={options} initialValue="small" />,
    )
    expect(screen.getByRole('radio', { name: /Small/ })).toBeChecked()

    await userEvent.click(screen.getByRole('radio', { name: /Large/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(finish).toHaveBeenCalledWith({ size: 'large' }))
  })

  it('refuses a submit with nothing chosen when it is required', async () => {
    const finish = renderForm(
      <AxisChoiceField label="Size" help="Pick one." name="size" options={options} requiredMessage="Choose a size." />,
    )
    expect(screen.getByText('*')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    expect(await screen.findByText('Choose a size.')).toBeInTheDocument()
    expect(finish).not.toHaveBeenCalled()
  })

  it('draws the value the caller holds, with no form, and tells the screen the option clicked', async () => {
    const onChange = vi.fn()
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisChoiceField label="Size" help="Pick one." options={options} value="small" onChange={onChange} />
        </TextProvider>
      </ThemeModeProvider>,
    )
    expect(screen.getByRole('radiogroup', { name: 'Size' })).toHaveAccessibleDescription('Pick one.')
    expect(screen.getByRole('radio', { name: /Small/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /Large/ })).not.toBeChecked()

    await userEvent.click(screen.getByRole('radio', { name: /Large/ }))

    expect(onChange).toHaveBeenCalledWith('large')
  })

  it('gives each choice the caller holds a radio group name of its own, so two never join', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisChoiceField label="Size" help="" options={options} value="small" onChange={vi.fn()} />
          <AxisChoiceField label="Plan" help="" options={options} value="large" onChange={vi.fn()} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const size = within(screen.getByRole('radiogroup', { name: 'Size' })).getAllByRole('radio')
    const plan = within(screen.getByRole('radiogroup', { name: 'Plan' })).getAllByRole('radio')
    expect(new Set(size.map((radio) => radio.getAttribute('name'))).size).toBe(1)
    expect(size[0].getAttribute('name')).not.toBe('')
    expect(size[0].getAttribute('name')).not.toBe(plan[0].getAttribute('name'))
    expect(size[0]).toBeChecked()
    expect(plan[1]).toBeChecked()
  })

  it('marks a required choice the caller holds with * and aria-required on its group', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisChoiceField label="Size" help="" options={options} value={undefined} onChange={vi.fn()} required />
          <AxisChoiceField label="Plan" help="" options={options} value={undefined} onChange={vi.fn()} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('radiogroup', { name: 'Size' })).toHaveAttribute('aria-required', 'true')
    expect(screen.getByRole('radiogroup', { name: 'Plan' })).not.toHaveAttribute('aria-required')
    expect(screen.getAllByText('*')).toHaveLength(1)
  })

  it('refuses a name together with value or onChange, and required in a form', () => {
    expect([
      // @ts-expect-error A field with a `name` is in a form, which holds the value, so it takes no `value`.
      <AxisChoiceField key="value" label="Size" help="" name="size" options={options} value="small" />,
      // @ts-expect-error A field with a `name` is in a form, which holds the value, so it takes no `onChange`.
      <AxisChoiceField key="change" label="Size" help="" name="size" options={options} onChange={() => {}} />,
      // @ts-expect-error A field with a `name` is marked required by its `requiredMessage`, not by `required`.
      <AxisChoiceField key="required" label="Size" help="" name="size" options={options} required />,
    ]).toHaveLength(3)
  })

  it('requires help', () => {
    expect(
      // @ts-expect-error Every platform field has help.
      <AxisChoiceField label="Size" name="size" options={options} />,
    ).toBeDefined()
    expect(
      // @ts-expect-error Every platform field has help, also when the caller holds the value.
      <AxisChoiceField label="Size" options={options} value="small" onChange={vi.fn()} />,
    ).toBeDefined()
  })

  it('draws each unticked radio of a field with an error in dangerText, level with the first line', async () => {
    renderForm(
      <AxisChoiceField label="Size" help="Pick one." name="size" options={options} requiredMessage="Choose a size." />,
    )
    const icons = screen.getAllByRole('radio').map((radio) => radio.closest<HTMLElement>('.ant-radio'))
    expect(icons.every((icon) => icon?.style.borderColor === '')).toBe(true)

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    for (const icon of icons) {
      await waitFor(() => expect(icon).toHaveStyle({ borderColor: designTokensFor('light').colour.dangerText }))
      expect(icon).toHaveStyle({ alignSelf: 'flex-start' })
    }

    await userEvent.click(screen.getByRole('radio', { name: /Large/ }))

    await waitFor(() => expect(icons.every((icon) => icon?.style.borderColor === '')).toBe(true))
  })

  it('keeps the width of the radio group in a form, so a click beside an option does not pick it', () => {
    renderForm(<AxisChoiceField label="Size" help="Pick one." name="size" options={options} />)

    expect(screen.getByRole('radiogroup', { name: 'Size' }).closest('.ant-radio-group')).not.toHaveStyle({
      width: '100%',
    })
  })

  it('keeps the own look of the ticked radio in a field the server refused', () => {
    renderForm(
      <AxisChoiceField
        label="Size"
        help=""
        name="size"
        options={options}
        initialValue="small"
        status={{ state: 'refused', message: 'Refused.' }}
      />,
    )

    const border = (name: RegExp) =>
      screen.getByRole('radio', { name }).closest<HTMLElement>('.ant-radio')?.style.borderColor
    expect(border(/Small/)).toBe('')
    expect(border(/Large/)).not.toBe('')
    expect(border(/None/)).not.toBe('')
  })
})
