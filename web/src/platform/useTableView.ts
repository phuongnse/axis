import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useSearchParams } from 'react-router'
import type { FieldMetadata } from './site'
import { useTableChoices } from './tableChoices'
import {
  columnsChoice,
  parseTableQuery,
  recordPageSize,
  shownColumns,
  writeTableQuery,
  type TableQuery,
} from './tableQuery'

/**
 * A table's view, its search, filter, columns, sort, page, page size and data source parameter values, kept in the address, see
 * docs/reference/actions-and-controls.md#tables. An invalid, empty, repeated or out-of-order value, or a search the
 * table cannot make or a filter that is not valid for `fields`, is rewritten without a history entry. `setView` adds a history entry unless `replace` is set.
 * A view change keeps the scroll position, see docs/reference/frontend.md#routing.
 * `prefix` goes before every address name, so a second table on a page keeps its view under names of its own.
 * `fields` and `parameterNames` must be stable between renders.
 * A table with the `columnsControl` and a `tableKey` keeps the columns that Apply sets for the person on the server,
 * and Reset followed by Apply forgets them. A table opened with no `columns` in the address gets the kept columns,
 * written without a history entry. `columns` in the address wins. A table without a key keeps nothing, see
 * docs/reference/frontend.md#table-views-and-columns.
 */
export function useTableView(
  fields: readonly Pick<FieldMetadata, 'name' | 'type'>[],
  defaultPageSize: number = recordPageSize,
  parameterNames: readonly string[] = [],
  searchable = false,
  filterable = false,
  columnsControl = false,
  prefix = '',
  tableKey?: string,
): { view: TableQuery; setView: (next: TableQuery, options?: { replace?: boolean }) => void } {
  const [searchParams, setSearchParams] = useSearchParams()
  const { choices, keepColumns } = useTableChoices(columnsControl ? tableKey : undefined)
  const view = useMemo(
    () =>
      parseTableQuery(
        searchParams,
        fields,
        defaultPageSize,
        parameterNames,
        searchable,
        filterable,
        columnsControl,
        prefix,
      ),
    [searchParams, fields, defaultPageSize, parameterNames, searchable, filterable, columnsControl, prefix],
  )

  useEffect(() => {
    const canonical = writeTableQuery(searchParams, view, defaultPageSize, parameterNames, prefix)
    if (canonical.toString() !== searchParams.toString()) {
      setSearchParams(canonical, { replace: true, preventScrollReset: true })
    }
  }, [searchParams, view, defaultPageSize, parameterNames, prefix, setSearchParams])

  // The key whose kept columns are settled: written into the address, or beaten by the person's own Apply.
  const settledKey = useRef<string>(undefined)

  useEffect(() => {
    if (choices === undefined || settledKey.current === tableKey) {
      return
    }
    settledKey.current = tableKey
    if (searchParams.has(`${prefix}columns`)) {
      return
    }
    const names = fields.map((field) => field.name)
    const columns = columnsChoice(names, shownColumns(names, choices.columns ?? []))
    if (columns) {
      setSearchParams(writeTableQuery(searchParams, { ...view, columns }, defaultPageSize, parameterNames, prefix), {
        replace: true,
        preventScrollReset: true,
      })
    }
  }, [choices, tableKey, searchParams, fields, view, defaultPageSize, parameterNames, prefix, setSearchParams])

  const setView = useCallback(
    (next: TableQuery, options?: { replace?: boolean }) => {
      if ((next.columns ?? []).join(',') !== (view.columns ?? []).join(',')) {
        settledKey.current = tableKey
        keepColumns(next.columns)
      }
      setSearchParams(writeTableQuery(searchParams, next, defaultPageSize, parameterNames, prefix), {
        ...options,
        preventScrollReset: true,
      })
    },
    [view.columns, tableKey, keepColumns, searchParams, defaultPageSize, parameterNames, prefix, setSearchParams],
  )
  return { view, setView }
}
