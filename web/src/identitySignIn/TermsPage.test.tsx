import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identityUpdatePasswordContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import type { TermsContext } from './signInContext'
import { TermsPage } from './TermsPage'

const context: TermsContext = {
  page: 'terms',
  title: 'Accept the terms',
  realmName: 'Contoso',
  locale: identityUpdatePasswordContext.locale,
  locales: identityUpdatePasswordContext.locales,
  frameTexts: identityUpdatePasswordContext.frameTexts,
  legal: identityUpdatePasswordContext.legal,
  loginAction: '/terms?code=1',
  termsUrl: 'https://contoso.test/terms',
  privacyUrl: 'https://contoso.test/privacy',
  texts: {
    title: 'Accept the terms',
    accept: 'I accept the {terms} and the {privacy}.',
    acceptTermsOnly: 'I accept the {terms}.',
    termsOfService: 'Terms of service',
    privacyPolicy: 'Privacy policy',
    required: 'Accept the terms to continue.',
    continue: 'Continue',
    decline: 'Decline and sign out',
  },
}

describe('TermsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('names the Terms of service and the Privacy policy as links that open in a new tab', () => {
    render(<TermsPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('checkbox')).not.toBeChecked()
    const terms = screen.getByRole('link', { name: 'Terms of service' })
    expect(terms).toHaveAttribute('href', 'https://contoso.test/terms')
    expect(terms).toHaveAttribute('target', '_blank')
    expect(terms).toHaveAttribute('rel', 'noopener noreferrer')
    const privacy = screen.getByRole('link', { name: 'Privacy policy' })
    expect(privacy).toHaveAttribute('href', 'https://contoso.test/privacy')
    expect(privacy).toHaveAttribute('target', '_blank')
    expect(screen.getByText('Contoso')).toBeInTheDocument()
  })

  it('names only the Terms of service when the tenant has no privacy policy', () => {
    render(<TermsPage context={{ ...context, privacyUrl: undefined }} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Terms of service' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Privacy policy' })).not.toBeInTheDocument()
    expect(screen.queryByText(/Privacy policy/)).not.toBeInTheDocument()
    expect(screen.getByText(/I accept the/)).toHaveTextContent('I accept the Terms of service.')
  })

  it('marks the checkbox with the required error and posts nothing when Continue is pressed unticked', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    const { container } = render(<TermsPage context={context} />, { wrapper: ThemeModeProvider })

    await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

    await waitFor(() => expect(screen.getByText('Accept the terms to continue.')).toBeInTheDocument())
    expect(container.querySelector('.ant-form-item-has-error')).toBeInTheDocument()
    expect(submit).not.toHaveBeenCalled()
  })

  it('posts accept to the login action once the box is ticked', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    const { container } = render(<TermsPage context={context} />, { wrapper: ThemeModeProvider })
    const form = container.querySelector('form') as HTMLFormElement

    await userEvent.click(screen.getByRole('checkbox'))
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(1))
    expect(submit.mock.contexts[0]).toBe(form)
    expect(form).toHaveAttribute('method', 'post')
    expect(form).toHaveAttribute('action', '/terms?code=1')
    expect(form.querySelector('input[type="hidden"][name="accept"]')).toHaveValue('true')
    expect(screen.queryByText('Accept the terms to continue.')).not.toBeInTheDocument()
  })

  it('posts cancel to the login action from its own form', () => {
    const { container } = render(<TermsPage context={context} />, { wrapper: ThemeModeProvider })
    const forms = container.querySelectorAll('form')

    expect(forms).toHaveLength(2)
    expect(forms[1]).toHaveAttribute('method', 'post')
    expect(forms[1]).toHaveAttribute('action', '/terms?code=1')
    expect(forms[1].querySelector('input[name="cancel"]')).toHaveValue('true')
    expect(forms[1].querySelector('input[name="accept"]')).toBeNull()
    expect(screen.getByRole('button', { name: 'Decline and sign out' })).toHaveAttribute('type', 'submit')
  })

  it('shows an error above the form', () => {
    render(<TermsPage context={{ ...context, error: 'Something failed.' }} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('alert')).toHaveTextContent('Something failed.')
  })
})
