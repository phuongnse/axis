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

/** A field type as written in entity files. */
export type FieldType =
  'text' | 'integer' | 'decimal' | 'boolean' | 'date' | 'date-time' | 'enum' | 'reference' | 'child-collection'

/** The entity a reference field points to, its display field and the path of its record API. */
export interface ReferenceTarget {
  entity: string
  displayField: string | null
  recordsPath: string
}

/**
 * One field of an entity. Properties the field's type does not have are `null`: `target` is for a
 * reference, and `fields` holds a child collection's child fields in declaration order.
 */
export interface FieldMetadata {
  name: string
  type: FieldType
  labelKey: string | null
  required: boolean
  unique: boolean
  /** The server computes the value on every write. The form shows it read-only and never sends it. */
  computed: boolean
  maxLength: number | null
  precision: number | null
  scale: number | null
  values: string[] | null
  target: ReferenceTarget | null
  fields: FieldMetadata[] | null
}

/** The entity a widget shows, with its fields in declaration order and the path of its record API. */
export interface EntityMetadata {
  name: string
  labelKey: string | null
  displayField: string | null
  recordsPath: string
  fields: FieldMetadata[]
}

/** One widget of a page. `formPage` is set only on a table that names the page holding its form. */
export interface WidgetMetadata {
  type: 'table' | 'form'
  formPage: string | null
  entity: EntityMetadata
}

/** A page of an application site, as served by `GET /api/sites/{path}/pages/{page}`. */
export interface PageMetadata {
  name: string
  titleKey: string
  widgets: WidgetMetadata[]
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
