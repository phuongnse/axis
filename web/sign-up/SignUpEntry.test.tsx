import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { formItem } from '../src/test/antd'
import { SignUpEntry } from './SignUpEntry'

const applications = {
  applications: [{ id: 'approvals', titles: { en: 'Approvals', vi: 'Phê duyệt' } }],
}

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stubServer(
  subdomain: (name: string) => Response = () => answer({ free: true, reason: null }),
  signUp: () => Response = () => new Response(null, { status: 202 }),
) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/sign-up/applications') {
      return answer(applications)
    }
    if (input === '/api/sign-up' && init?.method === 'POST') {
      return signUp()
    }
    return subdomain(decodeURIComponent(input.replace('/api/sign-up/subdomains/', '')))
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function signUpCalls(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([input, init]) => input === '/api/sign-up' && init?.method === 'POST')
}

/** Fills every field but the application, and waits until the subdomain check says the name is free. */
async function fillForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByRole('textbox', { name: 'Full name' }), 'Ana Nguyen')
  await user.type(screen.getByRole('textbox', { name: 'Work email' }), 'ana@example.com')
  await user.type(screen.getByRole('textbox', { name: 'Business name' }), 'Acme')
  await user.click(screen.getByRole('checkbox'))
  await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('This subdomain is free.'), {
    timeout: 5000,
  })
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
    expect(screen.getByRole('button', { name: 'Sign up' })).toBeInTheDocument()
  })

  it('shows every field and the button that sends the form', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    for (const label of ['Full name', 'Work email', 'Business name', 'Subdomain']) {
      expect(screen.getByRole('textbox', { name: label })).toBeInTheDocument()
    }
    for (const label of ['Application', 'Language']) {
      expect(screen.getByRole('combobox', { name: label })).toBeInTheDocument()
    }
    expect(screen.getByRole('checkbox')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign up' })).toBeInTheDocument()
  })

  it('suggests a subdomain from the business name and checks it', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.type(screen.getByRole('textbox', { name: 'Business name' }), 'Công ty Đông Á')

    expect(screen.getByRole('textbox', { name: 'Subdomain' })).toHaveValue('cong-ty-dong-a')
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

    const subdomain = screen.getByRole('textbox', { name: 'Subdomain' })
    await user.type(subdomain, 'acme')
    expect(await screen.findByText(/This subdomain is taken\./, {}, { timeout: 5000 })).toBeInTheDocument()

    await user.type(screen.getByRole('textbox', { name: 'Business name' }), 'Other name')

    expect(subdomain).toHaveValue('acme')
  })

  it('says the subdomain could not be checked when the route answers 503', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(() => new Response(null, { status: 503 }))
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.type(screen.getByRole('textbox', { name: 'Subdomain' }), 'acme')

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

  it('sends nothing for an empty form and focuses its first field', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByText('Enter your full name.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveFocus()
    expect(signUpCalls(fetchMock)).toHaveLength(0)
  })

  it('sends nothing for a malformed email and focuses the email', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await user.type(screen.getByRole('textbox', { name: 'Full name' }), 'Ana Nguyen')
    await user.type(screen.getByRole('textbox', { name: 'Work email' }), 'ana@')
    await user.type(screen.getByRole('textbox', { name: 'Business name' }), 'Acme')
    await user.click(screen.getByRole('checkbox'))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('This subdomain is free.'), {
      timeout: 5000,
    })

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Work email' })).toHaveFocus()
    expect(signUpCalls(fetchMock)).toHaveLength(0)
  })

  it('sends nothing while the subdomain is refused and focuses the subdomain', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer(() => answer({ free: false, reason: 'taken' }))
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await user.type(screen.getByRole('textbox', { name: 'Full name' }), 'Ana Nguyen')
    await user.type(screen.getByRole('textbox', { name: 'Work email' }), 'ana@example.com')
    await user.type(screen.getByRole('textbox', { name: 'Business name' }), 'Acme')
    await user.click(screen.getByRole('checkbox'))
    expect(
      await screen.findByText('This subdomain is taken. Choose another.', {}, { timeout: 5000 }),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Subdomain' })).toHaveFocus())
    expect(signUpCalls(fetchMock)).toHaveLength(0)
  })

  it('shows Check your inbox with the address after a 202, and Change email brings the form back with its values', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByRole('heading', { name: 'Check your inbox' })).toHaveFocus()
    expect(screen.getByText('We sent the confirmation email to ana@example.com.')).toBeInTheDocument()
    expect(signUpCalls(fetchMock)).toHaveLength(1)
    expect(JSON.parse(String(signUpCalls(fetchMock)[0][1]?.body))).toEqual({
      fullName: 'Ana Nguyen',
      email: 'ana@example.com',
      businessName: 'Acme',
      subdomain: 'acme',
      language: 'en',
      application: '',
    })

    await user.click(screen.getByRole('button', { name: 'Change email' }))

    expect(await screen.findByRole('heading', { name: 'Sign up' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveValue('Ana Nguyen')
    expect(screen.getByRole('textbox', { name: 'Work email' })).toHaveValue('ana@example.com')
    expect(screen.getByRole('textbox', { name: 'Work email' })).toHaveFocus()
  })

  it('shows a refusal of the subdomain inside the subdomain field', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(undefined, () => answer({ errors: { '/subdomain': ['signUp.subdomain.taken'] } }, 400))
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    const subdomain = screen.getByRole('textbox', { name: 'Subdomain' })
    expect(await within(formItem(subdomain)).findByText('This subdomain is taken. Choose another.')).toBeInTheDocument()
    expect(subdomain).toHaveFocus()
  })

  it('shows a failed sign-up in an error alert above the form', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(undefined, () => new Response(null, { status: 500 }))
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The sign-up could not be sent. Try again.')
    expect(alert.parentElement).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Sign up' })).toBeEnabled()
  })
})
