import { apiFetch } from './api'
import type { ChartKind } from './chartOption'

/** One navigation entry. The label is a text key. */
export interface NavigationItem {
  key: string
  path: string
  labelKey: string
  /** An Axis icon name, see navigationIcons. */
  icon?: string | null
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
  /** The lower-case path of the site whose own host serves the SPA, or `null` or missing when the host belongs to no site. */
  hostSite?: string | null
}

/** The application of a listed site, with its title per locale it has texts for. */
export interface SiteApplication {
  name: string
  titles: Record<string, string>
}

/** One application site in the list served by `GET /api/sites`, with its title per available locale. */
export interface SiteListItem {
  path: string
  titleKey: string
  titles: Record<string, string>
  application: SiteApplication
}

/** The site title in the given locale, or in the site's first locale when it lacks that one. */
export function siteTitle(site: SiteListItem, locale: string): string {
  return site.titles[locale] ?? Object.values(site.titles)[0] ?? site.path
}

/** The application title in the given locale, or in the application's first locale when it lacks that one. */
export function applicationTitle(app: SiteApplication, locale: string): string {
  return app.titles[locale] ?? Object.values(app.titles)[0] ?? app.name
}

/** An application site, as served by `GET /api/sites/{path}`. */
export interface ApplicationSite {
  path: string
  titleKey: string
  locales: SiteLocales
  navigation: { page: string; labelKey: string; icon?: string | null }[]
  /** The address of the application's notification routes. */
  notificationsPath?: string | null
}

/** A field type as written in entity files. */
export type FieldType =
  | 'text'
  | 'integer'
  | 'decimal'
  | 'money'
  | 'boolean'
  | 'date'
  | 'date-time'
  | 'enum'
  | 'reference'
  | 'references'
  | 'child-collection'
  | 'file'

/** The entity a reference or references field points to, its display field and the path of its record API. */
export interface ReferenceTarget {
  entity: string
  displayField: string | null
  recordsPath: string
}

/**
 * One field of an entity. Properties the field's type does not have are `null`: `target` is for a
 * reference and references, and `fields` holds a child collection's child fields in declaration order.
 */
export interface FieldMetadata {
  name: string
  type: FieldType
  labelKey: string | null
  /** The text key of the field's help text, or `null` when it has none. Older metadata may leave it out. */
  helpKey?: string | null
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
  /** The fixed currency code of a money field, or missing or `null` otherwise. */
  currency?: string | null
  /** The name of the field that holds the currency code of a money field, or missing or `null` otherwise. */
  currencyField?: string | null
  /** The lowest accepted value as the literal was written, such as `10`, `0.5` or `2026-01-01`. */
  min: string | null
  /** The highest accepted value, written like `min`. */
  max: string | null
  /** The fewest characters a text value may have. */
  minLength: number | null
  values: string[] | null
  /** The text key of each enum value's label, by value, or missing or `null` when the enum has no labels. */
  valueLabelKeys?: Record<string, string> | null
  /** The largest accepted file of a file field in bytes, or missing or `null` when it has no limit of its own. */
  maxSize?: number | null
  /** The media types a file field accepts, or missing or `null` when it accepts any. */
  accept?: string[] | null
  /** The path of a file field's upload route, or missing or `null` when it has none, as for a field of a module's entity. */
  uploadPath?: string | null
  /** The path to which a client appends a file's id to download it, or missing or `null` on a field that is not a file. */
  downloadPath?: string | null
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
  /** The text key of each enum value's label, by value, or missing or `null` when the enum has no labels. */
  valueLabelKeys?: Record<string, string> | null
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
  /** The text key of each enum value's label, by value, or missing or `null` when the enum has no labels. */
  valueLabelKeys?: Record<string, string> | null
  target: ReferenceTarget | null
  /** The fixed currency code of a money column or measure, or missing or `null` otherwise. */
  currency?: string | null
  /** The name of the column that holds the currency code of a money column or measure, or missing or `null` otherwise. */
  currencyColumn?: string | null
}

/**
 * The data source a table shows: the path of its rows endpoint, its root entity, its parameters,
 * its default page size, whether its rows route takes `search` and its columns in declaration order.
 */
export interface DataSourceMetadata {
  name: string
  rowsPath: string
  entity: string
  parameters: DataSourceParameter[]
  pageSize: number
  /** Whether the rows route takes `search`. */
  search: boolean
  columns: DataSourceColumn[]
}

