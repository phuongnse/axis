import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { identitySelectAuthenticatorContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { SelectAuthenticatorPage } from './SelectAuthenticatorPage'
import type { SelectAuthenticatorContext } from './signInContext'

const context: SelectAuthenticatorContext = {
  ...identitySelectAuthenticatorContext,
  realmName: 'Contoso',
  loginAction: '/choose?code=1',
  restartUrl: '/restart?code=1',
}

describe('SelectAuthenticatorPage', () => {
  it('shows the title, the organization and one row per method with its help line', () => {
    const { container } = render(<SelectAuthenticatorPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Choose how to sign in')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(container.querySelectorAll('form button')).toHaveLength(context.methods.length)
    expect(screen.getByRole('button', { name: /Authenticator app/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Password/ })).toBeInTheDocument()
    expect(screen.getByText('Sign in with your password.')).toBeInTheDocument()
  })

  it('posts the chosen method as authenticationExecution to the login action as a real form', () => {
    const { container } = render(<SelectAuthenticatorPage context={context} />, { wrapper: ThemeModeProvider })
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/choose?code=1')
    const buttons = [...form.querySelectorAll('button')]
    expect(buttons.map((button) => button.getAttribute('name'))).toEqual(
      context.methods.map(() => 'authenticationExecution'),
    )
    expect(buttons.map((button) => button.getAttribute('value'))).toEqual(context.methods.map((method) => method.id))
    for (const button of buttons) {
      expect(button).toHaveAttribute('type', 'submit')
    }
  })

  it('links "Back to sign in" to the restart address', () => {
    render(<SelectAuthenticatorPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute('href', '/restart?code=1')
  })

  it('shows a method without a help line', () => {
    render(<SelectAuthenticatorPage context={{ ...context, methods: [{ id: 'x', label: 'Security key' }] }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.getByRole('button', { name: 'Security key' })).toHaveAttribute('value', 'x')
  })

  it('shows an error above the methods', () => {
    render(<SelectAuthenticatorPage context={{ ...context, error: 'Something went wrong.' }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong.')
  })

  it('shows no alert without an error', () => {
    render(<SelectAuthenticatorPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
