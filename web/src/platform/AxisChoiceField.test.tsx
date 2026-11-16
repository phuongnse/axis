import { ConfigProvider, Form } from 'antd'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisChoiceField } from './AxisChoiceField'
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

  it('draws a choice the caller holds, named by the label and described by the help, with no form', async () => {
    const onChange = vi.fn()
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisChoiceField label="Size" help="Pick one." options={options} value="small" onChange={onChange} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const group = screen.getByRole('radiogroup', { name: 'Size' })
    expect(group).toHaveAccessibleDescription('Pick one.')
    expect(screen.getByRole('radio', { name: /Small/ })).toBeChecked()

    await userEvent.click(screen.getByRole('radio', { name: /Large/ }))

    expect(onChange).toHaveBeenCalledWith('large')
  })

  it('requires help in the mode where the caller holds the value', () => {
    expect(
      // @ts-expect-error Every platform field has help.
      <AxisChoiceField label="Size" options={options} value="small" onChange={vi.fn()} />,
    ).toBeDefined()
  })

  it('refuses a name together with onChange', () => {
    expect(
      // @ts-expect-error A named field sits in a form, which holds the value.
      <AxisChoiceField label="Size" help="Pick one." name="size" options={options} onChange={vi.fn()} />,
    ).toBeDefined()
  })

  it('requires help', () => {
    expect(
      // @ts-expect-error Every platform field has help.
      <AxisChoiceField label="Size" name="size" options={options} />,
    ).toBeDefined()
  })
})
