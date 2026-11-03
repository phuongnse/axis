import { render, screen } from '@testing-library/react'
import { StrictMode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identitySamlPostContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { SamlPostPage } from './SamlPostPage'
import type { SamlPostContext } from './signInContext'

const context: SamlPostContext = {
  ...identitySamlPostContext,
  realmName: 'Contoso',
  action: 'https://sp.example/acs',
  samlResponse: 'response',
  relayState: 'relay',
}

describe('SamlPostPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('posts its form once on mount, even in StrictMode', () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})

    render(
      <StrictMode>
        <ThemeModeProvider>
          <SamlPostPage context={context} />
        </ThemeModeProvider>
      </StrictMode>,
    )

    expect(submit).toHaveBeenCalledTimes(1)
  })

  it('holds only the hidden fields that Keycloak gave', () => {
    vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})

    const { container } = render(<SamlPostPage context={context} />, { wrapper: ThemeModeProvider })
    const form = container.querySelector('form') as HTMLFormElement

    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', 'https://sp.example/acs')
    expect(form.querySelector('input[name="SAMLResponse"]')).toHaveValue('response')
    expect(form.querySelector('input[name="RelayState"]')).toHaveValue('relay')
    expect(form.querySelector('input[name="SAMLRequest"]')).not.toBeInTheDocument()
  })

  it('shows the title, the organization and a Continue button', () => {
    vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})

    render(<SamlPostPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Redirecting')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue' })).toHaveAttribute('type', 'submit')
  })
})
