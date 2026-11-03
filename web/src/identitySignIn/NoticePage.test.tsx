import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { identityNoticeContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { NoticePage } from './NoticePage'
import type { NoticeContext } from './signInContext'

const context: NoticeContext = {
  ...identityNoticeContext,
  realmName: 'Contoso',
  linkUrl: '/next',
  linkText: 'Continue',
}

describe('NoticePage', () => {
  it('shows the title, the organization and the message', () => {
    render(<NoticePage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Notice')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByText('Your account has been updated.')).toBeInTheDocument()
  })

  it('links onward with the text of the link', () => {
    render(<NoticePage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Continue' })).toHaveAttribute('href', '/next')
  })

  it('shows no link when Keycloak offers none', () => {
    render(<NoticePage context={{ ...context, linkUrl: undefined, linkText: undefined }} />, {
      wrapper: ThemeModeProvider,
    })

    expect(screen.queryByRole('link', { name: 'Continue' })).not.toBeInTheDocument()
  })
})
