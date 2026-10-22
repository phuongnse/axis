import { apiFetch } from './api'

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
  /** A sequence numbers the field on create. The form shows it read-only and never sends it. */
  sequence: boolean
  /** The server sets the field to the creating user's id. The form shows it read-only and never sends it. */
  creator: boolean
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

/** One typed input of a data source's filter. `values` is for an enum and `target` for a reference. */
export interface DataSourceParameter {
  name: string
  type: FieldType
  required: boolean
  labelKey: string | null
  values: string[] | null
  target: ReferenceTarget | null
}

/**
 * One projected column of a data source: its key in a row, and the type and label of the field its
 * path ends at. `values` is for an enum and `target` for a reference.
 */
export interface DataSourceColumn {
  name: string
  type: FieldType
  labelKey: string | null
  values: string[] | null
  target: ReferenceTarget | null
}

/**
 * The data source a table shows: the path of its rows endpoint, its root entity, its parameters,
 * its default page size and its columns in declaration order.
 */
export interface DataSourceMetadata {
  name: string
  rowsPath: string
  entity: string
  parameters: DataSourceParameter[]
  pageSize: number
  columns: DataSourceColumn[]
}

/** One field of a form section, by its declared name. A read-only field is shown and never sent. */
export interface FormFieldMetadata {
  name: string
  readOnly: boolean
}

/** One section of a form: the text key of its title and its fields in display order. */
export interface FormSectionMetadata {
  titleKey: string
  fields: FormFieldMetadata[]
}

/** The form a form widget lays its entity's fields out with: its sections in display order. */
export interface FormMetadata {
  name: string
  sections: FormSectionMetadata[]
}

/**
 * A `startProcess` action of a form widget: a button that starts `process` for the form's record by
 * posting to `instancesPath`, the start endpoint, so the SPA never builds that URL itself.
 */
export interface StartProcessAction {
  type: 'startProcess'
  process: string
  labelKey: string
  instancesPath: string
}

/**
 * A `navigate` action: opens the page `page`, by its declared name. `parameters` holds the expression
 * of each target page parameter the action gives, and `record` the expression of the record id a
 * form page opens, or `null`. `opensForm` says whether the target page holds a form widget, so the
 * SPA knows to open `/new` when no `record` is given.
 */
export interface NavigateAction {
  type: 'navigate'
  page: string
  labelKey: string
  parameters: Record<string, string>
  record: string | null
  opensForm: boolean
}

/** One action of a form widget, in page file order. */
export type WidgetAction = StartProcessAction | NavigateAction

/**
 * What the signed-in user may do with a table or form widget's entity, or with the root entity of
 * its data source. `actions` lists the granted entity actions in the order `read`, `create`,
 * `update`, `delete`. `createFields` and `updateFields` list the fields a create and an update may
 * write, in declaration order, and are `[]` when the action is not granted. This only shapes the UI:
 * the server checks every request.
 */
export interface WidgetAccess {
  actions: ('read' | 'create' | 'update' | 'delete')[]
  createFields: string[]
  updateFields: string[]
}

/**
 * One widget of a page. On a table or form, exactly one of `entity` and `dataSource` is set: the
 * widget shows all records of an entity, or the rows of a data source. `formPage` is set only on a
 * table that names the page holding its form. `form` is set only on a form widget that names a form,
 * and `entity` is then the form's entity with every field. A task inbox has neither an entity nor a
 * data source, and lists the signed-in user's tasks from `tasksPath`. A process inspector has neither,
 * and lists the instances of its `process` from `instancesPath`.
 */
export interface WidgetMetadata {
  type: 'table' | 'form' | 'taskInbox' | 'processInspector'
  formPage: string | null
  entity: EntityMetadata | null
  dataSource: DataSourceMetadata | null
  form: FormMetadata | null
  /** The task API path on a task inbox, and `null` on every other widget. */
  tasksPath: string | null
  /** The process a process inspector inspects, and `null` on every other widget. */
  process: string | null
  /** The inspection API's instance list path on a process inspector, and `null` on every other widget. */
  instancesPath: string | null
  /** The actions of a form widget the user may take, and `[]` on every other widget. */
  actions: WidgetAction[]
  /** The navigate actions a table shows on each row, and `[]` on every other widget. */
  rowActions: NavigateAction[]
  /** The navigate actions a table shows above its rows, and `[]` on every other widget. */
  toolbarActions: NavigateAction[]
  /** What the user may do with a table or form widget's entity, and `null` on a task inbox and a process inspector. */
  access: WidgetAccess | null
}

/** A widget over an entity. A form widget is always one: it is never bound to a data source. */
export type EntityWidgetMetadata = WidgetMetadata & { entity: EntityMetadata }

/** A page of an application site, as served by `GET /api/sites/{path}/pages/{page}`. */
export interface PageMetadata {
  name: string
  titleKey: string
  widgets: WidgetMetadata[]
}

export async function fetchSite(signal?: AbortSignal): Promise<SiteMetadata> {
  const response = await apiFetch('/api/site', { signal })
  if (!response.ok) {
    throw new Error(`Loading the site failed with status ${response.status}.`)
  }
  return (await response.json()) as SiteMetadata
}

/**
 * Loads the tenant's application sites, or returns `null` when nobody is signed in. The 401 does not
 * start sign-in, so the platform home page stays open to a signed-out person.
 */
export async function fetchSites(signal?: AbortSignal): Promise<SiteListItem[] | null> {
  const response = await apiFetch('/api/sites', { signal }, { signInOnUnauthorized: false })
  if (response.status === 401) {
    return null
  }
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
  const response = await apiFetch(url, { signal })
  if (response.status === 404) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Loading '${url}' failed with status ${response.status}.`)
  }
  return (await response.json()) as T
}
