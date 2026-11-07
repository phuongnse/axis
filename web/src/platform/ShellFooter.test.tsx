import { render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchAboutVersion } from './about'
import { ShellFooter } from './ShellFooter'
import type { SiteLegal } from './site'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

vi.mock('./about')

const texts = {
  'shell.legal.label': 'Legal',
  'shell.legal.privacy': 'Privacy',
  'shell.legal.terms': 'Terms',
  'shell.legal.licences': 'Licences',
}
const catalogs = [{ texts, fallbackTexts: texts }]
const year = new Date().getFullYear()

function renderFooter(legal?: SiteLegal | null) {
  return render(
    <ThemeModeProvider>
      <TextProvider catalogs={catalogs} development={false}>
        <ShellFooter legal={legal} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

describe('the shell footer', () => {
  beforeEach(() => {
    vi.mocked(fetchAboutVersion).mockResolvedValue(null)
  })

  it('shows Privacy, Terms and Licences in the Legal landmark when the site gives its links', async () => {
    renderFooter({ privacyUrl: 'https://example.test/privacy', termsUrl: 'https://example.test/terms' })

    const links = within(await screen.findByRole('navigation', { name: 'Legal' })).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual(['Privacy', 'Terms', 'Licences'])
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      'https://example.test/privacy',
      'https://example.test/terms',
      '/licences',
    ])
  })

  it('shows Licences alone when the site gives no links', async () => {
    renderFooter(null)

    const links = within(await screen.findByRole('navigation', { name: 'Legal' })).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual(['Licences'])
    expect(links[0]).toHaveAttribute('href', '/licences')
  })

  it('shows only the link whose address is set', async () => {
    renderFooter({ privacyUrl: null, termsUrl: 'https://example.test/terms' })

    const links = within(await screen.findByRole('navigation', { name: 'Legal' })).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual(['Terms', 'Licences'])
  })

  it('shows the version after the copyright when it loads', async () => {
    vi.mocked(fetchAboutVersion).mockResolvedValue('1.2.3')
    renderFooter()

    const version = await screen.findByText('v1.2.3')
    expect(version).toBeInTheDocument()
    expect(version.parentElement).toBe(screen.getByText(`© ${year} Axis`).parentElement)
  })

  it('shows no version when nobody is signed in', async () => {
    renderFooter()

    expect(await screen.findByText(`© ${year} Axis`)).toBeInTheDocument()
    expect(fetchAboutVersion).toHaveBeenCalled()
    expect(screen.queryByText(/^v\d/)).toBeNull()
  })
})
