import { suggestSubdomain } from '../../sign-up/suggestSubdomain'

// The answers of the `signUp` preview. Its states answer the same `/api/sign-up/` paths, so each state brings its own,
// and the preview shows one state at a time, see docs/reference/widget-previews.md#the-bundle.

/** The business name that the states with a subdomain type. */
export const signUpBusinessName = 'An Phát Trading'

const sendPath = '/api/sign-up'
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
} satisfies Record<string, Record<string, () => unknown>>
