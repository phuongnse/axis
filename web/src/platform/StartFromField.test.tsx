import { Button, Form } from 'antd'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { StartFromField } from './StartFromField'
import { ThemeModeProvider } from './theme'

const choices = [
  { id: 'crm', title: 'CRM', description: 'Track customers and deals.', mark: 'Built in' },
  { id: 'helpdesk', title: 'Help desk', description: 'Answer tickets.' },
]

function renderField(given: Parameters<typeof StartFromField>[0]['choices'], finish = vi.fn()) {
  const { container } = render(
    <Form onFinish={finish}>
      <StartFromField
        name="template"
        label="Start from"
        help="Pick Blank or a template."
        blank="Blank"
        choices={given}
        failedMessage="The templates could not be loaded. You can start from Blank."
      />
      <Button htmlType="submit">Send</Button>
    </Form>,
    { wrapper: ThemeModeProvider },
  )
  return { container, finish }
}

describe('StartFromField', () => {
  it('lists Blank first and chosen, then each choice with its title and description', () => {
    renderField(choices)

    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(3)
    expect(radios[0]).toBeChecked()
    expect(radios[1]).not.toBeChecked()
    expect(radios[2]).not.toBeChecked()
    expect(radios[0].closest('label')).toHaveTextContent(/^Blank$/)
    expect(radios[1].closest('label')).toHaveTextContent('CRMBuilt inTrack customers and deals.')
    expect(radios[2].closest('label')).toHaveTextContent(/^Help deskAnswer tickets\.$/)
  })

  it('shows a choice mark in its accessible name after its title, and none for a choice without one', () => {
    renderField(choices)

    expect(screen.getByRole('radio', { name: 'CRMBuilt in Track customers and deals.' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Help desk Answer tickets.' })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: /Help desk\s*Built in/ })).not.toBeInTheDocument()
  })

  it('offers only Blank, chosen, while the list loads', () => {
    renderField(undefined)

    expect(screen.getAllByRole('radio')).toHaveLength(1)
    expect(screen.getByRole('radio', { name: 'Blank' })).toBeChecked()
  })

  it('warns under the choices when the list did not load, and keeps Blank as the only choice, chosen', () => {
    const { container } = renderField('failed')

    expect(screen.getByText('The templates could not be loaded. You can start from Blank.')).toBeInTheDocument()
    expect(container.querySelector('.ant-form-item-has-warning')).toBeInTheDocument()
    expect(screen.getAllByRole('radio')).toHaveLength(1)
    expect(screen.getByRole('radio', { name: 'Blank' })).toBeChecked()
  })

  it('finishes the form with an empty value for Blank, and with the id of a chosen choice', async () => {
    const { finish } = renderField(choices)

    await userEvent.click(screen.getByRole('button', { name: 'Send' }))
    await waitFor(() => expect(finish).toHaveBeenCalledTimes(1))
    expect(finish.mock.calls[0][0]).toEqual({ template: '' })

    await userEvent.click(screen.getByRole('radio', { name: /^Help desk/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))
    await waitFor(() => expect(finish).toHaveBeenCalledTimes(2))
    expect(finish.mock.calls[1][0]).toEqual({ template: 'helpdesk' })
  })
})
