import { useCallback, useEffect, useMemo } from 'react'
import { useSearchParams } from 'react-router'
import type { FieldMetadata } from './site'
import { parseTableQuery, recordPageSize, writeTableQuery, type TableQuery } from './tableQuery'

/**
 * A table's view, its search, filter, sort, page, page size and data source parameter values, kept in the address, see
 * docs/reference/actions-and-controls.md#tables. An invalid, empty, repeated or out-of-order value, or a search the
 * table cannot make or a filter that is not valid for `fields`, is rewritten without a history entry. `setView` adds a history entry unless `replace` is set.
 * `fields` and `parameterNames` must be stable between renders.
 */
export function useTableView(
  fields: readonly Pick<FieldMetadata, 'name' | 'type'>[],
  defaultPageSize: number = recordPageSize,
  parameterNames: readonly string[] = [],
  searchable = false,
  filterable = false,
): { view: TableQuery; setView: (next: TableQuery, options?: { replace?: boolean }) => void } {
  const [searchParams, setSearchParams] = useSearchParams()
  const view = useMemo(
    () => parseTableQuery(searchParams, fields, defaultPageSize, parameterNames, searchable, filterable),
    [searchParams, fields, defaultPageSize, parameterNames, searchable, filterable],
  )

  useEffect(() => {
    const canonical = writeTableQuery(searchParams, view, defaultPageSize, parameterNames)
    if (canonical.toString() !== searchParams.toString()) {
      setSearchParams(canonical, { replace: true })
    }
  }, [searchParams, view, defaultPageSize, parameterNames, setSearchParams])

  const setView = useCallback(
    (next: TableQuery, options?: { replace?: boolean }) =>
      setSearchParams(writeTableQuery(searchParams, next, defaultPageSize, parameterNames), options),
    [searchParams, defaultPageSize, parameterNames, setSearchParams],
  )
  return { view, setView }
}
