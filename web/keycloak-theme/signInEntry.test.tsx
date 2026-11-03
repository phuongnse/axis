import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  identityAddAuthenticatorContext,
  identityErrorContext,
  identityErrorMessage,
  identityFrontChannelSignOutContext,
  identityNoticeContext,
  identityPageExpiredContext,
  identityQrCode,
  identityResetPasswordContext,
  identitySamlPostContext,
  identitySignInContext,
  identitySignOutContext,
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

  it('draws the same sign-out page as the identitySignIn preview', () => {
    const expectSignOutPage = (container: HTMLElement) => {
      const form = container.querySelector('form') as HTMLFormElement
      expect(form).toHaveAttribute('action', identitySignOutContext.action)
      expect(form).toHaveAttribute('method', 'post')
      expect(form.querySelector('input[name="session_code"]')).toHaveValue(identitySignOutContext.sessionCode)
      expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    }
    writeContextBlock(identitySignOutContext)
    const theme = render(<SignInEntry context={readSignInContext(document)} />)
    expectSignOutPage(theme.container)
    cleanup()

    const preview = render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[7].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectSignOutPage(preview.container)
  })

  it('draws the same notice page as the identitySignIn preview', () => {
    const expectNoticePage = () => {
      expect(screen.getByText(identityNoticeContext.message)).toBeInTheDocument()
      expect(screen.getByRole('link', { name: identityNoticeContext.linkText })).toHaveAttribute(
        'href',
        identityNoticeContext.linkUrl,
      )
    }
    writeContextBlock(identityNoticeContext)
    render(<SignInEntry context={readSignInContext(document)} />)
    expectNoticePage()
    cleanup()

    render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[8].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expectNoticePage()
  })

  it('draws the same error page as the identitySignIn preview', () => {
    writeContextBlock(identityErrorContext)
    render(<SignInEntry context={readSignInContext(document)} />)
    expect(screen.getByRole('alert')).toHaveTextContent(identityErrorMessage)
    cleanup()

    render(
      <ThemeModeProvider>
        <MemoryRouter>{previews.identitySignIn[9].draw()}</MemoryRouter>
      </ThemeModeProvider>,
    )
    expect(screen.getByRole('alert')).toHaveTextContent(identityErrorMessage)
  })

  it('draws the expired page with Continue and Start again', () => {
    writeContextBlock(identityPageExpiredContext)
    render(<SignInEntry context={readSignInContext(document)} />)

    expect(screen.getByRole('link', { name: 'Continue' })).toHaveAttribute(
      'href',
      identityPageExpiredContext.continueUrl,
    )
    expect(screen.getByRole('link', { name: 'Start again' })).toHaveAttribute(
      'href',
      identityPageExpiredContext.restartUrl,
    )
  })

  it('draws the SAML redirect and posts its form', () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    writeContextBlock(identitySamlPostContext)
    const { container } = render(<SignInEntry context={readSignInContext(document)} />)
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('action', identitySamlPostContext.action)
    expect(form.querySelector('input[name="SAMLResponse"]')).toHaveValue(identitySamlPostContext.samlResponse)
    expect(submit).toHaveBeenCalledTimes(1)
    submit.mockRestore()
  })

  it('draws the front-channel sign-out with a hidden frame per client', () => {
    const replace = vi.fn()
    const location = vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, replace })
    writeContextBlock(identityFrontChannelSignOutContext)
    const { container } = render(<SignInEntry context={readSignInContext(document)} />)

    expect(container.querySelectorAll('iframe[hidden]')).toHaveLength(identityFrontChannelSignOutContext.clients.length)
    expect(replace).not.toHaveBeenCalled()
    location.mockRestore()
  })

  it('fails with a clear error when the sign-in context is missing', () => {
    expect(() => readSignInContext(document)).toThrow('The sign-in context #axis-sign-in-context is missing.')
  })
})
