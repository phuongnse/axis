import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { identityLegal, identitySignInContext } from '../designSystem/fixtures'
import { ThemeModeProvider } from '../platform/theme'
import { IdentityFrame } from './IdentityFrame'

function renderFrame(frame = identitySignInContext) {
  return render(
    <IdentityFrame title="Sign in" frame={frame}>
      <p>Body</p>
    </IdentityFrame>,
    { wrapper: ThemeModeProvider },
  )
}

describe('IdentityFrame', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("lists each language in its own language and System, Light and Dark under the 'Language and theme' gear", async () => {
    renderFrame()

    await userEvent.click(screen.getByRole('button', { name: 'Language and theme' }))

    for (const name of ['English', 'Tiếng Việt', 'System', 'Light', 'Dark']) {
      expect(await screen.findByRole('menuitemradio', { name })).toBeInTheDocument()
    }
    expect(screen.getByRole('menuitemradio', { name: 'English' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.queryByRole('link', { name: 'English' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Tiếng Việt' })).toBeNull()
  })

  it('opens the page in the picked language', async () => {
    const assign = vi.fn()
    vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, assign })
    renderFrame()

    await userEvent.click(screen.getByRole('button', { name: 'Language and theme' }))
    await userEvent.click(await screen.findByRole('menuitemradio', { name: 'Tiếng Việt' }))

    expect(assign).toHaveBeenCalledWith('#vi')
  })

  it('switches the theme at once without leaving the page', async () => {
    const assign = vi.fn()
    vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, assign })
    renderFrame()

    await userEvent.click(screen.getByRole('button', { name: 'Language and theme' }))
    await userEvent.click(await screen.findByRole('menuitemradio', { name: 'Dark' }))
    await userEvent.click(screen.getByRole('button', { name: 'Language and theme' }))

    expect(await screen.findByRole('menuitemradio', { name: 'Dark' })).toHaveAttribute('aria-checked', 'true')
    expect(assign).not.toHaveBeenCalled()
  })

  it("draws the legal line after the card with the links of the page's context", () => {
    renderFrame()

    const legal = screen.getByRole('navigation', { name: 'Legal' })
    const links = within(legal).getAllByRole('link')
    expect(links.map((link) => link.textContent)).toEqual(['Privacy', 'Terms', 'Licences'])
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['#privacy', '#terms', '#licences'])
    expect(screen.getByText(`© ${new Date().getFullYear()} Axis`)).toBeInTheDocument()
    const title = screen.getByRole('heading', { name: 'Sign in' })
    expect(title.compareDocumentPosition(legal) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('shows the copyright alone when the context has no addresses', () => {
    renderFrame({ ...identitySignInContext, legal: { texts: identityLegal.texts } })

    expect(screen.getByText(`© ${new Date().getFullYear()} Axis`)).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Legal' })).toBeNull()
  })
})
