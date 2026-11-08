import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { identityUpdatePasswordContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { ConsentPage } from './ConsentPage'
import type { ConsentContext } from './signInContext'

const context: ConsentContext = {
  page: 'consent',
  title: 'Allow access',
  realmName: 'Contoso',
  locale: identityUpdatePasswordContext.locale,
  locales: identityUpdatePasswordContext.locales,
  frameTexts: identityUpdatePasswordContext.frameTexts,
  legal: identityUpdatePasswordContext.legal,
  oauthAction: '/consent?client=1',
  code: 'abc-123',
  clientName: 'Draft helper',
  homeUrl: 'https://helper.test/home',
  scopes: ['Read and change drafts of your applications', 'See your profile'],
  texts: {
    title: 'Allow access',
    intro: '{client} asks to:',
    allow: 'Allow',
    deny: 'Deny',
  },
}

describe('ConsentPage', () => {
  it('names the client in bold and lists the text of each scope', () => {
    render(<ConsentPage context={context} />, { wrapper: ThemeModeProvider })

    const name = screen.getByText('Draft helper')
    expect(name.tagName).toBe('STRONG')
    expect(screen.getByText(/asks to:/)).toBeInTheDocument()
    expect(screen.getByText('Read and change drafts of your applications')).toBeInTheDocument()
    expect(screen.getByText('See your profile')).toBeInTheDocument()
  })

  it('shows the home page address as a link that opens in a new tab', () => {
    render(<ConsentPage context={context} />, { wrapper: ThemeModeProvider })

    const home = screen.getByRole('link', { name: 'https://helper.test/home' })
    expect(home).toHaveAttribute('href', 'https://helper.test/home')
    expect(home).toHaveAttribute('target', '_blank')
    expect(home).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('shows no link when the client has no home page', () => {
    render(<ConsentPage context={{ ...context, homeUrl: undefined }} />, { wrapper: ThemeModeProvider })

    expect(screen.queryByRole('link', { name: /helper\.test/ })).not.toBeInTheDocument()
  })

  it('posts accept from one form and cancel from another, each with the code, to the consent address', () => {
    const { container } = render(<ConsentPage context={context} />, { wrapper: ThemeModeProvider })
    const forms = container.querySelectorAll('form')

    expect(forms).toHaveLength(2)
    for (const form of forms) {
      expect(form).toHaveAttribute('method', 'post')
      expect(form).toHaveAttribute('action', '/consent?client=1')
      expect(form.querySelector('input[type="hidden"][name="code"]')).toHaveValue('abc-123')
    }
    expect(forms[0].querySelector('input[name="accept"]')).toHaveValue('yes')
    expect(forms[0].querySelector('input[name="cancel"]')).toBeNull()
    expect(forms[1].querySelector('input[name="cancel"]')).toHaveValue('no')
    expect(forms[1].querySelector('input[name="accept"]')).toBeNull()
    expect(forms[0]).toContainElement(screen.getByRole('button', { name: 'Allow' }))
    expect(forms[1]).toContainElement(screen.getByRole('button', { name: 'Deny' }))
    expect(screen.getByRole('button', { name: 'Allow' })).toHaveAttribute('type', 'submit')
    expect(screen.getByRole('button', { name: 'Deny' })).toHaveAttribute('type', 'submit')
  })

  it('shows an error above the card', () => {
    render(<ConsentPage context={{ ...context, error: 'Something failed.' }} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('alert')).toHaveTextContent('Something failed.')
  })
})
