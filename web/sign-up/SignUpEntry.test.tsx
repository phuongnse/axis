import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { formItem, isRequiredLabel } from '../src/test/antd'
import { SignUpEntry } from './SignUpEntry'

// Web Crypto is unreliable under jsdom, so the solver answers at once. The end-to-end sign-up journey runs the real one.
const solveChallenge = vi.hoisted(() =>
  vi.fn((_challenge: string, _salt: string, _algorithm?: string, _max?: number) => ({
    promise: Promise.resolve({ number: 42, took: 1 }),
    controller: new AbortController(),
  })),
)
vi.mock('altcha-lib/v1', () => ({ solveChallenge }))

const applications = {
  applications: [
    {
      id: 'approvals',
      titles: { en: 'Approvals', vi: 'Phê duyệt' },
      descriptions: { en: 'Requests that move through approvers.', vi: 'Yêu cầu đi qua người phê duyệt.' },
    },
  ],
}

const documents = {
  terms: {
    version: '2026-11-01',
    urls: { en: 'https://axis.example.com/terms', vi: 'https://axis.example.com/vi/dieu-khoan' },
  },
  dpa: {
    version: '2026-11-01',
    urls: { en: 'https://axis.example.com/dpa', vi: 'https://axis.example.com/vi/xu-ly-du-lieu' },
  },
  privacy: {
    version: '2026-11-01',
    urls: { en: 'https://axis.example.com/privacy', vi: 'https://axis.example.com/vi/quyen-rieng-tu' },
  },
  licencesUrl: 'https://axis.example.com/licences',
}

const region = { tenantDomain: 'vn.example.com' }

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** A challenge for the n-th fetch, so a second fetch is told apart from the first. */
function challengeOf(n: number) {
  return {
    algorithm: 'SHA-256',
    challenge: `challenge-${n}`,
    maxnumber: 100000,
    salt: `salt-${n}?expires=4102444800`,
    signature: `signature-${n}`,
  }
}

function stubServer(
  subdomain: (name: string) => Response = () => answer({ free: true, reason: null }),
  signUp: () => Response = () => new Response(null, { status: 202 }),
  {
    applicationsAnswer = () => answer(applications),
    documentsAnswer = () => answer(documents),
    regionAnswer = () => answer(region),
  }: {
    applicationsAnswer?: () => Response
    documentsAnswer?: () => Response
    regionAnswer?: () => Response
  } = {},
) {
  let challenges = 0
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/sign-up/challenge') {
      challenges += 1
      return answer(challengeOf(challenges))
    }
    if (input === '/api/sign-up/applications') {
      return applicationsAnswer()
    }
    if (input === '/api/sign-up/documents') {
      return documentsAnswer()
    }
    if (input === '/api/sign-up/region') {
      return regionAnswer()
    }
    if (input === '/api/sign-up' && init?.method === 'POST') {
      return signUp()
    }
    return subdomain(decodeURIComponent(input.replace('/api/sign-up/subdomains/', '')))
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function challengeFetches(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([input]) => input === '/api/sign-up/challenge')
}

/** The solved challenge that a sign-up call carries as altcha. */
function altchaOf(call: ReturnType<typeof signUpCalls>[number]) {
  return JSON.parse(atob((JSON.parse(String(call[1]?.body)) as { altcha: string }).altcha)) as unknown
}

function signUpCalls(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([input, init]) => input === '/api/sign-up' && init?.method === 'POST')
}

function subdomainStatus(name = 'Subdomain') {
  return within(formItem(screen.getByRole('textbox', { name }))).getByRole('status')
}

const fieldNames = {
  en: {
    fullName: 'Full name',
    email: 'Work email',
    businessName: 'Business name',
    subdomain: 'Subdomain',
    free: 'This subdomain is free.',
  },
  vi: {
    fullName: 'Họ và tên',
    email: 'Email công việc',
    businessName: 'Tên doanh nghiệp',
    subdomain: 'Tên miền phụ',
    free: 'Tên miền phụ này còn trống.',
  },
}

/** Fills every field but the application, and waits until the subdomain check says the name is free. */
async function fillForm(user: ReturnType<typeof userEvent.setup>, names = fieldNames.en) {
  await user.type(screen.getByRole('textbox', { name: names.fullName }), 'Ana Nguyen')
  await user.type(screen.getByRole('textbox', { name: names.email }), 'ana@example.com')
  await user.type(screen.getByRole('textbox', { name: names.businessName }), 'Acme')
  await user.click(screen.getByRole('checkbox'))
  await waitFor(() => expect(subdomainStatus(names.subdomain)).toHaveTextContent(names.free), {
    timeout: 5000,
  })
}

