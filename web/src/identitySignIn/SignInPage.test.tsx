import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identityFrameTexts } from '../designSystem/fixtures'
import { designTokensFor } from '../platform/designTokens'
import { ThemeModeProvider } from '../platform/theme'
import { SignInPage } from './SignInPage'
import type { SignInContext } from './signInContext'

const context: SignInContext = {
  page: 'signIn',
  title: 'Sign in',
  realmName: 'Contoso',
  providers: [{ alias: 'contoso', label: 'Contoso ID', loginUrl: '/broker/contoso' }],
  accounts: true,
  username: 'alex.morgan@contoso.example',
  loginAction: '/realms/contoso/login-actions/authenticate',
  rememberMe: true,
  forgotPasswordUrl: '/realms/contoso/reset-credentials',
  locale: 'en',
  locales: [
    { tag: 'en', label: 'English', url: '/en' },
    { tag: 'vi', label: 'Tiếng Việt', url: '/vi' },
  ],
  frameTexts: identityFrameTexts,
  texts: {
    continueWith: 'Continue with {provider}',
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

const twoProviders: SignInContext = {
  ...context,
  providers: [
    { alias: 'contoso', label: 'Contoso ID', loginUrl: '/broker/contoso' },
    { alias: 'fabrikam', label: 'Fabrikam ID', loginUrl: '/broker/fabrikam' },
  ],
}

function card(container: HTMLElement) {
  return container.querySelector('.ant-card') as HTMLElement
}

function form(container: HTMLElement) {
  return container.querySelector('form') as HTMLFormElement
}

describe('SignInPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("draws the organization's name beside a 32 px mark above the card", () => {
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    const heading = screen.getByTestId('organization-heading')
    expect(within(heading).getByText('Contoso', { exact: true })).toBeVisible()
    const mark = heading.querySelector('[aria-hidden="true"]')
    expect(mark).toHaveStyle({ width: '32px', height: '32px' })
  })

  it('draws Axis in the heading when the realm has no display name', () => {
    const { realmName: _realmName, ...unnamed } = context
    render(<SignInPage context={unnamed} />, { wrapper: ThemeModeProvider })

    expect(within(screen.getByTestId('organization-heading')).getByText('Axis', { exact: true })).toBeVisible()
  })

  it('shows the title "Sign in" without the organization\'s name under it', () => {
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
    const names = screen.queryAllByText('Contoso', { exact: true })
    expect(names).toHaveLength(1)
    expect(screen.getByTestId('organization-heading')).toContainElement(names[0])
  })

  it('draws the field labels at weight 500 in the muted colour', () => {
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    const style = { fontWeight: '500', color: designTokensFor('light').colour.textMuted }
    expect(screen.getByText('Email or username')).toHaveStyle(style)
    expect(screen.getByText('Password', { exact: true })).toHaveStyle(style)
  })

  it('puts "Forgot password?" on the row of the password label, outside the label', () => {
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    const label = screen.getByText('Password', { exact: true })
    const link = screen.getByRole('link', { name: 'Forgot password?' })
    expect(label.parentElement).toContainElement(link)
    expect(label).not.toContainElement(link)
    expect(screen.getByLabelText('Password', { exact: true })).toHaveAttribute('name', 'password')
  })

  it('draws every provider button as a secondary button, the lone one too', () => {
    const { rerender } = render(<SignInPage context={twoProviders} />, { wrapper: ThemeModeProvider })

    for (const button of screen.getAllByRole('button', { name: /^Continue with / })) {
      expect(button).toHaveClass('ant-btn-default')
    }
    rerender(<SignInPage context={{ ...context, accounts: false }} />)
    expect(screen.getByRole('button', { name: 'Continue with Contoso ID' })).toHaveClass('ant-btn-default')
  })

  it('lists the provider, then "or" and a form that posts to the login address', () => {
    const { container } = render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('button', { name: 'Continue with Contoso ID' })).toBeInTheDocument()
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
    const { container } = render(
      <SignInPage context={{ ...context, error: 'Invalid email, username or password.' }} />,
      { wrapper: ThemeModeProvider },
    )

    expect(screen.getByText('Invalid email, username or password.')).toBeInTheDocument()
    expect(form(container).querySelector('input[name=username]')).toHaveValue('alex.morgan@contoso.example')
  })

  it('shows only the provider button under a line that names it with one provider and no accounts', () => {
    const { container } = render(<SignInPage context={{ ...context, accounts: false }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.getByText('Your organization signs in only with Contoso ID.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue with Contoso ID' })).toBeInTheDocument()
    expect(screen.queryByText('or')).not.toBeInTheDocument()
    expect(container.querySelector('form')).toBeNull()
    expect(within(card(container)).getAllByRole('button')).toHaveLength(1)
  })

  it('lists only the providers that the site allows', () => {
    render(<SignInPage context={{ ...twoProviders, site: { providers: ['fabrikam'], accounts: true } }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.getByRole('button', { name: 'Continue with Fabrikam ID' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Continue with Contoso ID' })).not.toBeInTheDocument()
  })

  it('shows every provider and the form for a client that belongs to no site', () => {
    const { container } = render(<SignInPage context={twoProviders} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('button', { name: 'Continue with Contoso ID' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue with Fabrikam ID' })).toBeInTheDocument()
    expect(container.querySelector('form')).not.toBeNull()
  })

  it('shows no form and no "or" when the site allows no accounts, though the realm does', () => {
    const { container } = render(
      <SignInPage context={{ ...twoProviders, site: { providers: ['fabrikam'], accounts: false } }} />,
      { wrapper: ThemeModeProvider },
    )

    expect(container.querySelector('form')).toBeNull()
    expect(screen.queryByText('or')).not.toBeInTheDocument()
    expect(screen.getByText('Your organization signs in only with Fabrikam ID.')).toBeInTheDocument()
    expect(within(card(container)).getAllByRole('button')).toHaveLength(1)
  })

  it("shows Keycloak's notice in an info alert above the form", () => {
    const notice = 'If the email or username matches an account, we sent it a link to set a new password.'
    const { container } = render(<SignInPage context={{ ...context, notice }} />, { wrapper: ThemeModeProvider })

    const alert = screen.getByRole('alert')
    expect(alert).toHaveTextContent(notice)
    expect(alert).toHaveClass('ant-alert-info')
    expect(alert.compareDocumentPosition(form(container)) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('shows no alert without a notice or an error', () => {
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('leaves out the remember-me checkbox and the forgot-password link when the realm has none', () => {
    render(<SignInPage context={{ ...context, rememberMe: false, forgotPasswordUrl: undefined }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.queryByText('Keep me signed in')).not.toBeInTheDocument()
    expect(screen.queryByText('Forgot password?')).not.toBeInTheDocument()
  })

  it('submits the form natively once both fields are filled', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1))
  })

  it('does not submit with the password empty', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    render(<SignInPage context={context} />, { wrapper: ThemeModeProvider })

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Enter your password.')).toBeInTheDocument()
    expect(submit).not.toHaveBeenCalled()
  })
})
