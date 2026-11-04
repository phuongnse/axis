import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { identityErrorContext, identityErrorMessage } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { ErrorPage } from './ErrorPage'
import type { ErrorContext } from './signInContext'

const context: ErrorContext = { ...identityErrorContext, realmName: 'Contoso', backUrl: '/app' }

describe('ErrorPage', () => {
  it('shows the message as an error alert under the title and the organization', () => {
    render(<ErrorPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByText('Something went wrong')).toBeInTheDocument()
    expect(screen.getByText('Contoso')).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent(identityErrorMessage)
  })

  it('links "Back to application" when the client has a home address', () => {
    render(<ErrorPage context={context} />, { wrapper: ThemeModeProvider })

    expect(screen.getByRole('link', { name: 'Back to application' })).toHaveAttribute('href', '/app')
  })

  it('shows no link without a home address', () => {
    render(<ErrorPage context={{ ...context, backUrl: undefined }} />, { wrapper: ThemeModeProvider })

    expect(screen.queryByRole('link', { name: 'Back to application' })).not.toBeInTheDocument()
  })
})
