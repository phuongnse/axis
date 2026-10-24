import type { ApiGuard } from './api'
import { formStatePath, type FieldState, type FormStateAnswer } from './formState'
import type { InstancePage } from './inspection'
import type { FieldValue, RecordItem, RecordRow } from './records'
import type { DataSourceMetadata, FieldMetadata, PageMetadata, PageParameter, ReferenceTarget } from './site'
import type { TaskPage } from './tasks'
import { leafWidgets } from './widgetTree'

/** What the placeholder value of a field or data source column is made from. */
type Typed = Pick<FieldMetadata, 'name' | 'type' | 'values'> &
  Partial<Pick<FieldMetadata, 'maxLength' | 'scale' | 'target' | 'fields'>>

/** Builds the placeholder record number `n` (from 1) with the id the caller asks for. */
type RecordBuilder = (n: number, id: string) => RecordItem

const recordCount = 3
const childRowCount = 2
const referenceId = '00000000-0000-4000-8000-000000000101'
const defaultPage = 1
const defaultPageSize = 20

/** The constant id of placeholder record number `n`. */
function placeholderId(n: number): string {
  return `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
}

function value(field: Typed, n: number): FieldValue {
  const day = String(n).padStart(2, '0')
  switch (field.type) {
    case 'text':
      return `Sample ${field.name} ${n}`.slice(0, field.maxLength ?? undefined)
    case 'integer':
      return String(n * 10)
    case 'decimal':
      return (n * 1250.5).toFixed(field.scale ?? 2)
    case 'boolean':
      return n % 2 === 1
    case 'date':
      return `2026-03-${day}`
    case 'date-time':
      return `2026-03-${day}T09:30:00.000000Z`
    case 'enum':
      return field.values?.[(n - 1) % field.values.length] ?? null
    case 'reference':
      return referenceId
    case 'child-collection':
      return Array.from({ length: childRowCount }, (_, row) => values(field.fields ?? [], row + 1) as RecordRow)
  }
}

function values(fields: readonly Typed[], n: number): Record<string, FieldValue> {
  return Object.fromEntries(fields.map((field) => [field.name, value(field, n)]))
}

function labels(fields: readonly Typed[]): Record<string, string> {
  return Object.fromEntries(
    fields
      .filter((field) => field.type === 'reference')
      .map((field) => [field.name, `Sample ${field.target?.entity ?? field.name}`]),
  )
}

function recordBuilder(fields: readonly Typed[]): RecordBuilder {
  return (n, id) => ({ id, version: 1, values: values(fields, n), labels: labels(fields) })
}

/**
 * The value of each page parameter, as the address would carry it, made from the parameter's type. A reference gets
 * the id of a placeholder record, so a detail or a bound widget has something to draw.
 */
export function placeholderPageValues(parameters: readonly PageParameter[]): Record<string, string> {
  return Object.fromEntries(parameters.map((parameter) => [parameter.name, String(value(parameter, 1))]))
}

/** The builder for a reference target's records, which hold only the display field. */
function targetBuilder(target: ReferenceTarget): RecordBuilder {
  return (n, id) => ({
    id,
    version: 1,
    values: target.displayField === null ? {} : { [target.displayField]: `Sample ${target.entity} ${n}` },
    labels: {},
  })
}

/** Every reference target among fields, down into child collections. */
function targetsOf(fields: readonly FieldMetadata[]): ReferenceTarget[] {
  return fields.flatMap((field) => [...(field.target ? [field.target] : []), ...targetsOf(field.fields ?? [])])
}

function pageOf<T>(items: T[], query: URLSearchParams) {
  const number = (name: string, fallback: number) => {
    const parsed = Number.parseInt(query.get(name) ?? '', 10)
    return parsed > 0 ? parsed : fallback
  }
  return {
    items,
    page: number('page', defaultPage),
    pageSize: number('pageSize', defaultPageSize),
    totalCount: items.length,
  }
}

function ok(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function notFound(): Response {
  return new Response(
    JSON.stringify({ status: 404, title: 'The page designer does not send this request.', errors: {} }),
    {
      status: 404,
      headers: { 'Content-Type': 'application/problem+json' },
    },
  )
}

/** How many rows a data source answers: one total row for a kpi, a few groups for any other widget. */
function rowCount(dataSource: DataSourceMetadata, kind: 'kpi' | 'chart' | 'other', category: string | null): number {
  if (kind === 'kpi') {
    return 1
  }
  const enumValues = kind === 'chart' ? dataSource.columns.find((column) => column.name === category)?.values : null
  return enumValues?.length ? Math.min(recordCount, enumValues.length) : recordCount
}

/**
 * The guard that answers a page's `/api/apps/` calls with placeholder data made from the page's
 * metadata. Values depend on the field type and are fixed, so the same request always gets the same
 * answer. A write, and a path the page does not use, gets a local 404.
 */
export function placeholderApi(metadata: PageMetadata): ApiGuard {
  const recordSources = new Map<string, RecordBuilder>()
  const exact = new Map<string, (query: URLSearchParams) => unknown>()
  const widgets = leafWidgets(metadata.widgets)

  // Reference targets first, so an entity the page shows overrides the display-field-only records.
  for (const widget of widgets) {
    const targets = [
      ...targetsOf(widget.entity?.fields ?? []),
      ...(widget.dataSource?.parameters ?? []).flatMap((parameter) => (parameter.target ? [parameter.target] : [])),
    ]
    for (const target of targets) {
      if (!recordSources.has(target.recordsPath)) {
        recordSources.set(target.recordsPath, targetBuilder(target))
      }
    }
  }
  for (const widget of widgets) {
    // A placeholder widget of a draft page lacks these, so read them defensively.
    const entity = widget.entity ?? null
    const dataSource = widget.dataSource ?? null
    const form = widget.form ?? null
    const kpi = widget.kpi ?? null
    const chart = widget.chart ?? null
    if (entity) {
      recordSources.set(entity.recordsPath, recordBuilder(entity.fields))
      if (form) {
        const state: FormStateAnswer = {
          sections: form.sections.flatMap((section) =>
            section.name === null ? [] : [{ name: section.name, visible: true }],
          ),
          fields: Object.fromEntries(
            entity.fields.map((field): [string, FieldState] => [
              field.name,
              { visible: true, required: field.required, readOnly: false },
            ]),
          ),
          errors: {},
        }
        exact.set(`POST ${formStatePath(entity.recordsPath, form.name)}`, () => state)
      }
    }
    if (dataSource) {
      const count = rowCount(dataSource, kpi ? 'kpi' : chart ? 'chart' : 'other', chart?.category ?? null)
      const key = `GET ${dataSource.rowsPath}`
      if (!exact.has(key)) {
        const build = recordBuilder(dataSource.columns)
        exact.set(key, (query) =>
          pageOf(
            Array.from({ length: count }, (_, index) => build(index + 1, placeholderId(index + 1))),
            query,
          ),
        )
      }
    }
    if (widget.tasksPath) {
      exact.set(`GET ${widget.tasksPath}`, (query): TaskPage => pageOf([], query))
    }
    if (widget.instancesPath) {
      exact.set(`GET ${widget.instancesPath}`, (query): InstancePage => pageOf([], query))
    }
  }

  return (input, init) => {
    const [path, queryText = ''] = input.split('?')
    const method = (init?.method ?? 'GET').toUpperCase()
    const query = new URLSearchParams(queryText)
    const route = exact.get(`${method} ${path}`)
    if (route) {
      return ok(route(query))
    }
    if (method === 'GET') {
      for (const [recordsPath, build] of recordSources) {
        if (path === recordsPath) {
          return ok(
            pageOf(
              Array.from({ length: recordCount }, (_, index) => build(index + 1, placeholderId(index + 1))),
              query,
            ),
          )
        }
        const rest = path.startsWith(`${recordsPath}/`) ? path.slice(recordsPath.length + 1).split('/') : []
        if (rest.length === 1 && rest[0]) {
          return ok(build(1, rest[0]))
        }
        if (rest.length === 2 && rest[0] && rest[1] === 'history') {
          return ok({ items: [], page: defaultPage, pageSize: defaultPageSize, totalCount: 0 })
        }
      }
    }
    return notFound()
  }
}
