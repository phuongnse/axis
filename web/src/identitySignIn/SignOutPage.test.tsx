import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { identitySignOutContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { SignOutPage } from './SignOutPage'
import type { SignOutContext } from './signInContext'

const context: SignOutContext = {
  ...identitySignOutContext,
  realmName: 'Contoso',
  action: '/logout?code=1',
  sessionCode: 'abc',
  backUrl: '/app',
}

describe('SignOutPage', () => {
  it('asks the question under the title and the organization', () => {
    render(<SignOutPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByText('Do you want to sign out?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
  })

  it('posts session_code to its action as a real form', () => {
    const { container } = render(<SignOutPage context={context} />, { wrapper: ThemeModeProvider })
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/logout?code=1')
    expect(form.querySelector('input[name="session_code"]')).toHaveValue('abc')
    expect(form.querySelector('input[name="confirmLogout"]')).toHaveValue('on')
  })

  it('links "Back to application" when Keycloak gives an address', () => {
    render(<SignOutPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Back to application' })).toHaveAttribute('href', '/app')
  })

  it('shows no link without an address', () => {
    render(<SignOutPage context={{ ...context, backUrl: undefined }} />, { wrapper: ThemeModeProvider })

    expect(screen.queryByRole('link', { name: 'Back to application' })).not.toBeInTheDocument()
  })
})