/** One field of a form section, by its declared name. A read-only field is shown and never sent. */
export interface FormFieldMetadata {
  name: string
  readOnly: boolean
  /** The field's width in twelfths of the row, 12 when the form leaves it out. Older metadata may leave it out. */
  width?: number
  /** The element's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
}

/** One section of a form: the text key of its title and its fields in display order. */
export interface FormSectionMetadata {
  /** The section's name, which a step lists, or `null` when it has none. */
  name: string | null
  titleKey: string
  fields: FormFieldMetadata[]
  /** The element's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
}

/** One wizard step of a form: the text key of its title and the names of its sections. */
export interface FormStepMetadata {
  titleKey: string
  sections: string[]
  /** The element's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
}

/**
 * The form a form widget lays its entity's fields out with: its sections in display order, and its
 * wizard steps in order, which are empty when the form declares none.
 */
export interface FormMetadata {
  name: string
  sections: FormSectionMetadata[]
  steps: FormStepMetadata[]
}

/**
 * A `startProcess` action of a form widget or a table row: a button that starts `process` for the
 * form's record or the row's record by posting to `instancesPath`, the start endpoint, so the SPA
 * never builds that URL itself. `visibleWhen` is the boolean expression over the row that decides
 * whether a row action shows, or `null` when it always shows.
 */
export interface StartProcessAction {
  type: 'startProcess'
  process: string
  labelKey: string
  instancesPath: string
  visibleWhen: string | null
}

/**
 * A `navigate` action: opens the page `page`, by its declared name. `parameters` holds the expression
 * of each target page parameter the action gives, and `record` the expression of the record id a
 * form page opens, or `null`. `opensForm` says whether the target page holds a form widget, so the
 * SPA knows to open `/new` when no `record` is given. `paths` maps each reference along a path
 * expression, by its dotted declared names (`customer`, `customer.parent`), to the record API path
 * of that reference's target entity, so the SPA reads the records on click. `visibleWhen` is as on a
 * `startProcess` action.
 */
export interface NavigateAction {
  type: 'navigate'
  page: string
  labelKey: string
  parameters: Record<string, string>
  record: string | null
  opensForm: boolean
  paths: Record<string, string>
  visibleWhen: string | null
}

/** One action of a form widget or a table row, in page file order. */
export type WidgetAction = StartProcessAction | NavigateAction

/**
 * A `delete` row action of a table: a button that deletes the row's record after a confirmation by
 * sending `DELETE {recordsPath}/{id}`. `recordsPath` is the record API path of the table's entity, or
 * of its data source's root entity, so the SPA never builds that URL itself. `visibleWhen` is as on a
 * `startProcess` action.
 */
export interface DeleteAction {
  type: 'delete'
  labelKey: string
  recordsPath: string
  visibleWhen: string | null
}

/** One action of a table row, in page file order. Only a row can hold a `delete`. */
export type RowAction = WidgetAction | DeleteAction

/**
 * What the signed-in user may do with a table or form widget's entity, or with the root entity of
 * its data source. `actions` lists the granted entity actions in the order `read`, `create`,
 * `update`, `delete`, then `import` on a table over an entity, for a user with `import` and `create`.
 * `createFields` and `updateFields` list the fields a create and an update may write, in
 * declaration order, and are `[]` when the action is not granted. This only shapes the UI: the
 * server checks every request.
 */
export interface WidgetAccess {
  actions: ('read' | 'create' | 'update' | 'delete' | 'export' | 'import')[]
  createFields: string[]
  updateFields: string[]
}

/**
 * One widget of a page. On a table or form, exactly one of `entity` and `dataSource` is set: the
 * widget shows all records of an entity, or the rows of a data source. `form` is set only on a form
 * widget that names a form, and `entity` is then the form's entity with every field. A task inbox has
 * neither an entity nor a data source, and lists the signed-in user's tasks from `tasksPath`. A process
 * inspector has neither, and lists the instances of its `process` from `instancesPath` and its open dead
 * letters from `deadLettersPath`.
 */
export interface WidgetMetadata {
  type: 'table' | 'form' | 'taskInbox' | 'processInspector' | 'kpi' | 'chart' | 'detail'
  /** The widget's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
  entity: EntityMetadata | null
  dataSource: DataSourceMetadata | null
  form: FormMetadata | null
  /** The task API path on a task inbox, and `null` on every other widget. */
  tasksPath: string | null
  /** The process a process inspector inspects, and `null` on every other widget. */
  process: string | null
  /** The inspection API's instance list path on a process inspector, and `null` on every other widget. */
  instancesPath: string | null
  /** The dead letter list path on a process inspector, and `null` on every other widget. */
  deadLettersPath: string | null
  /** The actions of a form or detail widget the user may take, and `[]` on every other widget. */
  actions: WidgetAction[]
  /** The `startProcess`, navigate and `delete` actions a table shows on each row, and `[]` on every other widget. */
  rowActions: RowAction[]
  /** The navigate actions a table shows above its rows, and `[]` on every other widget. */
  toolbarActions: NavigateAction[]
  /**
   * The page parameter that gives each data source parameter its value on a table, keyed by the data
   * source parameter's name, and `{}` on every other widget. A bound parameter has no filter input.
   */
  parameters: Record<string, string>
  /** What the user may do with a table, form or detail widget's entity, and `null` on a task inbox and a process inspector. */
  access: WidgetAccess | null
  /** What a detail widget shows, and `null` on every other widget. A detail has an `entity` and maybe a `form`. */
  detail: DetailMetadata | null
  /** What a kpi widget shows, and `null` on every other widget. A kpi also has a `dataSource` and no entity. */
  kpi: KpiMetadata | null
  /** What a chart widget draws, and `null` on every other widget. A chart also has a `dataSource` and no entity. */
  chart: ChartMetadata | null
}

/** One series of a chart widget: a measure of its data source and the text key of its label. */
export interface ChartMeasureMetadata {
  name: string
  labelKey: string
}

/**
 * What a chart widget draws of the groups of its data source: the group field `category` names each group, and
 * every measure is one series. `titleKey` is the chart's title and accessible name.
 */
export interface ChartMetadata {
  kind: ChartKind
  titleKey: string
  category: string
  measures: ChartMeasureMetadata[]
}

/** What a detail widget shows: `record` names the page parameter whose value is the id of the record. */
export interface DetailMetadata {
  record: string
}

/** The second measure of a kpi widget and its text key. */
export interface KpiComparisonMetadata {
  measure: string
  labelKey: string
}

/**
 * What a kpi widget shows of its data source's one total row: a measure and its label, an optional
 * second measure to compare it with, and an optional navigate action the whole tile opens.
 */
export interface KpiMetadata {
  measure: string
  labelKey: string
  comparison: KpiComparisonMetadata | null
  navigate: NavigateAction | null
}

/** A `columns` widget: columns side by side, each `width` twelfths of the row wide. */
export interface ColumnsWidgetMetadata {
  type: 'columns'
  /** The widget's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
  columns: { width: number; widgets: PageWidgetMetadata[] }[]
}

/** A `tabs` widget: one tab per entry, each with the text key of its label. */
export interface TabsWidgetMetadata {
  type: 'tabs'
  /** The widget's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
  tabs: { labelKey: string; widgets: PageWidgetMetadata[] }[]
}

/** A `section` widget: a titled group of widgets that may start closed. */
export interface SectionWidgetMetadata {
  type: 'section'
  /** The widget's JSON Pointer in the draft. Only the draft page metadata route sets it. */
  path?: string
  titleKey: string
  collapsed: boolean
  widgets: PageWidgetMetadata[]
}

/** A container widget, which holds lists of other widgets. */
export type ContainerWidgetMetadata = ColumnsWidgetMetadata | TabsWidgetMetadata | SectionWidgetMetadata

/** One entry of a page's widget tree: a widget that shows something, or a container around other widgets. */
export type PageWidgetMetadata = WidgetMetadata | ContainerWidgetMetadata

/**
 * A widget of a draft page that does not compile, as the draft page metadata route answers it. `type` is the draft's
 * `type`, or `null` when that is not a string, and `path` is the widget's JSON Pointer in the draft. Only the page
 * designer's canvas gets one. The site never does.
 */
export interface PlaceholderWidgetMetadata {
  type: string | null
  path: string
  placeholder: true
}

/** Whether a widget of a draft page is a {@link PlaceholderWidgetMetadata}. */
export function isPlaceholder(widget: unknown): widget is PlaceholderWidgetMetadata {
  return typeof widget === 'object' && widget !== null && (widget as { placeholder?: unknown }).placeholder === true
}

/** A widget over an entity. A form widget is always one: it is never bound to a data source. */
export type EntityWidgetMetadata = WidgetMetadata & { entity: EntityMetadata }

/** One typed value a page reads from its URL. `values` is for an enum and `target` for a reference. */
export interface PageParameter {
  name: string
  type: FieldType
  required: boolean
  values: string[] | null
  target: ReferenceTarget | null
}

/** A page of an application site, as served by `GET /api/sites/{path}/pages/{page}`. */
export interface PageMetadata {
  name: string
  titleKey: string
  parameters: PageParameter[]
  widgets: PageWidgetMetadata[]
}

export async function fetchSite(signal?: AbortSignal): Promise<SiteMetadata> {
  const response = await apiFetch('/api/site', { signal })
  if (!response.ok) {
    throw new Error(`Loading the site failed with status ${response.status}.`)
  }
  return (await response.json()) as SiteMetadata
}

/** Loads the sites of the signed-in person, or returns `null` on a 401, which shows no sign-in prompt. */
export async function fetchSites(signal?: AbortSignal): Promise<SiteListItem[] | null> {
  const response = await apiFetch('/api/sites', { signal }, { promptSignInOnUnauthorized: false })
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
