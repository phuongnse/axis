import { suggestSubdomain } from '../../sign-up/suggestSubdomain'

// The answers of the `signUp` preview. Its states answer the same `/api/sign-up/` paths, so each state brings its own,
// and the preview shows one state at a time, see docs/reference/widget-previews.md#the-bundle.

/** The business name that the states with a subdomain type. */
export const signUpBusinessName = 'An Phát Trading'

const applicationsPath = '/api/sign-up/applications'
const subdomainPath = `/api/sign-up/subdomains/${suggestSubdomain(signUpBusinessName)}`

const applications = () => ({ applications: [{ id: 'approvals', titles: { en: 'Approvals', vi: 'Phê duyệt' } }] })
const free = () => ({ free: true, reason: null })
const unavailable = () => new Response(null, { status: 503 })

export const signUpFixtures = {
  empty: { [applicationsPath]: applications, [subdomainPath]: free },
  free: { [applicationsPath]: applications, [subdomainPath]: free },
  taken: { [applicationsPath]: applications, [subdomainPath]: () => ({ free: false, reason: 'taken' }) },
  checkFailed: { [applicationsPath]: applications, [subdomainPath]: unavailable },
  applicationsFailed: { [applicationsPath]: unavailable, [subdomainPath]: free },
} satisfies Record<string, Record<string, () => unknown>>
