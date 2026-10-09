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

/** The paging and sorting of a table, as kept in the URL. */
export interface TableQuery {
  page: number
  pageSize: number
  sort: TableSort | null
}

/**
 * Reads `page`, `pageSize` and `sort` from the URL. An invalid value falls back to its default: a
 * `page` that is not a positive integer, a `pageSize` the table does not offer, or a `sort` that
 * names no field or a reference field. `fields` are the table's columns.
 */
export function parseTableQuery(
  params: URLSearchParams,
  fields: readonly Pick<FieldMetadata, 'name' | 'type'>[],
  defaultPageSize: number = recordPageSize,
): TableQuery {
  const page = params.get('page')
  const pageSize = tablePageSizes(defaultPageSize).find((size) => String(size) === params.get('pageSize'))
  const sort = params.get('sort')
  const sortField = sort?.startsWith('-') ? sort.slice(1) : sort
  const sortable = fields.some((field) => field.name === sortField && field.type !== 'reference')
  return {
    page: page !== null && /^[1-9]\d*$/.test(page) ? Number(page) : defaultPage,
    pageSize: pageSize ?? defaultPageSize,
    sort: sortField && sortable ? { field: sortField, descending: sort !== sortField } : null,
  }
}

/**
 * Writes the table's parameters into a copy of the URL parameters. Other parameters stay first, as
 * they are. `page`, `pageSize` and `sort` follow in that order, and defaults are left out.
 */
export function writeTableQuery(
  current: URLSearchParams,
  query: TableQuery,
  defaultPageSize: number = recordPageSize,
): URLSearchParams {
  const next = new URLSearchParams(current)
  next.delete('page')
  next.delete('pageSize')
  next.delete('sort')
  for (const [name, value] of new URLSearchParams(recordQuery(query, defaultPageSize))) {
    next.append(name, value)
  }
  return next
}

/** The record API query for a table query: only the parameters that differ from the defaults. */
export function recordQuery(query: TableQuery, defaultPageSize: number = recordPageSize): string {
  const params = new URLSearchParams()
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
