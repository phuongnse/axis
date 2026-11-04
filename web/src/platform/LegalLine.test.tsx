import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { LegalLine } from './LegalLine'
import type { LegalTexts } from './LegalLine'
import { ThemeModeProvider } from './theme'

const texts: LegalTexts = { legal: 'Legal', privacy: 'Privacy', terms: 'Terms', licences: 'Licences' }

function renderLine(links: Parameters<typeof LegalLine>[0]['links']) {
  return render(<LegalLine copyright="© 2026 Axis" links={links} />, { wrapper: ThemeModeProvider })
}

describe('LegalLine', () => {
  it('shows the copyright first, then Privacy, Terms and Licences in the Legal landmark', () => {
    renderLine({ privacyUrl: '/privacy', termsUrl: '/terms', licencesUrl: '/licences', texts })

    const legal = screen.getByRole('navigation', { name: 'Legal' })
    const links = within(legal).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual(['Privacy', 'Terms', 'Licences'])
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['/privacy', '/terms', '/licences'])
    expect(
      screen.getByText('© 2026 Axis').compareDocumentPosition(legal) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })

  it('leaves out Privacy and Terms when their addresses are not set', () => {
    renderLine({ licencesUrl: '/licences', texts })

    expect(screen.getByRole('link', { name: 'Licences' })).toHaveAttribute('href', '/licences')
    expect(screen.queryByRole('link', { name: 'Privacy' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Terms' })).toBeNull()
  })

  it('shows the copyright alone when no address is set', () => {
    renderLine({ texts })

    expect(screen.getByText('© 2026 Axis')).toBeInTheDocument()
    expect(screen.queryByRole('navigation')).toBeNull()
  })
})
