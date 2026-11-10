import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SignUpEntry } from './SignUpEntry'

const pending = {
  state: 'pending',
  email: 'ana@example.com',
  businessName: 'Acme',
  address: 'acme.vn.example.com',
}

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs the two routes of the verify page. Each answer is a function, so a test can change it between calls. */
function stubServer(verification: () => Response, confirm: () => Response = () => answer({ id: 'x', secret: 'y' })) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input === '/api/sign-up/verification') {
      return verification()
    }
    if (input === '/api/sign-up/confirm') {
      return confirm()
    }
    throw new Error(`Unexpected request to ${input}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function bodyOf(fetchMock: ReturnType<typeof stubServer>, url: string) {
  const call = fetchMock.mock.calls.find(([input]) => input === url) as unknown as [string, RequestInit]
  return JSON.parse(call[1].body as string) as unknown
}

describe('the verify page', () => {
  beforeEach(() => {
    document.documentElement.lang = 'en'
    window.history.replaceState(null, '', '/sign-up/verify#abc')
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    window.history.replaceState(null, '', '/')
  })

  it('checks the link with the fragment in the body, then asks to confirm what will start', async () => {
    const fetchMock = stubServer(() => answer(pending))
    render(<SignUpEntry />)

    expect(screen.getByText('Checking the link.')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Confirm' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Confirm your sign-up' })).toBeInTheDocument()
    expect(
      screen.getByText((_content, element) =>
        element?.tagName === 'DIV' || element?.tagName === 'P'
          ? element.textContent ===
            'Confirm to start the trial for Acme at acme.vn.example.com. You sign in with ana@example.com.'
          : false,
      ),
    ).toBeInTheDocument()
    expect(bodyOf(fetchMock, '/api/sign-up/verification')).toEqual({ token: 'abc' })
    for (const [input] of fetchMock.mock.calls) {
      expect(String(input)).not.toContain('abc')
    }
  })

  it('shows a link that was used as already confirmed', async () => {
    stubServer(() => answer({ state: 'confirmed' }))
    render(<SignUpEntry />)

    expect(await screen.findByRole('heading', { name: 'Already confirmed' })).toBeInTheDocument()
    expect(screen.getByText('This sign-up is confirmed. An email tells you when you can sign in.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument()
  })

  it('shows an expired link with Sign up again to the sign-up page', async () => {
    stubServer(() => answer({ state: 'expired' }))
    render(<SignUpEntry />)

    expect(await screen.findByRole('heading', { name: 'This link has expired' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Sign up again' })).toHaveAttribute('href', '/sign-up')
  })

  it('offers Try again after a check that failed, and checks again', async () => {
    let failing = true
    const fetchMock = stubServer(() => (failing ? new Response(null, { status: 500 }) : answer(pending)))
    const user = userEvent.setup()
    render(<SignUpEntry />)

    expect(await screen.findByRole('alert')).toHaveTextContent('The link could not be checked. Try again.')
    failing = false
    await user.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('button', { name: 'Confirm' })).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(([input]) => input === '/api/sign-up/verification')).toHaveLength(2)
  })

  it('confirms with the fragment in the body and then shows already confirmed', async () => {
    const fetchMock = stubServer(() => answer(pending))
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.click(await screen.findByRole('button', { name: 'Confirm' }))

    expect(await screen.findByRole('heading', { name: 'Already confirmed' })).toBeInTheDocument()
    expect(bodyOf(fetchMock, '/api/sign-up/confirm')).toEqual({ token: 'abc' })
  })

  it('shows the error above the sentence and keeps Confirm when the confirm fails', async () => {
    stubServer(
      () => answer(pending),
      () => new Response(null, { status: 500 }),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.click(await screen.findByRole('button', { name: 'Confirm' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The sign-up could not be confirmed. Try again.')
    // The button's loading icon leaves the DOM only when its motion ends, which jsdom never reports.
    expect(await screen.findByRole('button', { name: /Confirm$/ })).toBeInTheDocument()
    const sentence = screen.getByText(/Confirm to start the trial for/)
    expect(alert.compareDocumentPosition(sentence) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Confirm your sign-up' })).toBeInTheDocument()
  })

  it('shows the state that a refused confirm names, and not as an error', async () => {
    stubServer(
      () => answer(pending),
      () => answer({ state: 'expired' }, 409),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.click(await screen.findByRole('button', { name: 'Confirm' }))

    expect(await screen.findByRole('heading', { name: 'This link has expired' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the sign-up form at any other path', () => {
    window.history.replaceState(null, '', '/sign-up')
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => answer({ applications: [] })),
    )
    render(<SignUpEntry />)

    expect(screen.getByRole('heading', { name: 'Sign up' })).toBeInTheDocument()
  })
})
