import { suggestSubdomain } from '../../sign-up/suggestSubdomain'

// The answers of the `signUp` preview. Its states answer the same `/api/sign-up/` paths, so each state brings its own,
// and the preview shows one state at a time, see docs/reference/widget-previews.md#the-bundle.

/** The business name that the states with a subdomain type. */
export const signUpBusinessName = 'Contoso Trading'

const sendPath = '/api/sign-up'
const resendPath = '/api/sign-up/resend'
const applicationsPath = '/api/sign-up/applications'
const documentsPath = '/api/sign-up/documents'
const regionPath = '/api/sign-up/region'
const subdomainPath = `/api/sign-up/subdomains/${suggestSubdomain(signUpBusinessName)}`

const applications = () => ({
  applications: [
    {
      id: 'approvals',
      titles: { en: 'Approvals', vi: 'Phê duyệt' },
      descriptions: {
        en: 'Requests that people send and approvers approve or reject.',
        vi: 'Yêu cầu do nhân viên gửi và người duyệt phê duyệt hoặc từ chối.',
      },
    },
  ],
})
const document = (kind: string, version: string) => ({
  version,
  urls: {
    en: `https://axis.example/legal/${kind}/${version}/en`,
    vi: `https://axis.example/legal/${kind}/${version}/vi`,
  },
})
const documents = () => ({
  terms: document('terms', '2026-11-01'),
  dpa: document('dpa', '2026-11-01'),
  privacy: document('privacy', '2026-10-15'),
  licencesUrl: 'https://axis.example/licences',
})
const region = () => ({ tenantDomain: 'vn.example.com' })
const problem = (status: number, errors: Record<string, string[]>) =>
  new Response(JSON.stringify({ errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })
// The same answer for a new email and for one that already had a trial.
const accepted = () =>
  new Response(JSON.stringify({ resend: 'r-7c1e' }), { status: 202, headers: { 'Content-Type': 'application/json' } })
const free = () => ({ free: true, reason: null })
const unavailable = () => new Response(null, { status: 503 })

/** What every state answers the same: the region, its documents and its applications. */
const page = { [regionPath]: region, [documentsPath]: documents, [applicationsPath]: applications }

export const signUpFixtures = {
  empty: { ...page, [subdomainPath]: free },
  free: { ...page, [subdomainPath]: free },
  taken: { ...page, [subdomainPath]: () => ({ free: false, reason: 'taken' }) },
  checkFailed: { ...page, [subdomainPath]: unavailable },
  applicationsFailed: { ...page, [applicationsPath]: unavailable, [subdomainPath]: free },
  sentEmpty: { ...page, [subdomainPath]: free },
  emailRefused: {
    ...page,
    [subdomainPath]: free,
    [sendPath]: () => problem(400, { '/email': ['signUp.email.disposable'] }),
  },
  termsChanged: {
    ...page,
    [subdomainPath]: free,
    [sendPath]: () => problem(409, { '/termsVersion': ['signUp.terms.changed'] }),
  },
  tooMany: {
    ...page,
    [subdomainPath]: free,
    [sendPath]: () =>
      new Response(JSON.stringify({ limit: 'address' }), {
        status: 429,
        headers: { 'Content-Type': 'application/problem+json', 'Retry-After': '1800' },
      }),
  },
  sendFailed: { ...page, [subdomainPath]: free, [sendPath]: unavailable },
  inbox: { ...page, [subdomainPath]: free, [sendPath]: accepted },
  resent: {
    ...page,
    [subdomainPath]: free,
    [sendPath]: accepted,
    [resendPath]: () => new Response(null, { status: 202 }),
  },
  resendFailed: { ...page, [subdomainPath]: free, [sendPath]: accepted, [resendPath]: unavailable },
} satisfies Record<string, Record<string, () => unknown>>

const verificationPath = '/api/sign-up/verification'
const confirmPath = '/api/sign-up/confirm'
const pending = () => ({
  state: 'pending',
  email: 'alex.morgan@contoso.example',
  businessName: signUpBusinessName,
  address: `${suggestSubdomain(signUpBusinessName)}.vn.example.com`,
})
/** What every verify state answers the same: the region's documents, for the legal line. */
const verifyPage = { [documentsPath]: documents }

export const verifyFixtures = {
  confirm: { ...verifyPage, [verificationPath]: pending },
  checking: { ...verifyPage, [verificationPath]: () => new Promise<never>(() => {}) },
  checkFailed: { ...verifyPage, [verificationPath]: unavailable },
  confirmFailed: { ...verifyPage, [verificationPath]: pending, [confirmPath]: unavailable },
  confirmed: { ...verifyPage, [verificationPath]: () => ({ state: 'confirmed' }) },
  expired: { ...verifyPage, [verificationPath]: () => ({ state: 'expired' }) },
} satisfies Record<string, Record<string, () => unknown>>

const progressPath = '/api/sign-up/p-3d9a'
const at = (step: string) => () => ({ step, address: `${suggestSubdomain(signUpBusinessName)}.vn.example.com` })

export const progressFixtures = {
  verified: { ...verifyPage, [progressPath]: at('verified') },
  creating: { ...verifyPage, [progressPath]: at('creating') },
  delayed: { ...verifyPage, [progressPath]: at('delayed') },
  ready: { ...verifyPage, [progressPath]: at('ready') },
  unknown: { ...verifyPage, [progressPath]: () => new Response(null, { status: 404 }) },
} satisfies Record<string, Record<string, () => unknown>>
