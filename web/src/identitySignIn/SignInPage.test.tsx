import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SignInPage } from './SignInPage'
import type { SignInContext } from './signInContext'

const context: SignInContext = {
  title: 'Sign in to Axis',
  realmName: 'contoso',
  providers: [{ alias: 'contoso', label: 'Contoso ID', loginUrl: '/broker/contoso' }],
  accounts: true,
  username: 'alex.morgan@contoso.example',
  loginAction: '/realms/contoso/login-actions/authenticate',
  rememberMe: true,
  forgotPasswordUrl: '/realms/contoso/reset-credentials',
  locales: [
    { label: 'English', url: '/en' },
    { label: 'Tiếng Việt', url: '/vi' },
  ],
  texts: {
    signInWith: 'Sign in with {provider}',
    providerOnly: 'Your organization signs in only with {provider}.',
    or: 'or',
    username: 'Email or username',
    password: 'Password',
    usernameRequired: 'Enter your email or username.',
    passwordRequired: 'Enter your password.',
    forgotPassword: 'Forgot password?',
    keepSignedIn: 'Keep me signed in',
    signIn: 'Sign in',
  },
}

function form(container: HTMLElement) {
  return container.querySelector('form') as HTMLFormElement
}

describe('SignInPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('lists the provider, then "or" and a form that posts to the login address', () => {
    const { container } = render(<SignInPage context={context} />)

    expect(screen.getByRole('button', { name: 'Sign in with Contoso ID' })).toBeInTheDocument()
    expect(screen.getByText('or')).toBeInTheDocument()
    const element = form(container)
    expect(element.getAttribute('method')).toBe('post')
    expect(element.getAttribute('action')).toBe(context.loginAction)
    expect(element.querySelector('input[name=username]')).not.toBeNull()
    expect(element.querySelector('input[name=password]')).not.toBeNull()
    expect(element.querySelector('input[name=rememberMe]')).not.toBeNull()
    expect(element.querySelector('input[name=credentialId][type=hidden]')).toHaveValue('')
  })

  it('shows the error above the form and keeps the username', () => {
    const { container } = render(<SignInPage context={{ ...context, error: 'Invalid email, username or password.' }} />)

    expect(screen.getByText('Invalid email, username or password.')).toBeInTheDocument()
    expect(form(container).querySelector('input[name=username]')).toHaveValue('alex.morgan@contoso.example')
  })

  it('shows only the provider button under a line that names it with one provider and no accounts', () => {
    const { container } = render(<SignInPage context={{ ...context, accounts: false }} />)

    expect(screen.getByText('Your organization signs in only with Contoso ID.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in with Contoso ID' })).toBeInTheDocument()
    expect(screen.queryByText('or')).not.toBeInTheDocument()
    expect(container.querySelector('form')).toBeNull()
    expect(screen.getAllByRole('button')).toHaveLength(1)
  })

  it('leaves out the remember-me checkbox and the forgot-password link when the realm has none', () => {
    render(<SignInPage context={{ ...context, rememberMe: false, forgotPasswordUrl: undefined }} />)

    expect(screen.queryByText('Keep me signed in')).not.toBeInTheDocument()
    expect(screen.queryByText('Forgot password?')).not.toBeInTheDocument()
  })

  it('submits the form natively once both fields are filled', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<SignInPage context={context} />)

    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1))
  })

  it('does not submit with the password empty', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<SignInPage context={context} />)

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Enter your password.')).toBeInTheDocument()
    expect(submit).not.toHaveBeenCalled()
  })
})
