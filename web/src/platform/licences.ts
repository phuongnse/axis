import { apiFetch } from './api'

/** One shipped package. The version is missing when nobody is signed in. */
export interface LicencePackage {
  name: string
  licence: string
  version?: string
}

/** The npm and NuGet packages Axis ships, as in `GET /api/licences` and the public `/licences.json`. */
export interface Licences {
  npm: LicencePackage[]
  nuget: LicencePackage[]
}

/** Where Axis's source is, which the AGPL requires Axis to offer to every person who uses it over a network. */
export const axisSourceUrl = 'https://github.com/phuongnse/axis'

/** The text of Axis's licence. */
export const axisLicenceUrl = 'https://spdx.org/licenses/AGPL-3.0-only.html'

/**
 * Loads the packages from `GET /api/licences`, which holds their versions, or on a 401 from the public
 * `/licences.json`, which holds names and licences only. A 401 here never shows the sign-in prompt.
 */
export async function fetchLicences(signal?: AbortSignal): Promise<Licences> {
  const response = await apiFetch('/api/licences', { signal }, { promptSignInOnUnauthorized: false })
  if (response.ok) {
    return (await response.json()) as Licences
  }
  if (response.status !== 401) {
    throw new Error(`Loading the licences failed with status ${response.status}.`)
  }
  const publicResponse = await fetch('/licences.json', { signal })
  if (!publicResponse.ok) {
    throw new Error(`Loading the public licences failed with status ${publicResponse.status}.`)
  }
  return (await publicResponse.json()) as Licences
}

/** The source at the commit when the version's build metadata is 7 to 40 hex characters, else the repository root. */
export function sourceUrl(version: string | null): string {
  const metadata = version === null ? '' : version.slice(version.indexOf('+') + 1)
  return version?.includes('+') && /^[0-9a-f]{7,40}$/i.test(metadata)
    ? `${axisSourceUrl}/tree/${metadata}`
    : axisSourceUrl
}

/**
 * The page of the licence: spdx.org for one SPDX identifier, the address itself for an https URL, else `null`, as for an
 * expression such as `MIT OR Apache-2.0`, a file name or `UNKNOWN`.
 */
export function licenceUrl(licence: string): string | null {
  if (/^https:\/\/\S+$/.test(licence)) {
    return licence
  }
  return /^[A-Za-z0-9.+-]+$/.test(licence) && !isFileName(licence) ? `https://spdx.org/licenses/${licence}.html` : null
}

// A nuspec that names its licence file gives `LICENSE.txt` and the like. UNKNOWN is the build's mark for no licence.
function isFileName(licence: string): boolean {
  return licence === 'UNKNOWN' || /^(licen[cs]e|copying)/i.test(licence) || /\.(txt|md|rst|html?)$/i.test(licence)
}
