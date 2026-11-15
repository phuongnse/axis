import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioAutoAccept } from './StudioAutoAccept'

function renderSetting(publisher: boolean) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify({ autoAccept: false }), { status: 200 })),
  )
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioAutoAccept application="crm" publisher={publisher} />
      </TextProvider>
    </ThemeModeProvider>,
  )
}

const publisherOnly = /Only a publisher can change this\./

describe('StudioAutoAccept', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('draws its label, then its help, then the switch, which the label names and the help describes', async () => {
    renderSetting(true)

    const toggle = await screen.findByRole('switch', { name: 'Let AI accept its own drafts' })
    await waitFor(() => expect(toggle).toBeEnabled())
    const label = screen.getByText('Let AI accept its own drafts')
    const help = screen.getByText(/AI can accept a draft it made for this application/)
    expect(label.compareDocumentPosition(help) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(help.nextElementSibling).toBe(toggle)
    expect(toggle).toHaveAccessibleDescription(help.textContent!)
    expect(toggle).not.toHaveAccessibleDescription(publisherOnly)
  })

  it('disables the switch for an author who is not a publisher and ends its help with who can change it', async () => {
    renderSetting(false)

    const toggle = await screen.findByRole('switch', { name: 'Let AI accept its own drafts' })
    await waitFor(() => expect(toggle).toBeDisabled())
    expect(toggle).toHaveAccessibleDescription(publisherOnly)
    expect(screen.getByText(publisherOnly).nextElementSibling).toBe(toggle)
  })
})
