import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { identityFrameTexts, identityLegal } from '../designSystem/fixtures'
import { CardPage } from './CardPage'
import { designTokensFor } from './designTokens'
import { ThemeModeProvider } from './theme'
import { TextContext } from './texts'

const texts = new Map<string, string>([
  ['shell.preferences.label', identityFrameTexts.languageAndTheme],
  ['shell.locale.label', identityFrameTexts.language],
  ['shell.locale.en', 'English'],
  ['shell.locale.vi', 'Tiếng Việt'],
  ['shell.theme.label', identityFrameTexts.theme],
  ['shell.theme.system', identityFrameTexts.system],
  ['shell.theme.light', identityFrameTexts.light],
  ['shell.theme.dark', identityFrameTexts.dark],
])

function renderPage({
  organization,
  onLocaleChange = vi.fn(),
}: { organization?: string; onLocaleChange?: () => void } = {}) {
  return render(
    <TextContext.Provider value={(key) => texts.get(key) ?? key}>
      <CardPage
        organization={organization}
        title="Sign in"
        locales={['en', 'vi']}
        locale="en"
        onLocaleChange={onLocaleChange}
        legal={identityLegal}
      >
        <p>Body</p>
      </CardPage>
    </TextContext.Provider>,
    { wrapper: ThemeModeProvider },
  )
}

describe('CardPage', () => {
  it("draws the organization's heading, and Axis when no name is given", () => {
    const { unmount } = renderPage({ organization: 'Contoso' })
    expect(screen.getByTestId('organization-heading')).toHaveTextContent('Contoso')
    unmount()

    renderPage()
    expect(screen.getByTestId('organization-heading')).toHaveTextContent('Axis')
  })

  it('draws the title above the children in the card', () => {
    const { container } = renderPage()

    const card = container.querySelector('.ant-card')
    expect(card).toContainElement(screen.getByRole('heading', { name: 'Sign in' }))
    expect(card).toContainElement(screen.getByText('Body'))
  })

  it("draws the 'Language and theme' button and tells which language is picked", async () => {
    const onLocaleChange = vi.fn()
    renderPage({ onLocaleChange })

    await userEvent.click(screen.getByRole('button', { name: 'Language and theme' }))
    await userEvent.click(await screen.findByRole('menuitemradio', { name: 'Tiếng Việt' }))

    expect(onLocaleChange).toHaveBeenCalledWith('vi')
  })

  it('makes the card and the legal line as wide as the small dialog size token', () => {
    const { container } = renderPage()
    const width = `${designTokensFor('light').size.dialog.small}px`

    expect(container.querySelector('.ant-card')).toHaveStyle({ maxWidth: width })
    expect(screen.getByTestId('card-page-legal')).toHaveStyle({ maxWidth: width })
  })

  it('draws the legal line after the card, with the copyright and the links', () => {
    const { container } = renderPage()

    const card = container.querySelector('.ant-card')!
    const legal = screen.getByRole('navigation', { name: 'Legal' })
    expect(card.compareDocumentPosition(legal) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByText(`© ${new Date().getFullYear()} Axis`)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Privacy' })).toHaveAttribute('href', '#privacy')
    expect(screen.getByRole('link', { name: 'Terms' })).toHaveAttribute('href', '#terms')
    expect(screen.getByRole('link', { name: 'Licences' })).toHaveAttribute('href', '#licences')
  })
})
