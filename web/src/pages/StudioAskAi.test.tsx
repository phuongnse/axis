import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { ShellAsideContext } from '../platform/shellAsideContext'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioAskAi } from './StudioAskAi'

let aside: HTMLDivElement | undefined

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs `fetch` with the application's settings and the send route, which answers `sendStatus`. */
function stubServer(autoAccept: boolean, sendStatus = 202) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit): Promise<Response> => {
    if (input === '/api/studio/apps/PurchaseRequests/settings') {
      return json({ autoAccept })
    }
    if (input === '/api/studio/apps/PurchaseRequests/authoring-requests' && init?.method === 'POST') {
      return sendStatus === 202
        ? json({ id: 'r1', state: 'planning', text: 'x', autoAccept: false }, 202)
        : json({ title: 'No.' }, sendStatus)
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function Location() {
  const location = useLocation()
  return <div data-testid="location">{location.pathname + location.search}</div>
}

function renderPanel() {
  aside = document.createElement('div')
  document.body.append(aside)
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ShellAsideContext.Provider value={{ target: aside, claim: () => () => {} }}>
          <MemoryRouter initialEntries={['/studio?app=PurchaseRequests']}>
            <StudioAskAi application="PurchaseRequests" onClose={vi.fn()} />
            <Location />
          </MemoryRouter>
        </ShellAsideContext.Provider>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

/** The body of the POST to the send route, parsed, or undefined when nothing was posted. */
function posted(fetchMock: ReturnType<typeof stubServer>) {
  const call = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')
  return call === undefined ? undefined : JSON.parse(call[1]!.body as string)
}

afterEach(() => {
  vi.unstubAllGlobals()
  aside?.remove()
  aside = undefined
})

describe('StudioAskAi', () => {
  it('posts the text and the ticked check box, opens the request and empties the field', async () => {
    const fetchMock = stubServer(true)
    renderPanel()

    const text = screen.getByRole('textbox', { name: /Your request/ })
    await userEvent.type(text, '  Add a due date field  ')
    await userEvent.click(await screen.findByRole('checkbox', { name: /Accept when checks and review pass/ }))
    await userEvent.click(screen.getByTestId('studio-ask-ai-send'))

    await waitFor(() =>
      expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests&view=requests&request=r1'),
    )
    expect(posted(fetchMock)).toEqual({ text: 'Add a due date field', autoAccept: true })
    expect(screen.getByRole('textbox', { name: /Your request/ })).toHaveValue('')
    expect(screen.getByTestId('studio-ask-ai-panel')).toBeInTheDocument()
  })

  it('shows no check box when auto-accept is off, and posts autoAccept false', async () => {
    const fetchMock = stubServer(false)
    renderPanel()

    await userEvent.type(screen.getByRole('textbox', { name: /Your request/ }), 'Add a field')
    await userEvent.click(screen.getByTestId('studio-ask-ai-send'))

    await waitFor(() => expect(posted(fetchMock)).toEqual({ text: 'Add a field', autoAccept: false }))
    expect(fetchMock).toHaveBeenCalledWith('/api/studio/apps/PurchaseRequests/settings', expect.anything())
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('refuses an empty request on click and posts nothing', async () => {
    const fetchMock = stubServer(false)
    renderPanel()

    await userEvent.type(screen.getByRole('textbox', { name: /Your request/ }), '   ')
    await userEvent.click(screen.getByTestId('studio-ask-ai-send'))

    expect(await screen.findByText('Enter your request.')).toBeInTheDocument()
    expect(posted(fetchMock)).toBeUndefined()
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests')
  })

  it('shows one error message when the send fails, and stays on the page', async () => {
    stubServer(false, 409)
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderPanel()

    await userEvent.type(screen.getByRole('textbox', { name: /Your request/ }), 'Add a field')
    await userEvent.click(screen.getByTestId('studio-ask-ai-send'))

    expect(await screen.findByTestId('studio-ask-ai-error')).toHaveTextContent('The request could not be sent.')
    expect(screen.getByTestId('location')).toHaveTextContent('/studio?app=PurchaseRequests')
  })

  it("ends with the MCP address of the page's host and a copy button", () => {
    stubServer(false)
    renderPanel()

    expect(screen.getByText('Use your own AI app')).toBeInTheDocument()
    const address = screen.getByTestId('studio-mcp-address')
    expect(address).toHaveTextContent(`${window.location.origin}/mcp`)
    expect(within(address).getByRole('button', { name: /copy/i })).toBeInTheDocument()
  })
})
