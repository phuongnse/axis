import { ConfigProvider, Form } from 'antd'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisCheckboxField } from './AxisCheckboxField'
import { requiredMark } from './requiredMark'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import { hasFieldError, isRequiredLabel } from '../test/antd'

const texts = {}
const catalogs = [{ texts, fallbackTexts: texts }]

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

describe('AxisCheckboxField', () => {
  it('draws its label, then its help, then the checkbox, which the label names and the help describes', () => {
    renderForm(<AxisCheckboxField label="Active" help="An inactive item is hidden." name="active" />)

    const label = screen.getByText('Active')
    const help = screen.getByText('An inactive item is hidden.')
    const box = screen.getByRole('checkbox', { name: 'Active' })
    expect(label.compareDocumentPosition(help) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(help.compareDocumentPosition(box) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(box).toHaveAccessibleDescription('An inactive item is hidden.')
  })

  it('marks a required field with *, refuses an empty submit and drops the message once the box is ticked', async () => {
    const finish = renderForm(
      <AxisCheckboxField
        label="Active"
        help="An inactive item is hidden."
        name="active"
        requiredMessage="Tick Active."
      />,
    )
    const box = screen.getByRole('checkbox', { name: 'Active' })
    expect(isRequiredLabel(screen.getByText('Active'))).toBe(true)
    expect(screen.getByText('*')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    expect(await screen.findByText('Tick Active.')).toBeInTheDocument()
    expect(hasFieldError(box)).toBe(true)
    expect(finish).not.toHaveBeenCalled()

    await userEvent.click(box)

    await waitFor(() => expect(screen.queryByText('Tick Active.')).not.toBeInTheDocument())
  })

  it('starts at its initial value and sends the box state', async () => {
    const finish = renderForm(<AxisCheckboxField label="Active" help="" name="active" initialValue />)
    expect(screen.getByRole('checkbox', { name: 'Active' })).toBeChecked()

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(finish).toHaveBeenCalledWith({ active: true }))
  })

  it('draws a switch named by the label, which sends true once clicked', async () => {
    const finish = renderForm(<AxisCheckboxField label="Active" help="Turn it on." name="active" kind="switch" />)

    await userEvent.click(screen.getByRole('switch', { name: 'Active' }))
    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(finish).toHaveBeenCalledWith({ active: true }))
  })

  it('draws a switch the caller holds, named by the label and described by the help, with no form', async () => {
    const onChange = vi.fn()
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisCheckboxField kind="switch" label="Active" help="Turn it on." checked={false} onChange={onChange} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const toggle = screen.getByRole('switch', { name: 'Active' })
    expect(toggle).toHaveAccessibleDescription('Turn it on.')
    expect(toggle).not.toBeChecked()

    await userEvent.click(toggle)

    expect(onChange).toHaveBeenCalledWith(true)
  })

  it('draws a checkbox the caller holds, named by the label and described by the help, with no form', async () => {
    const onChange = vi.fn()
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisCheckboxField label="Active" help="An inactive item is hidden." checked={false} onChange={onChange} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const box = screen.getByRole('checkbox', { name: 'Active' })
    expect(box).toHaveAccessibleDescription('An inactive item is hidden.')
    expect(box).not.toBeChecked()

    await userEvent.click(box)

    expect(onChange).toHaveBeenCalledWith(true)
  })

  it('does not take a click when disabled', () => {
    renderForm(<AxisCheckboxField label="Active" help="" name="active" disabled />)

    expect(screen.getByRole('checkbox', { name: 'Active' })).toBeDisabled()
  })

  it('requires help in the mode where the caller holds the value', () => {
    expect(
      // @ts-expect-error Every platform field has help.
      <AxisCheckboxField label="Active" checked onChange={vi.fn()} />,
    ).toBeDefined()
  })

  it('refuses a name together with onChange', () => {
    expect(
      // @ts-expect-error A named field sits in a form, which holds the value.
      <AxisCheckboxField label="Active" help="An inactive item is hidden." name="active" onChange={vi.fn()} />,
    ).toBeDefined()
  })

  it('requires help', () => {
    expect(
      // @ts-expect-error Every platform field has help.
      <AxisCheckboxField label="Active" name="active" />,
    ).toBeDefined()
  })
})
