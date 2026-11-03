import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identityUpdateProfileContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import type { UpdateProfileContext } from './signInContext'
import { UpdateProfilePage } from './UpdateProfilePage'

const context: UpdateProfileContext = {
  ...identityUpdateProfileContext,
  realmName: 'Contoso',
  loginAction: '/update-profile?code=1',
}

describe('UpdateProfilePage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows one field per user profile field with its current value under the title', () => {
    render(<UpdateProfilePage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Update your profile')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveValue('alex.morgan@contoso.example')
    expect(screen.getByLabelText('Email')).toHaveAttribute('type', 'email')
    expect(screen.getByLabelText('First name')).toHaveValue('Alex')
    expect(screen.getByLabelText('First name')).toHaveAttribute('type', 'text')
    expect(screen.getByLabelText('Last name')).toHaveValue('Morgan')
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })

  it('posts each field under its attribute name to the login action as a real form', () => {
    const { container } = render(<UpdateProfilePage context={context} />, { wrapper: ThemeModeProvider })
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/update-profile?code=1')
    for (const field of context.fields) {
      expect(form.querySelector(`input[name="${field.name}"]`)).toHaveValue(field.value)
    }
    expect(form.querySelector('input[name="email"]')).toHaveAttribute('autocomplete', 'email')
  })

  it('draws the review title for the review profile page', () => {
    render(<UpdateProfilePage context={{ ...context, page: 'reviewProfile', title: 'Review your profile' }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.getByText('Review your profile')).toBeInTheDocument()
    expect(screen.queryByText('Update your profile')).not.toBeInTheDocument()
  })

  it('does not let a person edit a read-only field', () => {
    const fields = context.fields.map((field) => (field.name === 'email' ? { ...field, readOnly: true } : field))
    render(<UpdateProfilePage context={{ ...context, fields }} />, { wrapper: ThemeModeProvider })

    expect(screen.getByLabelText('Email')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('First name')).not.toHaveAttribute('readonly')
  })

  it("shows Keycloak's reminder as an info alert above the fields", () => {
    render(<UpdateProfilePage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('alert')).toHaveTextContent(
      'You need to update your user profile to activate your account.',
    )
  })

  it('shows a field error under its field and no alert', async () => {
    const fields = context.fields.map((field) =>
      field.name === 'email' ? { ...field, error: 'Invalid email address.' } : field,
    )
    const { container } = render(<UpdateProfilePage context={{ ...context, notice: undefined, fields }} />, {
      wrapper: ThemeModeProvider,
    })

    const item = container.querySelector('input[name="email"]')?.closest('.ant-form-item') as HTMLElement
    await waitFor(() => expect(within(item).getByText('Invalid email address.')).toBeInTheDocument())
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows an error that belongs to no field above the form', () => {
    render(<UpdateProfilePage context={{ ...context, notice: undefined, error: 'Something went wrong.' }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong.')
  })

  it('shows no alert without a reminder or an error', () => {
    render(<UpdateProfilePage context={{ ...context, notice: undefined }} />, { wrapper: ThemeModeProvider })

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('asks for a required field before it posts', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<UpdateProfilePage context={context} />, { wrapper: ThemeModeProvider })

    await userEvent.clear(screen.getByLabelText('First name'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.getByText('First name is required.')).toBeInTheDocument())
    expect(submit).not.toHaveBeenCalled()
  })

  it('posts when the fields are valid', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<UpdateProfilePage context={context} />, { wrapper: ThemeModeProvider })

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1))
  })
})
