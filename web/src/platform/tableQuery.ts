import type { FieldMetadata } from './site'

/** The page sizes a table offers. */
export const pageSizes = [10, 20, 50, 100]

const defaultPage = 1
/** The record API's default page size. A data source declares its own. */
export const recordPageSize = 20

/** The page sizes a table offers, with its default page size among them, in ascending order. */
export function tablePageSizes(defaultPageSize: number = recordPageSize): number[] {
  return pageSizes.includes(defaultPageSize) ? pageSizes : [...pageSizes, defaultPageSize].sort((a, b) => a - b)
}

/** The column a table is sorted by. */
export interface TableSort {
  field: string
  descending: boolean
}

/** The paging, sorting and data source parameter values of a table, as kept in the URL. */
export interface TableQuery {
  page: number
  pageSize: number
  sort: TableSort | null
  /** The data source parameter values by parameter name, in declaration order. None is empty. */
  parameters?: Record<string, string>
}

/**
 * Reads `page`, `pageSize`, `sort` and the values of `parameterNames` from the URL. An invalid value
 * falls back to its default: a `page` that is not a positive integer, a `pageSize` the table does
 * not offer, or a `sort` that names no field or a reference field. `fields` are the table's columns.
 * A repeated parameter keeps its first value, and an empty value is left out.
 */
export function parseTableQuery(
  params: URLSearchParams,
  fields: readonly Pick<FieldMetadata, 'name' | 'type'>[],
  defaultPageSize: number = recordPageSize,
  parameterNames: readonly string[] = [],
): TableQuery {
  const parameters: Record<string, string> = {}
  for (const name of parameterNames) {
    // `get` returns the first value of a repeated parameter.
    const value = params.get(name)
    if (value) {
      parameters[name] = value
    }
  }
  const page = params.get('page')
  const pageSize = tablePageSizes(defaultPageSize).find((size) => String(size) === params.get('pageSize'))
  const sort = params.get('sort')
  const sortField = sort?.startsWith('-') ? sort.slice(1) : sort
  const sortable = fields.some((field) => field.name === sortField && field.type !== 'reference')
  return {
    page: page !== null && /^[1-9]\d*$/.test(page) ? Number(page) : defaultPage,
    pageSize: pageSize ?? defaultPageSize,
    sort: sortField && sortable ? { field: sortField, descending: sort !== sortField } : null,
    parameters,
  }
}

/**
 * Writes the table's parameters into a copy of the URL parameters. Other parameters stay first, as
 * they are. The data source parameter values follow, then `page`, `pageSize` and `sort` in that
 * order, and defaults are left out. `parameterNames` are removed first, so a cleared value goes.
 */
export function writeTableQuery(
  current: URLSearchParams,
  query: TableQuery,
  defaultPageSize: number = recordPageSize,
  parameterNames: readonly string[] = [],
): URLSearchParams {
  const next = new URLSearchParams(current)
  for (const name of parameterNames) {
    next.delete(name)
  }
  next.delete('page')
  next.delete('pageSize')
  next.delete('sort')
  for (const [name, value] of new URLSearchParams(recordQuery(query, defaultPageSize))) {
    next.append(name, value)
  }
  return next
}

/**
 * The record API query for a table query: the data source parameter values, then only the paging and
 * sorting parameters that differ from the defaults.
 */
export function recordQuery(query: TableQuery, defaultPageSize: number = recordPageSize): string {
  const params = new URLSearchParams()
  for (const [name, value] of Object.entries(query.parameters ?? {})) {
    if (value) {
      params.append(name, value)
    }
  }
  if (query.page !== defaultPage) {
    params.set('page', String(query.page))
  }
  if (query.pageSize !== defaultPageSize) {
    params.set('pageSize', String(query.pageSize))
  }
  if (query.sort) {
    params.set('sort', `${query.sort.descending ? '-' : ''}${query.sort.field}`)
  }
  return params.toString()
}
