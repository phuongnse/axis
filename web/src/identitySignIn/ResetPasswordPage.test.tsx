import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { identityResetPasswordContext } from '../designSystem/fixtures'
import { ResetPasswordPage } from './ResetPasswordPage'
import type { ResetPasswordContext } from './signInContext'

const context: ResetPasswordContext = {
  ...identityResetPasswordContext,
  realmName: 'Contoso',
  loginAction: '/reset?code=1',
  loginUrl: '/sign-in?client=axis',
}

describe('ResetPasswordPage', () => {
  it('asks for the email or username under the title and the organization', () => {
    render(<ResetPasswordPage context={context} />)

    expect(screen.getByText('Reset your password')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByLabelText('Email or username')).toHaveValue(context.username)
    expect(screen.getByRole('button', { name: 'Send link' })).toBeInTheDocument()
  })

  it('posts username to the login action as a real form', () => {
    const { container } = render(<ResetPasswordPage context={context} />)
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/reset?code=1')
    expect(form.querySelector('input[name="username"]')).toHaveValue(context.username)
  })

  it('links "Back to sign in" to the login address', () => {
    render(<ResetPasswordPage context={context} />)

    expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/sign-in?client=axis')
  })

  it('asks for the username before it posts', async () => {
    render(<ResetPasswordPage context={{ ...context, username: undefined }} />)

    await userEvent.click(screen.getByRole('button', { name: 'Send link' }))

    await waitFor(() => expect(screen.getByText(context.texts.usernameRequired)).toBeInTheDocument())
  })

  it('shows an error above the form', () => {
    render(<ResetPasswordPage context={{ ...context, error: 'Invalid username.' }} />)

    expect(screen.getByRole('alert')).toHaveTextContent('Invalid username.')
  })

  it('shows no alert without an error', () => {
    render(<ResetPasswordPage context={context} />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
