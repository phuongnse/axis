import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it } from 'vitest'
import {
  identityAddAuthenticatorContext,
  identityQrCode,
  identityResetPasswordContext,
  identitySignInContext,
  identityTwoStepContext,
  identityUpdatePasswordContext,
} from '../src/designSystem/fixtures'
import { previews } from '../src/designSystem/previews'
import type { IdentityContext } from '../src/identitySignIn/signInContext'
import { ThemeModeProvider } from '../src/platform/theme'
import { readSignInContext } from './readSignInContext'
import { SignInEntry } from './signInEntry'

function writeContextBlock(context: IdentityContext = identitySignInContext) {
  const block = document.createElement('script')
  block.type = 'application/json'
  block.id = 'axis-sign-in-context'
  block.textContent = JSON.stringify(context)
  document.body.append(block)
}

function expectSignInPage(container: HTMLElement) {
  expect(screen.getByRole('button', { name: 'Continue with Contoso ID' })).toBeInTheDocument()
  const form = container.querySelector('form') as HTMLFormElement
  expect(form).toHaveAttribute('action', identitySignInContext.loginAction)
  expect(form).toHaveAttribute('method', 'post')
  expect(form.querySelector('input[name="username"]')).toHaveValue(identitySignInContext.username)
}

function expectTwoStepPage(container: HTMLElement) {
  expect(screen.getByText('Two-step verification')).toBeInTheDocument()
  const form = container.querySelector('form') as HTMLFormElement
  expect(form).toHaveAttribute('action', identityTwoStepContext.loginAction)
  expect(form).toHaveAttribute('method', 'post')
  expect(form.querySelectorAll('input:not([type="hidden"])')).toHaveLength(6)
  expect(form.querySelector('input[name="otp"]')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute(
    'href',
    identityTwoStepContext.restartUrl,
  )
}

function expectResetPasswordPage(container: HTMLElement) {
  expect(screen.getByText('Reset your password')).toBeInTheDocument()
  const form = container.querySelector('form') as HTMLFormElement
  expect(form).toHaveAttribute('action', identityResetPasswordContext.loginAction)
  expect(form).toHaveAttribute('method', 'post')
  expect(form.querySelector('input[name="username"]')).toHaveValue(identityResetPasswordContext.username)
  expect(screen.getByRole('link', { name: 'Back to sign in' })).toHaveAttribute(
    'href',
    identityResetPasswordContext.loginUrl,
  )
}

function expectUpdatePasswordPage(container: HTMLElement) {
  expect(screen.getByText('Set your password')).toBeInTheDocument()
  const form = container.querySelector('form') as HTMLFormElement
  expect(form).toHaveAttribute('action', identityUpdatePasswordContext.loginAction)
  expect(form).toHaveAttribute('method', 'post')
  expect(form.querySelector('input[name="password-new"]')).toBeInTheDocument()
  expect(form.querySelector('input[name="password-confirm"]')).toBeInTheDocument()
  expect(form.querySelector('input[name="logout-sessions"]')).toHaveValue('on')
}

function expectAddAuthenticatorPage(container: HTMLElement) {
  expect(container.querySelector(`img[src="data:image/png;base64,${identityQrCode}"]`)).toBeInTheDocument()
  expect(screen.getByText(identityAddAuthenticatorContext.manualKey)).toBeInTheDocument()
  const form = container.querySelector('form') as HTMLFormElement
  expect(form).toHaveAttribute('action', identityAddAuthenticatorContext.loginAction)
  expect(form).toHaveAttribute('method', 'post')
  expect(form.querySelector('input[name="totp"]')).toBeInTheDocument()
  expect(form.querySelector('input[name="totpSecret"]')).toHaveValue(identityAddAuthenticatorContext.secret)
}

describe('the Keycloak theme entry', () => {
  afterEach(() => {
    document.getElementById('axis-sign-in-context')?.remove()
  })

  it('draws the same sign-in page as the identitySignIn preview', () => {
    writeContextBlock()
    const theme = render(<SignInEntry context={readSignInContext(document)} />)
    expectSignInPage(theme.container)
    cleanup()

    const preview = render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[0].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectSignInPage(preview.container)
  })

  it('draws the same two-step page as the identitySignIn preview', () => {
    writeContextBlock(identityTwoStepContext)
    const theme = render(<SignInEntry context={readSignInContext(document)} />)
    expectTwoStepPage(theme.container)
    cleanup()

    const preview = render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[3].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectTwoStepPage(preview.container)
  })

  it('draws the same forgot-password page as the identitySignIn preview', () => {
    writeContextBlock(identityResetPasswordContext)
    const theme = render(<SignInEntry context={readSignInContext(document)} />)
    expectResetPasswordPage(theme.container)
    cleanup()

    const preview = render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[4].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectResetPasswordPage(preview.container)
  })

  it('draws the same set-password page as the identitySignIn preview', () => {
    writeContextBlock(identityUpdatePasswordContext)
    const theme = render(<SignInEntry context={readSignInContext(document)} />)
    expectUpdatePasswordPage(theme.container)
    cleanup()

    const preview = render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[5].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectUpdatePasswordPage(preview.container)
  })

  it('draws the same add-an-authenticator page as the identitySignIn preview', () => {
    writeContextBlock(identityAddAuthenticatorContext)
    const theme = render(<SignInEntry context={readSignInContext(document)} />)
    expectAddAuthenticatorPage(theme.container)
    cleanup()

    const preview = render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[6].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectAddAuthenticatorPage(preview.container)
  })

  it('fails with a clear error when the sign-in context is missing', () => {
    expect(() => readSignInContext(document)).toThrow('The sign-in context #axis-sign-in-context is missing.')
  })
})
