/** One navigation entry. The label is a text key. */
export interface NavigationItem {
  key: string
  path: string
  labelKey: string
}

/** The locales a site offers. */
export interface SiteLocales {
  default: string
  fallback: string
  available: string[]
}

/** The platform site, as served by `GET /api/site`. */
export interface SiteMetadata {
  name: string
  titleKey: string
  locales: SiteLocales
  navigation: NavigationItem[]
}

/** One application site in the list served by `GET /api/sites`, with its title per available locale. */
export interface SiteListItem {
  path: string
  titleKey: string
  titles: Record<string, string>
}

/** An application site, as served by `GET /api/sites/{path}`. */
export interface ApplicationSite {
  path: string
  titleKey: string
  locales: SiteLocales
  navigation: { page: string; labelKey: string }[]
}

/** A page of an application site, as served by `GET /api/sites/{path}/pages/{page}`. */
export interface PageMetadata {
  name: string
  titleKey: string
  widgets: { type: 'table' | 'form'; formPage: string | null; entity: unknown }[]
}

export async function fetchSite(signal?: AbortSignal): Promise<SiteMetadata> {
  const response = await fetch('/api/site', { signal })
  if (!response.ok) {
    throw new Error(`Loading the site failed with status ${response.status}.`)
  }
  return (await response.json()) as SiteMetadata
}

export async function fetchSites(signal?: AbortSignal): Promise<SiteListItem[]> {
  const response = await fetch('/api/sites', { signal })
  if (!response.ok) {
    throw new Error(`Loading the sites failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { sites: SiteListItem[] }
  return body.sites
}

/** Loads an application site, or returns `null` when no site has the path. */
export async function fetchApplicationSite(path: string, signal?: AbortSignal): Promise<ApplicationSite | null> {
  return fetchOrNull<ApplicationSite>(`/api/sites/${encodeURIComponent(path)}`, signal)
}

/** Loads a page of an application site, or returns `null` when the site has no such page. */
export async function fetchPage(path: string, page: string, signal?: AbortSignal): Promise<PageMetadata | null> {
  return fetchOrNull<PageMetadata>(`/api/sites/${encodeURIComponent(path)}/pages/${encodeURIComponent(page)}`, signal)
}

async function fetchOrNull<T>(url: string, signal?: AbortSignal): Promise<T | null> {
  const response = await fetch(url, { signal })
  if (response.status === 404) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Loading '${url}' failed with status ${response.status}.`)
  }
  return (await response.json()) as T
}
