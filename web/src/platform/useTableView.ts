import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useSearchParams } from 'react-router'
import type { FieldMetadata } from './site'
import { useTableChoices, type SavedTableView } from './tableChoices'
import {
  columnsChoice,
  ownEntries,
  parseTableQuery,
  recordPageSize,
  shownColumns,
  writeTableQuery,
  type TableQuery,
} from './tableQuery'

/**
 * The saved views of a table with a key, and the means to open, save, rename and delete them, see
 * docs/reference/actions-and-controls.md#tables. The writes answer whether the server took them.
 */
export interface TableSavedViews {
  views: SavedTableView[]
  /** Writes the view's search, filter and sort into the address at page 1, with a history entry. An empty part clears that part. */
  open: (view: SavedTableView) => void
  /** Saves the address's search, filter and sort under `name`. */
  save: (name: string) => Promise<boolean>
  rename: (view: SavedTableView, name: string) => Promise<boolean>
  remove: (view: SavedTableView) => Promise<boolean>
}

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
 * A table with a `tableKey` also gives `saved`, the person's saved views, once they have loaded. Opening a view reads
 * its search, filter and sort as the address is read, so a filter that names a lost field is dropped.
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
): {
  view: TableQuery
  setView: (next: TableQuery, options?: { replace?: boolean }) => void
  saved: TableSavedViews | undefined
} {
  const [searchParams, setSearchParams] = useSearchParams()
  const { choices, keepColumns, saveView, renameView, deleteView } = useTableChoices(tableKey)
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
    // Only the table's own names are compared, so a second table on the page, which writes its names last, is not
    // rewritten back and forth with this one.
    if (ownEntries(canonical, parameterNames, prefix) !== ownEntries(searchParams, parameterNames, prefix)) {
      setSearchParams(canonical, { replace: true, preventScrollReset: true })
    }
  }, [searchParams, view, defaultPageSize, parameterNames, prefix, setSearchParams])

  // The key whose kept columns are settled: written into the address, or beaten by the person's own Apply.
  const settledKey = useRef<string>(undefined)

  useEffect(() => {
    if (!columnsControl || choices === undefined || settledKey.current === tableKey) {
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
  }, [
    columnsControl,
    choices,
    tableKey,
    searchParams,
    fields,
    view,
    defaultPageSize,
    parameterNames,
    prefix,
    setSearchParams,
  ])

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

  const saved = useMemo<TableSavedViews | undefined>(() => {
    if (choices === undefined) {
      return undefined
    }
    return {
      views: choices.views,
      open: (opened) => {
        const params = new URLSearchParams()
        for (const [name, value] of [
          ['search', opened.search],
          ['filter', opened.filter],
          ['sort', opened.sort],
        ] as const) {
          if (value !== null) {
            params.set(prefix + name, value)
          }
        }
        const parsed = parseTableQuery(params, fields, defaultPageSize, [], searchable, filterable, false, prefix)
        setView({ ...view, page: 1, search: parsed.search, filter: parsed.filter, sort: parsed.sort })
      },
      save: (name) => {
        const written = writeTableQuery(new URLSearchParams(), view, defaultPageSize, parameterNames, prefix)
        return saveView({
          name,
          search: written.get(`${prefix}search`),
          filter: written.get(`${prefix}filter`),
          sort: written.get(`${prefix}sort`),
        })
      },
      rename: renameView,
      remove: deleteView,
    }
  }, [
    choices,
    fields,
    view,
    defaultPageSize,
    parameterNames,
    searchable,
    filterable,
    prefix,
    setView,
    saveView,
    renameView,
    deleteView,
  ])
  return { view, setView, saved }
}
