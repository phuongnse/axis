import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SignUpEntry } from './SignUpEntry'

const applications = {
  applications: [{ id: 'approvals', titles: { en: 'Approvals', vi: 'Phê duyệt' } }],
}

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stubServer(subdomain: (name: string) => Response = () => answer({ free: true, reason: null })) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input === '/api/sign-up/applications') {
      return answer(applications)
    }
    return subdomain(decodeURIComponent(input.replace('/api/sign-up/subdomains/', '')))
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('SignUpEntry', () => {
  beforeEach(() => {
    document.documentElement.lang = 'en'
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('starts in Vietnamese for a Vietnamese browser and switches to English', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('vi')
    stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)

    expect(screen.getByRole('heading', { name: 'Đăng ký' })).toBeInTheDocument()
    expect(document.documentElement.lang).toBe('vi')

    await user.click(screen.getByRole('button', { name: 'Ngôn ngữ và giao diện' }))
    await user.click(await screen.findByRole('menuitemradio', { name: 'English' }))

    expect(await screen.findByRole('heading', { name: 'Sign up' })).toBeInTheDocument()
    expect(document.documentElement.lang).toBe('en')
  })

  it('starts in English for any other browser language', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('fr-FR')
    stubServer()
    render(<SignUpEntry />)

    expect(screen.getByRole('heading', { name: 'Sign up' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /sign up|đăng ký/i })).not.toBeInTheDocument()
  })

  it('shows every field and no button to send the form', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    for (const label of ['Full name', 'Work email', 'Business name', 'Subdomain', 'Application', 'Language']) {
      expect(screen.getByLabelText(label)).toBeInTheDocument()
    }
    expect(screen.getByRole('checkbox')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /submit|send|create/i })).not.toBeInTheDocument()
  })

  it('suggests a subdomain from the business name and checks it', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.type(screen.getByLabelText('Business name'), 'Công ty Đông Á')

    expect(screen.getByLabelText('Subdomain')).toHaveValue('cong-ty-dong-a')
    expect(await screen.findByText(/This subdomain is free\./, {}, { timeout: 5000 })).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/sign-up/subdomains/cong-ty-dong-a',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
  })

  it('stops suggesting once the person edits the subdomain, and says why a name is refused', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer((name) =>
      name === 'acme' ? answer({ free: false, reason: 'taken' }) : answer({ free: true, reason: null }),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)

    const subdomain = screen.getByLabelText('Subdomain')
    await user.type(subdomain, 'acme')
    expect(await screen.findByText(/This subdomain is taken\./, {}, { timeout: 5000 })).toBeInTheDocument()

    await user.type(screen.getByLabelText('Business name'), 'Other name')

    expect(subdomain).toHaveValue('acme')
  })

  it('says the subdomain could not be checked when the route answers 503', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(() => new Response(null, { status: 503 }))
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.type(screen.getByLabelText('Subdomain'), 'acme')

    expect(await screen.findByText(/The subdomain could not be checked\./, {}, { timeout: 5000 })).toBeInTheDocument()
  })

  it('offers the region applications by their title in the page language', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('vi')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/sign-up/applications', expect.anything()))

    await user.click(screen.getByLabelText('Ứng dụng'))

    expect(await screen.findByText('Phê duyệt')).toBeInTheDocument()
  })
})
