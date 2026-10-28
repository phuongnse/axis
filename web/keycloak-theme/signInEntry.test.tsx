import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it } from 'vitest'
import { identitySignInContext } from '../src/designSystem/fixtures'
import { previews } from '../src/designSystem/previews'
import { ThemeModeProvider } from '../src/platform/theme'
import { readSignInContext } from './readSignInContext'
import { SignInEntry } from './signInEntry'

function writeContextBlock() {
  const block = document.createElement('script')
  block.type = 'application/json'
  block.id = 'axis-sign-in-context'
  block.textContent = JSON.stringify(identitySignInContext)
  document.body.append(block)
}

function expectSignInPage(container: HTMLElement) {
  expect(screen.getByRole('button', { name: 'Sign in with Contoso ID' })).toBeInTheDocument()
  const form = container.querySelector('form') as HTMLFormElement
  expect(form).toHaveAttribute('action', identitySignInContext.loginAction)
  expect(form).toHaveAttribute('method', 'post')
  expect(form.querySelector('input[name="username"]')).toHaveValue(identitySignInContext.username)
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

  it('fails with a clear error when the sign-in context is missing', () => {
    expect(() => readSignInContext(document)).toThrow('The sign-in context #axis-sign-in-context is missing.')
  })
})
