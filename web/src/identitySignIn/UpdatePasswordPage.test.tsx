import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identityUpdatePasswordContext } from '../designSystem/fixtures'
import type { UpdatePasswordContext } from './signInContext'
import { UpdatePasswordPage } from './UpdatePasswordPage'

const context: UpdatePasswordContext = {
  ...identityUpdatePasswordContext,
  realmName: 'Contoso',
  loginAction: '/set-password?code=1',
}

describe('UpdatePasswordPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('asks for the new password twice under the title and the organization', () => {
    render(<UpdatePasswordPage context={context} />)

    expect(screen.getByText('Set your password')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByLabelText('New password')).toBeInTheDocument()
    expect(screen.getByLabelText('Confirm password')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Set password' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Back to sign in' })).not.toBeInTheDocument()
  })

  it('posts password-new, password-confirm and logout-sessions to the login action as a real form', () => {
    const { container } = render(<UpdatePasswordPage context={context} />)
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/set-password?code=1')
    expect(form.querySelector('input[name="password-new"]')).toHaveAttribute('type', 'password')
    expect(form.querySelector('input[name="password-confirm"]')).toHaveAttribute('type', 'password')
    expect(form.querySelector('input[name="logout-sessions"]')).toHaveValue('on')
  })

  it('posts when both passwords are filled and the same', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<UpdatePasswordPage context={context} />)

    await userEvent.type(screen.getByLabelText('New password'), 'S3cret!pass')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'S3cret!pass')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1))
  })

  it('shows that the passwords differ and does not post', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<UpdatePasswordPage context={context} />)

    await userEvent.type(screen.getByLabelText('New password'), 'S3cret!pass')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'Other!pass')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    await waitFor(() => expect(screen.getByText(context.texts.passwordsDiffer)).toBeInTheDocument())
    expect(submit).not.toHaveBeenCalled()
  })

  it('asks for the new password before it posts', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<UpdatePasswordPage context={context} />)

    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    await waitFor(() => expect(screen.getByText(context.texts.newPasswordRequired)).toBeInTheDocument())
    expect(submit).not.toHaveBeenCalled()
  })

  it('shows an error above the form', () => {
    render(<UpdatePasswordPage context={{ ...context, error: 'Invalid password: minimum length 8.' }} />)

    expect(screen.getByRole('alert')).toHaveTextContent('Invalid password: minimum length 8.')
  })

  it('shows no alert without an error', () => {
    render(<UpdatePasswordPage context={context} />)

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
