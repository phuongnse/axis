import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { identityPageExpiredContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { PageExpiredPage } from './PageExpiredPage'
import type { PageExpiredContext } from './signInContext'

const context: PageExpiredContext = {
  ...identityPageExpiredContext,
  realmName: 'Contoso',
  continueUrl: '/login?code=1',
  restartUrl: '/restart',
}

describe('PageExpiredPage', () => {
  it('explains what happened under the title and the organization', () => {
    render(<PageExpiredPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Page expired')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByText(context.texts.message)).toBeInTheDocument()
  })

  it('offers Continue at the login action and Start again at the restart address', () => {
    render(<PageExpiredPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Continue' })).toHaveAttribute('href', '/login?code=1')
    expect(screen.getByRole('link', { name: 'Start again' })).toHaveAttribute('href', '/restart')
  })
})
