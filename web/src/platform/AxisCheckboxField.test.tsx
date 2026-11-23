import { ConfigProvider, Form } from 'antd'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AxisCheckboxField } from './AxisCheckboxField'
import { designTokensFor } from './designTokens'
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

  it('draws a checkbox the caller holds, with no form, and tells the screen the new state', async () => {
    const onChange = vi.fn()
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisCheckboxField label="Active" help="Turn it on." checked={false} onChange={onChange} />
          <AxisCheckboxField label="Visible" help="Show it." checked onChange={vi.fn()} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('checkbox', { name: 'Visible' })).toBeChecked()
    const box = screen.getByRole('checkbox', { name: 'Active' })
    expect(box).not.toBeChecked()
    expect(box).toHaveAccessibleDescription('Turn it on.')

    await userEvent.click(box)

    expect(onChange).toHaveBeenCalledWith(true)
  })

  it('marks a required box the caller holds with * and aria-required, and an unmarked one with neither', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisCheckboxField label="Active" help="Turn it on." checked={false} onChange={vi.fn()} required />
          <AxisCheckboxField label="Visible" help="Show it." checked={false} onChange={vi.fn()} />
        </TextProvider>
      </ThemeModeProvider>,
    )

    expect(screen.getByRole('checkbox', { name: 'Active' })).toHaveAttribute('aria-required', 'true')
    expect(screen.getByRole('checkbox', { name: 'Visible' })).not.toHaveAttribute('aria-required')
    expect(screen.getAllByText('*')).toHaveLength(1)
  })

  it('refuses a name together with checked or onChange, and required in a form', () => {
    expect([
      // @ts-expect-error A field with a `name` is in a form, which holds the value, so it takes no `checked`.
      <AxisCheckboxField key="checked" label="Active" help="" name="active" checked />,
      // @ts-expect-error A field with a `name` is in a form, which holds the value, so it takes no `onChange`.
      <AxisCheckboxField key="change" label="Active" help="" name="active" onChange={() => {}} />,
      // @ts-expect-error A field with a `name` is marked required by its `requiredMessage`, not by `required`.
      <AxisCheckboxField key="required" label="Active" help="" name="active" required />,
    ]).toHaveLength(3)
  })

  it('does not take a click when disabled', () => {
    renderForm(<AxisCheckboxField label="Active" help="" name="active" disabled />)

    expect(screen.getByRole('checkbox', { name: 'Active' })).toBeDisabled()
  })

  it('requires help', () => {
    expect(
      // @ts-expect-error Every platform field has help.
      <AxisCheckboxField label="Active" name="active" />,
    ).toBeDefined()
    expect(
      // @ts-expect-error Every platform field has help, also when the caller holds the value.
      <AxisCheckboxField label="Active" checked onChange={vi.fn()} />,
    ).toBeDefined()
  })

  it('names a switch by a label hidden from the eye', () => {
    render(
      <ThemeModeProvider initialMode="light">
        <TextProvider catalogs={catalogs} development={false}>
          <AxisCheckboxField
            kind="switch"
            label="Mentions: In the application"
            help=""
            hideLabel
            checked
            onChange={vi.fn()}
          />
        </TextProvider>
      </ThemeModeProvider>,
    )

    const toggle = screen.getByRole('switch', { name: 'Mentions: In the application' })
    expect(toggle).toBeChecked()
    expect(screen.getByText('Mentions: In the application')).toHaveStyle({ position: 'absolute', width: '1px' })
  })

  it('draws an unticked box in a field with an error in dangerText, level with the first line, until it is ticked', async () => {
    renderForm(
      <AxisCheckboxField
        label="Active"
        help="An inactive item is hidden."
        name="active"
        requiredMessage="Tick Active."
      />,
    )
    const box = screen.getByRole('checkbox', { name: 'Active' })
    const icon = box.closest<HTMLElement>('.ant-checkbox')
    expect(icon?.style.borderColor).toBe('')

    await userEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => expect(icon).toHaveStyle({ borderColor: designTokensFor('light').colour.dangerText }))
    expect(icon).toHaveStyle({ alignSelf: 'flex-start' })

    await userEvent.click(box)

    await waitFor(() => expect(icon?.style.borderColor).toBe(''))
  })

  it.each([
    { kind: 'checkbox', props: {}, role: 'checkbox', root: '.ant-checkbox-wrapper' },
    { kind: 'switch', props: { kind: 'switch' as const }, role: 'switch', root: '.ant-switch' },
  ])('keeps the width of a $kind in a form, so a click beside its label does not tick it', ({ props, role, root }) => {
    renderForm(<AxisCheckboxField label="Active" help="An inactive item is hidden." name="active" {...props} />)

    expect(screen.getByRole(role, { name: 'Active' }).closest(root)).not.toHaveStyle({ width: '100%' })
  })

  it('keeps the own look of a ticked box in a field the server refused', () => {
    renderForm(
      <AxisCheckboxField
        label="Active"
        help=""
        name="active"
        initialValue
        status={{ state: 'refused', message: 'Refused.' }}
      />,
    )

    expect(
      screen.getByRole('checkbox', { name: 'Active' }).closest<HTMLElement>('.ant-checkbox')?.style.borderColor,
    ).toBe('')
  })
})
