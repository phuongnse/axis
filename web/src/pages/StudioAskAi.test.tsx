import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellAsideContext } from '../platform/shellAsideContext'
import type { StudioAiConnection } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { StudioAskAi } from './StudioAskAi'

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

/** Draws the panel with the tenant's AI connection answering `connection`. */
function renderPanel(connection: StudioAiConnection) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      if (input === '/api/studio/ai') {
        return json(connection)
      }
      if (input.endsWith('/settings')) {
        return json({ autoAccept: false })
      }
      return new Response(null, { status: 404 })
    }),
  )
  const column = document.createElement('div')
  document.body.append(column)
  render(
    <MemoryRouter>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ShellAsideContext.Provider value={{ target: column, claim: () => () => {} }}>
          <StudioAskAi application="Purchases" onClose={() => {}} />
        </ShellAsideContext.Provider>
      </TextProvider>
    </MemoryRouter>,
  )
}

// Proposed: each author's own Claude account and no count of requests, see the review on #3263.
describe('StudioAskAi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    document.body.replaceChildren()
  })

  it('shows the form with Send and no count of requests once the tenant has connected its AI', async () => {
    renderPanel({ connected: true })

    expect(await screen.findByRole('button', { name: 'Send' })).toBeEnabled()
    expect(screen.queryByText(/left this month/)).toBeNull()
    expect(screen.queryByTestId('studio-ai-not-connected')).toBeNull()
    expect(within(screen.getByTestId('studio-ai-mcp')).getByText('Use your own AI app')).toBeInTheDocument()
  })

  it('leads an author with no AI of their own to connect one on their account page or to use an AI app through MCP, with no form', async () => {
    renderPanel({ connected: false })

    expect(await screen.findByText('With your own API key')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Connect your AI' })).toHaveAttribute('href', '/account')
    expect(
      within(screen.getByTestId('studio-ai-mcp')).getByText('With an AI app you already pay for'),
    ).toBeInTheDocument()
    expect(screen.queryByText('Use your own AI app')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Send' })).toBeNull()
  })
})