describe('SignUpEntry', () => {
  beforeEach(() => {
    document.documentElement.lang = 'en'
  })

  afterEach(() => {
    vi.restoreAllMocks()
    solveChallenge.mockClear()
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

  it('shows the fields in the documented order, then the terms box, the privacy notice and the button', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)
    await screen.findByRole('radio', { name: /Approvals/ })

    const inOrder = [
      screen.getByRole('textbox', { name: 'Full name' }),
      screen.getByRole('textbox', { name: 'Work email' }),
      screen.getByRole('textbox', { name: 'Business name' }),
      screen.getByRole('textbox', { name: 'Subdomain' }),
      screen.getByRole('combobox', { name: 'Language' }),
      screen.getByRole('radiogroup', { name: 'Start from' }),
      screen.getByRole('checkbox'),
      screen.getByText(/Your name and email are used/),
      screen.getByRole('button', { name: 'Sign up' }),
    ]
    for (let index = 1; index < inOrder.length; index++) {
      expect(inOrder[index - 1].compareDocumentPosition(inOrder[index]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
  })

  it('marks the required fields and gives each field its help', () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    const fields = [
      ['Full name', 'The name on the account you sign in with.'],
      ['Work email', 'The link to confirm the sign-up goes to this email, and you sign in with it.'],
      ['Business name', 'The name shown for the business. The subdomain is suggested from it.'],
      ['Subdomain', 'The first part of the address you sign in at. It cannot be changed later.'],
      ['Language', 'The language of the screens and of every email.'],
    ]
    for (const [label, help] of fields) {
      expect(isRequiredLabel(screen.getByText(label))).toBe(true)
      expect(screen.getByText(help)).toBeInTheDocument()
    }
    expect(isRequiredLabel(screen.getByText('Start from'))).toBe(false)
    expect(
      screen.getByText('The application that is there when you first sign in. Blank starts empty, to build in Studio.'),
    ).toBeInTheDocument()
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

  it('offers the region applications by their title and description in the page language', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('vi')
    stubServer()
    render(<SignUpEntry />)

    expect(await screen.findByRole('radio', { name: /Phê duyệt/ })).toBeInTheDocument()
    expect(screen.getByText('Yêu cầu đi qua người phê duyệt.')).toBeInTheDocument()
  })

  it('starts from Blank and sends the application the person picks', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const user = userEvent.setup()
    render(<SignUpEntry />)

    expect(await screen.findByRole('radio', { name: 'Blank' })).toBeChecked()
    await user.click(screen.getByRole('radio', { name: /Approvals/ }))
    expect(screen.getByRole('radio', { name: /Approvals/ })).toBeChecked()
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByRole('heading', { name: 'Check your inbox' })).toBeInTheDocument()
    expect(JSON.parse(String(signUpCalls(fetchMock)[0][1]?.body)).application).toBe('approvals')
  })

  it('warns that the applications did not load and keeps Blank as the only choice', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(undefined, undefined, { applicationsAnswer: () => new Response(null, { status: 500 }) })
    render(<SignUpEntry />)

    expect(
      await screen.findByText('The applications could not be loaded. You can still start from a blank one.'),
    ).toBeInTheDocument()
    expect(screen.getAllByRole('radio')).toHaveLength(1)
    expect(screen.getByRole('radio', { name: 'Blank' })).toBeChecked()
  })

  it('names the terms and the data processing agreement as links that open in a new tab', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    const terms = await screen.findByRole('link', { name: 'Terms of service' })
    expect(terms).toHaveAttribute('href', 'https://axis.example.com/terms')
    expect(terms).toHaveAttribute('target', '_blank')
    const dpa = screen.getByRole('link', { name: 'Data processing agreement' })
    expect(dpa).toHaveAttribute('href', 'https://axis.example.com/dpa')
    expect(dpa).toHaveAttribute('target', '_blank')
  })

  it('puts the privacy notice under the terms box, with the privacy policy as a link', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    const box = screen.getByRole('checkbox')
    const privacy = await screen.findByRole('link', { name: 'Privacy policy' })
    expect(box.compareDocumentPosition(privacy) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(privacy).toHaveAttribute('href', 'https://axis.example.com/privacy')
    expect(privacy).toHaveAttribute('target', '_blank')
    expect(screen.getByText(/Your name and email are used/)).toHaveTextContent(
      'Your name and email are used to set up and run your account, as the Privacy policy says.',
    )
  })

  it('links the legal line to the documents in a navigation named Legal', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    const legal = await screen.findByRole('navigation', { name: 'Legal' })
    expect(within(legal).getByRole('link', { name: 'Privacy' })).toHaveAttribute(
      'href',
      'https://axis.example.com/privacy',
    )
    expect(within(legal).getByRole('link', { name: 'Terms' })).toHaveAttribute('href', 'https://axis.example.com/terms')
    expect(within(legal).getByRole('link', { name: 'Licences' })).toHaveAttribute(
      'href',
      'https://axis.example.com/licences',
    )
  })

  it('names the documents as plain text and shows no legal links when the documents do not load', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(undefined, undefined, { documentsAnswer: () => new Response(null, { status: 500 }) })
    render(<SignUpEntry />)
    await screen.findByText('.vn.example.com')

    expect(screen.getByRole('checkbox').closest('label')).toHaveTextContent(
      'On behalf of my business, I accept the Terms of service and the Data processing agreement.',
    )
    expect(screen.queryByRole('link', { name: 'Terms of service' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Data processing agreement' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Privacy policy' })).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Legal' })).not.toBeInTheDocument()
  })

  it('ends the subdomain field in the tenant domain', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer()
    render(<SignUpEntry />)

    expect(await screen.findByText('.vn.example.com')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Subdomain' })).toBeInTheDocument()
  })

  it('shows the plain subdomain when the region route answers 503', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(undefined, undefined, { regionAnswer: () => new Response(null, { status: 503 }) })
    render(<SignUpEntry />)
    await screen.findByRole('radio', { name: /Approvals/ })

    expect(screen.queryByText(/vn\.example\.com/)).not.toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Subdomain' })).toBeEnabled()
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
    await waitFor(() => expect(subdomainStatus()).toHaveTextContent('This subdomain is free.'), {
      timeout: 5000,
    })

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByText('Enter an email address, such as name@example.com.')).toBeInTheDocument()
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
      altcha: btoa(
        JSON.stringify({
          algorithm: 'SHA-256',
          challenge: 'challenge-1',
          number: 42,
          salt: 'salt-1?expires=4102444800',
          signature: 'signature-1',
          took: 1,
        }),
      ),
    })

    await user.click(screen.getByRole('button', { name: 'Change email' }))

    expect(await screen.findByRole('heading', { name: 'Sign up' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Full name' })).toHaveValue('Ana Nguyen')
    expect(screen.getByRole('textbox', { name: 'Work email' })).toHaveValue('ana@example.com')
    expect(screen.getByRole('textbox', { name: 'Work email' })).toHaveFocus()
  })

  it('sends the solved challenge as altcha and solves a new one after a refusal', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    let refusals = 1
    const fetchMock = stubServer(undefined, () =>
      refusals-- > 0
        ? answer({ errors: { '/email': ['signUp.email.invalid'] } }, 400)
        : new Response(null, { status: 202 }),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)
    expect(challengeFetches(fetchMock)).toHaveLength(1)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    await waitFor(() => expect(challengeFetches(fetchMock)).toHaveLength(2))
    expect(solveChallenge).toHaveBeenCalledWith('challenge-1', 'salt-1?expires=4102444800', 'SHA-256', 100000)
    expect(altchaOf(signUpCalls(fetchMock)[0])).toEqual({
      algorithm: 'SHA-256',
      challenge: 'challenge-1',
      number: 42,
      salt: 'salt-1?expires=4102444800',
      signature: 'signature-1',
      took: 1,
    })

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    await screen.findByRole('heading', { name: 'Check your inbox' })
    expect(solveChallenge).toHaveBeenCalledWith('challenge-2', 'salt-2?expires=4102444800', 'SHA-256', 100000)
    expect(altchaOf(signUpCalls(fetchMock)[1])).toMatchObject({ challenge: 'challenge-2', signature: 'signature-2' })
  })

  it('sends the form without altcha when the control plane has no challenge', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const fetchMock = stubServer()
    const server = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input, init) =>
      input === '/api/sign-up/challenge' ? answer({}, 503) : server(input, init),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByRole('heading', { name: 'Check your inbox' })).toBeInTheDocument()
    expect(JSON.parse(String(signUpCalls(fetchMock)[0][1]?.body))).not.toHaveProperty('altcha')
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

  it.each([
    ['en-US', fieldNames.en, 'Sign up', 'This email is from a disposable mailbox service. Enter your work email.'],
    ['vi-VN', fieldNames.vi, 'Đăng ký', 'Không thể đăng ký bằng hộp thư dùng tạm. Nhập email công việc.'],
  ])('shows a refusal of a disposable email inside the email field in %s', async (language, names, submit, text) => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue(language)
    stubServer(undefined, () => answer({ errors: { '/email': ['signUp.email.disposable'] } }, 400))
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user, names)

    await user.click(screen.getByRole('button', { name: submit }))

    const email = screen.getByRole('textbox', { name: names.email })
    expect(await within(formItem(email)).findByText(text)).toBeInTheDocument()
    expect(email).toHaveFocus()
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

  it.each([
    ['address', { 'Retry-After': '600' }, 'Try again in 10 minutes.'],
    ['domain', {}, 'Try again tomorrow.'],
  ])('shows the %s limit of a 429 answer in the error alert', async (limit, headers, message) => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(
      undefined,
      () =>
        new Response(JSON.stringify({ status: 429, limit }), {
          status: 429,
          headers: { 'Content-Type': 'application/problem+json', ...headers },
        }),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(message)
    expect(alert.parentElement).toHaveFocus()
  })

  it('shows the failed text for a 429 answer without a wait', async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    stubServer(
      undefined,
      () =>
        new Response(JSON.stringify({ status: 429, limit: 'address' }), {
          status: 429,
          headers: { 'Content-Type': 'application/problem+json' },
        }),
    )
    const user = userEvent.setup()
    render(<SignUpEntry />)
    await fillForm(user)

    await user.click(screen.getByRole('button', { name: 'Sign up' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The sign-up could not be sent. Try again.')
  })
})
