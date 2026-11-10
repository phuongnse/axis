import { Button, Form } from 'antd'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { AcceptTermsField } from './AcceptTermsField'
import { ThemeModeProvider } from './theme'

function renderField(documents: Parameters<typeof AcceptTermsField>[0]['documents'], finish = vi.fn()) {
  const { container } = render(
    <Form onFinish={finish}>
      <AcceptTermsField
        name="accept"
        text="I accept the {terms} and the {privacy}."
        documents={documents}
        requiredMessage="Accept the terms to continue."
      />
      <Button htmlType="submit">Send</Button>
    </Form>,
    { wrapper: ThemeModeProvider },
  )
  return { container, finish }
}

const both = {
  terms: { label: 'Terms of service', href: 'https://contoso.test/terms' },
  privacy: { label: 'Privacy policy', href: 'https://contoso.test/privacy' },
}

describe('AcceptTermsField', () => {
  it('starts unticked and names each document as a link that opens in a new tab', () => {
    renderField(both)

    expect(screen.getByRole('checkbox')).not.toBeChecked()
    const terms = screen.getByRole('link', { name: 'Terms of service' })
    expect(terms).toHaveAttribute('href', 'https://contoso.test/terms')
    expect(terms).toHaveAttribute('target', '_blank')
    expect(terms).toHaveAttribute('rel', 'noopener noreferrer')
    const privacy = screen.getByRole('link', { name: 'Privacy policy' })
    expect(privacy).toHaveAttribute('href', 'https://contoso.test/privacy')
    expect(privacy).toHaveAttribute('target', '_blank')
    expect(privacy).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('marks itself with its message and does not finish the form when it is unticked', async () => {
    const { container, finish } = renderField(both)

    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.getByText('Accept the terms to continue.')).toBeInTheDocument())
    expect(container.querySelector('.ant-form-item-has-error')).toBeInTheDocument()
    expect(finish).not.toHaveBeenCalled()
  })

  it('finishes the form once ticked', async () => {
    const { finish } = renderField(both)

    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(finish).toHaveBeenCalledTimes(1))
    expect(finish.mock.calls[0][0]).toEqual({ accept: true })
    expect(screen.queryByText('Accept the terms to continue.')).not.toBeInTheDocument()
  })

  it('shows a document with no address as plain text', () => {
    renderField({ ...both, privacy: { label: 'Privacy policy' } })

    expect(screen.getByRole('link', { name: 'Terms of service' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Privacy policy' })).not.toBeInTheDocument()
    expect(screen.getByText(/I accept the/)).toHaveTextContent('I accept the Terms of service and the Privacy policy.')
  })
})
