import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SignUpEntry } from './SignUpEntry'

const id = '0190a0b0-0000-7000-8000-000000000001'
const secret = 'c2VjcmV0'
const address = 'https://acme.vn.example.com'

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs the progress route with a queue of answers, the last of which repeats. */
function stubProgress(...answers: Array<() => Response>) {
  let next = 0
  const fetchMock = vi.fn(async (input: string) => {
    if (input === `/api/sign-up/${id}`) {
      return answers[Math.min(next++, answers.length - 1)]()
    }
    if (input === '/api/sign-up/documents') {
      return answer({
        terms: { version: '1', urls: { en: 'https://axis.example.com/terms', vi: 'https://axis.example.com/terms' } },
        dpa: { version: '1', urls: { en: 'https://axis.example.com/dpa', vi: 'https://axis.example.com/dpa' } },
        privacy: {
          version: '1',
          urls: { en: 'https://axis.example.com/privacy', vi: 'https://axis.example.com/privacy' },
        },
        licencesUrl: 'https://axis.example.com/licences',
      })
    }
    throw new Error(`Unexpected request to ${input}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function progressCalls(fetchMock: ReturnType<typeof stubProgress>) {
  return fetchMock.mock.calls.filter(([input]) => input === `/api/sign-up/${id}`)
}

describe('the progress page', () => {
  beforeEach(() => {
    document.documentElement.lang = 'en'
    window.history.replaceState(null, '', `/sign-up/progress#${id}.${secret}`)
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    window.history.replaceState(null, '', '/')
  })

  it('shows Setting up, then after one interval the ready title and sentence with the address as a link', async () => {
    stubProgress(
      () => answer({ step: 'verified', address }),
      () => answer({ step: 'ready', address }),
    )
    render(<SignUpEntry />)

    expect(await screen.findByRole('heading', { name: 'Setting up the trial' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(
      'Setting up the trial. You can close this page: an email tells you when you can sign in.',
    )
    expect(screen.getByText('Setting up', { exact: true }).closest('li')).toHaveAttribute('aria-current', 'step')

    await act(() => vi.advanceTimersByTimeAsync(2000))

    expect(await screen.findByRole('heading', { name: 'The trial is ready' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(
      `The trial is ready at ${address}. Open the invitation email to set your password, then sign in.`,
    )
    expect(screen.getByRole('link', { name: address })).toHaveAttribute('href', address)
    expect(screen.getByTestId('steps-bar').querySelector('[aria-current]')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('shows the delayed alert and keeps asking', async () => {
    const fetchMock = stubProgress(() => answer({ step: 'delayed', address }))
    render(<SignUpEntry />)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The trial needs more time to set up. Our team is on it, and you do not need to do anything.',
    )
    expect(screen.getByRole('heading', { name: 'Setting up the trial' })).toBeInTheDocument()
    expect(progressCalls(fetchMock)).toHaveLength(1)

    await act(() => vi.advanceTimersByTimeAsync(2000))

    expect(progressCalls(fetchMock)).toHaveLength(2)
    expect(screen.getByRole('alert')).toBeInTheDocument()
  })

  it('keeps asking quietly after a read that failed', async () => {
    const fetchMock = stubProgress(
      () => new Response(null, { status: 500 }),
      () => answer({ step: 'ready', address }),
    )
    render(<SignUpEntry />)

    expect(await screen.findByRole('heading', { name: 'Setting up the trial' })).toBeInTheDocument()
    await vi.waitFor(() => expect(progressCalls(fetchMock)).toHaveLength(1))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    await act(() => vi.advanceTimersByTimeAsync(2000))

    expect(await screen.findByRole('heading', { name: 'The trial is ready' })).toBeInTheDocument()
  })

  it('shows only that an email follows for a 404, and sends the secret only in a header', async () => {
    const fetchMock = stubProgress(() => new Response(null, { status: 404 }))
    render(<SignUpEntry />)

    expect(await screen.findByRole('heading', { name: 'The progress cannot be shown' })).toBeInTheDocument()
    expect(screen.getByText('An email tells you when you can sign in.')).toBeInTheDocument()
    expect(screen.queryByTestId('steps-bar')).not.toBeInTheDocument()

    const [, init] = progressCalls(fetchMock)[0] as unknown as [string, RequestInit]
    expect(new Headers(init.headers).get('Sign-Up-Secret')).toBe(secret)
    for (const [input] of fetchMock.mock.calls) {
      expect(String(input)).not.toContain(secret)
    }

    await act(() => vi.advanceTimersByTimeAsync(4000))
    expect(progressCalls(fetchMock)).toHaveLength(1)
  })

  it('shows the unknown state for a fragment without an id and a secret, and asks nothing', async () => {
    window.history.replaceState(null, '', '/sign-up/progress#nosecret')
    const fetchMock = stubProgress(() => answer({ step: 'verified', address }))
    render(<SignUpEntry />)

    expect(await screen.findByRole('heading', { name: 'The progress cannot be shown' })).toBeInTheDocument()
    expect(screen.queryByTestId('steps-bar')).not.toBeInTheDocument()
    expect(progressCalls(fetchMock)).toHaveLength(0)
  })
})
