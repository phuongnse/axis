import { suggestSubdomain } from '../../sign-up/suggestSubdomain'

// The answers of the `signUp` preview. Its states answer the same `/api/sign-up/` paths, so each state brings its own,
// and the preview shows one state at a time, see docs/reference/widget-previews.md#the-bundle.

/** The business name that the states with a subdomain type. */
export const signUpBusinessName = 'An Phát Trading'

const applicationsPath = '/api/sign-up/applications'
const documentsPath = '/api/sign-up/documents'
const subdomainPath = `/api/sign-up/subdomains/${suggestSubdomain(signUpBusinessName)}`

const applications = () => ({ applications: [{ id: 'approvals', titles: { en: 'Approvals', vi: 'Phê duyệt' } }] })
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
const free = () => ({ free: true, reason: null })
const unavailable = () => new Response(null, { status: 503 })

export const signUpFixtures = {
  empty: { [documentsPath]: documents, [applicationsPath]: applications, [subdomainPath]: free },
  free: { [documentsPath]: documents, [applicationsPath]: applications, [subdomainPath]: free },
  taken: {
    [documentsPath]: documents,
    [applicationsPath]: applications,
    [subdomainPath]: () => ({ free: false, reason: 'taken' }),
  },
  checkFailed: { [documentsPath]: documents, [applicationsPath]: applications, [subdomainPath]: unavailable },
  applicationsFailed: { [documentsPath]: documents, [applicationsPath]: unavailable, [subdomainPath]: free },
} satisfies Record<string, Record<string, () => unknown>>
