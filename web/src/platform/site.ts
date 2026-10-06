/** One navigation entry. The label is a text key. */
export interface NavigationItem {
  key: string
  path: string
  labelKey: string
}

/** The site the shell renders, as served by `GET /api/site`. */
export interface SiteMetadata {
  name: string
  titleKey: string
  locales: {
    default: string
    fallback: string
    available: string[]
  }
  navigation: NavigationItem[]
}

export async function fetchSite(signal?: AbortSignal): Promise<SiteMetadata> {
  const response = await fetch('/api/site', { signal })
  if (!response.ok) {
    throw new Error(`Loading the site failed with status ${response.status}.`)
  }
  return (await response.json()) as SiteMetadata
}
