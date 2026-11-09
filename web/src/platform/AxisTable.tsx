import { EllipsisOutlined, ReloadOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Dropdown, Flex, Input, Table, type TableColumnType, type TableProps } from 'antd'
import {
  useContext,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type HTMLAttributes,
  type Key,
  type ReactNode,
} from 'react'
import { tableTheme } from './antTheme'
import { AxisButton } from './AxisButton'
import { EmptyDash } from './EmptyValue'
import { EmptyState } from './EmptyState'
import { nameCompare } from './listOrder'
import { markCurrentPage } from './pagination'
import { matchesFilter, type FilterGroup, type FilterKind } from './tableFilter'
import { TableFilterButton, TableFilterChips, type FilterColumn } from './TableFilter'
import { TableColumnsButton } from './TableColumns'
import { shownColumns, type TableQuery, type TableSort } from './tableQuery'
import { makeRoom, type ColumnPriority } from './tableRoom'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'
import { visuallyHidden } from './visuallyHidden'

/**
 * How wide a column is, see docs/reference/actions-and-controls.md#tables.
 * - `fixed`: a value of a fixed form, such as a number, a date, a boolean, an enum or the row's actions, as wide as
 *   its content and never wrapping.
 * - `freeText`: text, a reference or a file. The free-text columns share the width the others leave in equal parts, and
 *   their content wraps at 320 px. A table with none gives that width to its last column.
 */
export type ColumnKind = 'fixed' | 'freeText'

/**
 * A column of an {@link AxisTable}: Ant Design's column, whose width and cell styles the table sets from its `kind`.
 * A cell whose `render` gives nothing, or with no `render` and no value, shows the em dash unless the row is blank.
 */
export type AxisColumn<T> = Omit<TableColumnType<T>, 'width' | 'render'> & {
  key: string
  kind: ColumnKind
  render?: (value: unknown, row: T, index: number) => ReactNode
  /** Which columns move under their row first when the table is too narrow, see docs/reference/actions-and-controls.md#tables. */
  priority?: ColumnPriority
  /** The text the search looks in, for a table that holds its rows, see docs/reference/actions-and-controls.md#tables. */
  searchText?: (row: T) => string
  /** The value the column's header sorts by, for a table that holds its rows, see docs/reference/actions-and-controls.md#tables. */
  sortValue?: (row: T) => string | number | null
  /**
   * What the column filters on, see docs/reference/actions-and-controls.md#tables: its kind of value and, for an enum,
   * its options. A column that gives it has a filter in the builder of any table with a view. Only a table that holds
   * its rows reads `value`, how a row gives the value (null is empty), so a table paged on the server may leave it out.
   * A column without `filter` has no filter. Dates compare as ISO text.
   */
  filter?: {
    kind: FilterKind
    value?: (row: T) => string | number | null
    options?: { value: string; label: string }[]
  }
}

/**
 * The view of a table: its search, filter, sort, page and page size, see docs/reference/actions-and-controls.md#tables.
 * Without a `total`, the table holds all its rows, and AxisTable searches, filters, sorts and pages them itself. With a `total`,
 * the server has done that work, and `dataSource` is the page.
 */
export interface AxisTableView {
  query: TableQuery
  onChange: (next: TableQuery) => void
  /** The page sizes the picker offers. Without any, there is no picker. */
  pageSizes?: number[]
  /** How many rows there are in all. */
  total?: number
  /** Whether the table has a search box. A table that holds its rows has one also when a column gives `searchText`. */
  search?: boolean
  /** Whether the table has the Columns button. */
  columns?: boolean
}

/**
 * An action on one row. One with an `icon` is an icon button whose name is its tooltip and accessible name, as the
 * platform gives an icon only to an action whose meaning it knows. One an author configured keeps its text.
 */
export interface AxisRowAction {
  key: string
  /** The text key of its name. */
  labelKey: string
  icon?: ReactNode
  destructive?: boolean
  disabled?: boolean
  /** Appended to the accessible name of an icon button only, such as a row number. The tooltip stays the name. */
  nameSuffix?: string
  onClick: () => void
  /** Attributes for the button, such as the canvas's selection. */
  attributes?: object
}

/** An action on the selected rows, see docs/reference/actions-and-controls.md#tables. It is a text button. */
export interface AxisBulkAction<T> {
  key: string
  /** The text key of its name. */
  labelKey: string
  destructive?: boolean
  onClick: (rows: T[]) => void
}

/**
 * The toolbar of every table, see docs/reference/actions-and-controls.md#tables. The view controls, such as search, sit
 * on the left. On the right come Refresh, the secondary actions such as Import and Export, and the main action last.
 * Each is there only when the table has it.
 */
export interface AxisTableToolbar {
  view?: ReactNode
  onRefresh?: () => void
  secondary?: ReactNode
  main?: ReactNode
}

export interface AxisTableProps<T> {
  columns: AxisColumn<T>[]
  rowKey: (row: T) => string
  dataSource?: readonly T[]
  loading?: boolean
  emptyText?: string
  /**
   * The paging. Without it, or with `false`, every row shows and there is no pager. The page-size picker shows only
   * when `pageSizeOptions` offers sizes, and a pager with no picker hides on a single page. A `view` replaces it.
   */
  pagination?: { current: number; pageSize: number; total: number; pageSizeOptions?: number[] } | false
  /** Called when the page, page size or sort changes. A `view` replaces it. */
  onChange?: TableProps<T>['onChange']
  /** The search, sort and paging, kept by the caller, for example in the address with `useTableView`. */
  view?: AxisTableView
  toolbar?: AxisTableToolbar
  /** What sits between the toolbar and the rows, such as a filter bar, messages and dialogs. */
  below?: ReactNode
  /** What shows in place of the rows, such as a message that they did not load. The toolbar stays. */
  failure?: ReactNode
  /**
   * The actions of a row. `configured` is how many an author configured, the same for every row, so every row has one
   * layout. `actions` gives the ones a row shows, or `null` for none.
   */
  rowActions?: { configured: number; actions: (row: T) => AxisRowAction[] | null }
  /**
   * The actions on the selected rows. Rows show checkboxes, and a selection shows the "N selected" bar, only when the
   * table has one.
   */
  bulkActions?: AxisBulkAction<T>[]
  /** Whether a row is a group row: its empty cells stay blank instead of showing the em dash. */
  blankRow?: (row: T) => boolean
  /** Attributes for the row's element, such as test ids and data attributes. */
  rowAttributes?: (row: T, index?: number) => HTMLAttributes<HTMLElement>
  /** The test id of the table's outer element. */
  testId?: string
}

/** Column key of the row actions. A field name starts with a letter, so no field column has it. */
const actionsKey = '$actions'

/** A width of 1 % makes an auto-layout column as narrow as its content, which `nowrap` keeps on one line. */
const fitContent = '1%'

function sameSort(a: TableSort | null, b: TableSort | null): boolean {
  return a?.field === b?.field && a?.descending === b?.descending
}

/** Compares two sort values: a number by its size, any other by the language's collation. */
function valueCompare(locale: string): (left: string | number, right: string | number) => number {
  const compareNames = nameCompare(locale)
  return (left, right) =>
    typeof left === 'number' && typeof right === 'number' ? left - right : compareNames(String(left), String(right))
}

/** The most a free-text value is wide before it wraps, in px, see docs/reference/actions-and-controls.md#tables. */
const freeTextMaxWidth = 320

/** The least a free-text value is wide before a column moves under its row, in px, see docs/reference/actions-and-controls.md#tables. */
const freeTextMinWidth = 160

/**
 * The row's actions fold into one menu from this many on, and at narrow width whatever their number, see
 * docs/reference/actions-and-controls.md#tables.
 */
const foldFrom = 3

/**
 * The one table of the platform, see docs/reference/actions-and-controls.md#tables. It draws the toolbar in its fixed
 * order, with the Columns button after Search and Filter for a view that turns it on, the columns by their kind, rows and a header of the `tableRow` size, the row actions, folded into one "…" menu
 * from three on or below 992 px, the row checkboxes and the selection bar of the bulk actions, the paging, the columns that move under their row and then the sideways scrolling when the page is narrower than the columns, and the em
 * dash for an empty cell.
 */
export function AxisTable<T extends object>({
  columns,
  rowKey,
  dataSource,
  loading = false,
  emptyText,
  pagination,
  onChange,
  view,
  toolbar = {},
  below,
  failure,
  rowActions,
  bulkActions,
  blankRow,
  rowAttributes,
  testId,
}: AxisTableProps<T>) {
  const t = useText()
  // The shell sets the locale of Ant Design's ConfigProvider. Outside it, there is none.
  const locale = useContext(ConfigProvider.ConfigContext).locale?.locale ?? 'en'
  const query = view?.query
  const [draft, setDraft] = useState(query?.search ?? '')
  const [shownSearch, setShownSearch] = useState(query?.search)
  // A search from the address, such as after the back button, replaces the text in the box.
  if (shownSearch !== query?.search) {
    setShownSearch(query?.search)
    setDraft(query?.search ?? '')
  }
  const [selectedKeys, setSelectedKeys] = useState<Key[]>([])
  const tableRef = useRef<HTMLDivElement>(null)
  // Ant Design has no prop for the focus of its scrolling region, so the attribute is set on the element.
  useEffect(() => {
    tableRef.current?.querySelector('.ant-table-content')?.setAttribute('tabindex', '0')
  })
  const tokens = useDesignTokens()
  const narrow = useNarrowWidth()
  // The widths the columns need when drawn and not stretched. A moved column is not drawn, so it is remembered.
  const naturalWidths = useRef(new Map<string, number>())
  const [room, setRoom] = useState<{ moved: string[]; scrolls: boolean }>({ moved: [], scrolls: false })
  const measureRef = useRef<() => void>(() => {})
  // The first column identifies the row. A table with the Columns button shows the columns of the choice in the view.
  const arrangeable = view?.columns === true && query !== undefined
  const keys = columns.map((column) => column.key)
  const shown = arrangeable
    ? shownColumns(keys, query.columns).map((key) => columns.find((column) => column.key === key)!)
    : columns
  const freeTexts = shown.filter((column) => column.kind === 'freeText').length
  // Each free-text column takes an equal share of the width. Without one, the last column takes what is left.
  const share = freeTexts > 0 ? `${100 / freeTexts}%` : undefined
  const lastKey = shown.at(-1)?.key
  const fold = narrow || (rowActions?.configured ?? 0) >= foldFrom

  // The table measures its own width after every draw and when it resizes, then moves columns under their row.
  useLayoutEffect(() => {
    const measure = () => {
      const root = tableRef.current
      const heads = new Map<string, HTMLElement>()
      root?.querySelectorAll<HTMLElement>('th[data-column-key]').forEach((head) => {
        heads.set(head.dataset.columnKey!, head)
      })
      if (!root || heads.size === 0) {
        return
      }
      const available = root.clientWidth
      const others =
        (root.querySelector<HTMLElement>('th.ant-table-selection-column')?.offsetWidth ?? 0) +
        (heads.get(actionsKey)?.offsetWidth ?? 0)
      const drawn = shown.reduce((sum, column) => sum + (heads.get(column.key)?.offsetWidth ?? 0), others)
      const widths = naturalWidths.current
      const sizes = shown.map((column) => {
        const head = heads.get(column.key)
        if (column.kind === 'freeText') {
          return { key: column.key, width: freeTextMinWidth + 2 * tokens.spacing.lg, priority: column.priority }
        }
        // With slack, a table with no free text stretches its last column past what it needs.
        const stretched = freeTexts === 0 && column.key === lastKey && drawn <= available
        if (head && (!stretched || !widths.has(column.key))) {
          widths.set(column.key, head.offsetWidth)
        }
        return { key: column.key, width: widths.get(column.key) ?? 0, priority: column.priority }
      })
      const next = makeRoom(available, others, sizes)
      setRoom((current) =>
        current.scrolls === next.scrolls &&
        current.moved.length === next.moved.length &&
        current.moved.every((key, index) => key === next.moved[index])
          ? current
          : next,
      )
    }
    measureRef.current = measure
    measure()
  })
  useEffect(() => {
    const root = tableRef.current
    if (!root || typeof ResizeObserver === 'undefined') {
      return
    }
    const observer = new ResizeObserver(() => measureRef.current())
    observer.observe(root)
    return () => observer.disconnect()
  }, [])

  /** What a cell shows, or null when it has no value. */
  const contentOf = (column: AxisColumn<T>, row: T, index: number): ReactNode => {
    const value = valueAt(row, column.dataIndex)
    const cell = column.render ? column.render(value, row, index) : value
    const empty = cell === null || cell === undefined || (!column.render && cell === '')
    return empty ? null : (cell as ReactNode)
  }
  const movedColumns = shown.filter((column) => room.moved.includes(column.key))
  /** The moved cells of a row, in the order of the columns, without those that have no value. */
  const movedLines = (row: T, index: number) =>
    movedColumns.flatMap((column) => {
      const content = contentOf(column, row, index)
      return content === null ? [] : [{ key: column.key, title: titleText(column), content }]
    })

  const local = view !== undefined && view.total === undefined
  const searchColumns = columns.filter((column) => column.searchText !== undefined)
  const searchable = view !== undefined && (view.search === true || (local && searchColumns.length > 0))
  const filterColumns: FilterColumn[] =
    view !== undefined
      ? columns.flatMap((column) =>
          column.filter
            ? [
                {
                  key: column.key,
                  title: typeof column.title === 'string' ? column.title : column.key,
                  kind: column.filter.kind,
                  options: column.filter.options,
                },
              ]
            : [],
        )
      : []
  const filterable = filterColumns.length > 0
  let pageRows = dataSource
  let total = 0
  let current = 1
  if (view && query) {
    if (local) {
      const rows = held(dataSource ?? [], columns, query, locale)
      total = rows.length
      current = Math.min(query.page, Math.max(1, Math.ceil(total / query.pageSize)))
      pageRows = rows.slice((current - 1) * query.pageSize, current * query.pageSize)
    } else {
      total = view.total ?? 0
      current = query.page
    }
  }

  // Only a row with a moved value has an expanded row, see docs/reference/actions-and-controls.md#tables.
  const expandedKeys = new Set<Key>()
  if (movedColumns.length > 0) {
    ;(pageRows ?? []).forEach((item, index) => {
      if (!blankRow?.(item) && movedLines(item, index).length > 0) {
        expandedKeys.add(rowKey(item))
      }
    })
  }
  // A row whose moved cells show draws no line below itself, as they sit under it on the same surface.
  const hasMoved = (row: T) => expandedKeys.has(rowKey(row))
  const line: CSSProperties = { lineHeight: `${tokens.size.smallControl}px`, verticalAlign: 'top' }
  const open: CSSProperties = { borderBottom: 'none' }
  const antColumns: TableColumnType<T>[] = shown
    .filter((column) => !room.moved.includes(column.key))
    .map(
      (
        {
          kind,
          render: _render,
          onCell,
          onHeaderCell,
          searchText: _searchText,
          sortValue,
          filter: _filter,
          priority: _priority,
          ...column
        },
        position,
      ) => {
        const freeText = kind === 'freeText'
        const width = freeText ? share : freeTexts === 0 && column.key === lastKey ? undefined : fitContent
        const sizing: CSSProperties = { width, ...(freeText ? {} : { whiteSpace: 'nowrap' }), ...line }
        const sortable = view !== undefined && (Boolean(column.sorter) || sortValue !== undefined)
        const original = shown.find((candidate) => candidate.key === column.key)!
        return {
          ...column,
          ...(position === 0 && room.scrolls ? { fixed: 'start' as const } : {}),
          ...(sortable
            ? {
                sorter: true,
                sortOrder: query?.sort?.field === column.key ? (query.sort.descending ? 'descend' : 'ascend') : null,
              }
            : {}),
          onHeaderCell: (data, index) => {
            const own = onHeaderCell?.(data, index)
            return { ...own, 'data-column-key': column.key, style: { ...sizing, ...own?.style } }
          },
          onCell: (row, index) => {
            const own = onCell?.(row, index)
            return { ...own, style: { ...sizing, ...(hasMoved(row) ? open : {}), ...own?.style } }
          },
          render: (_value: unknown, row: T, index: number) => {
            const content = contentOf(original, row, index)
            if (content === null) {
              return blankRow?.(row) ? null : <EmptyDash />
            }
            return freeText ? (
              <div
                style={{
                  minWidth: freeTextMinWidth,
                  maxWidth: freeTextMaxWidth,
                  whiteSpace: 'normal',
                  overflowWrap: 'anywhere',
                }}
              >
                {content}
              </div>
            ) : (
              content
            )
          },
        }
      },
    )
  if (rowActions && rowActions.configured > 0) {
    const cell = { style: { width: fitContent, whiteSpace: 'nowrap', ...line } satisfies CSSProperties }
    antColumns.push({
      key: actionsKey,
      // A header without text fails the accessibility check, so the column is named for screen readers only.
      title: <span style={visuallyHidden}>{t('shell.table.rowActions')}</span>,
      onHeaderCell: () => ({ ...cell, 'data-column-key': actionsKey }),
      onCell: (row) => ({ style: { ...cell.style, ...(hasMoved(row) ? open : {}) } }),
      render: (_value: unknown, row: T) => <RowActions actions={rowActions.actions(row)} fold={fold} />,
    })
  }

  const bulk = (bulkActions?.length ?? 0) > 0
  // Only the rows that show count, so the count and the action's rows match what the person sees.
  const selected = bulk ? (pageRows ?? []).filter((item) => selectedKeys.includes(rowKey(item))) : []
  const hasToolbar =
    searchable ||
    filterable ||
    arrangeable ||
    toolbar.view !== undefined ||
    toolbar.onRefresh !== undefined ||
    toolbar.secondary !== undefined ||
    toolbar.main !== undefined
  const row: CSSProperties = { height: tokens.size.tableRow }
  const paging = view
    ? { current, pageSize: query!.pageSize, total, pageSizeOptions: view.pageSizes ?? [] }
    : pagination || undefined
  const sizes = paging?.pageSizeOptions ?? []
  const picker = sizes.length > 0
  const number = new Intl.NumberFormat(locale)
  // A new sort or page size starts again at page 1.
  const onViewChange: TableProps<T>['onChange'] = (next, _filters, sorter) => {
    if (!view || !query) {
      return
    }
    const single = Array.isArray(sorter) ? sorter[0] : sorter
    const sort = single?.order ? { field: String(single.columnKey), descending: single.order === 'descend' } : null
    const pageSize = next.pageSize ?? query.pageSize
    const reset = !sameSort(sort, query.sort) || pageSize !== query.pageSize
    view.onChange({ ...query, page: reset ? 1 : (next.current ?? 1), pageSize, sort })
  }
  // A new search starts again at page 1. An empty text means no search.
  const onSearch = (value: string) => {
    if (!view || !query || value === (query.search ?? '')) {
      return
    }
    view.onChange({ ...query, page: 1, search: value || undefined })
  }
  // A new filter starts again at page 1. No filter leaves it out.
  const onFilter = (filter: FilterGroup | undefined) => {
    if (view && query) {
      view.onChange({ ...query, page: 1, filter })
    }
  }
  // Showing, hiding or ordering a column changes neither the rows nor their order, so the page stays.
  const onColumns = (next: string[] | undefined) => {
    if (view && query) {
      view.onChange({ ...query, columns: next })
    }
  }
  return (
    <Flex ref={tableRef} vertical gap="middle" aria-busy={loading} data-testid={testId}>
      {hasToolbar && (
        <Flex justify="space-between" align="center" gap="small" wrap>
          {(searchable || filterable || arrangeable || toolbar.view !== undefined) && (
            <Flex gap="small" wrap>
              {searchable && (
                <Input.Search
                  allowClear
                  maxLength={200}
                  style={{ maxWidth: 320 }}
                  aria-label={t('shell.table.search')}
                  placeholder={t('shell.table.search')}
                  value={draft}
                  onChange={(event) => setDraft(event.target.value)}
                  onSearch={onSearch}
                />
              )}
              {filterable && query && (
                <TableFilterButton columns={filterColumns} filter={query.filter} onApply={onFilter} />
              )}
              {arrangeable && (
                <TableColumnsButton
                  columns={columns.map((column) => ({
                    key: column.key,
                    title: typeof column.title === 'string' ? column.title : column.key,
                  }))}
                  shown={shown.map((column) => column.key)}
                  onApply={onColumns}
                />
              )}
              {toolbar.view}
            </Flex>
          )}
          <Flex gap="small" style={{ marginInlineStart: 'auto' }}>
            {toolbar.onRefresh && (
              <AxisButton
                role="icon"
                nameKey="shell.table.refresh"
                icon={<ReloadOutlined />}
                onClick={toolbar.onRefresh}
              />
            )}
            {toolbar.secondary}
            {toolbar.main}
          </Flex>
        </Flex>
      )}
      {filterable && query?.filter && (
        <TableFilterChips columns={filterColumns} filter={query.filter} locale={locale} onChange={onFilter} />
      )}
      {below}
      {failure ?? (
        <>
          {bulk && selected.length > 0 && (
            <Flex align="center" gap="small">
              <span>{t('shell.table.selected').replace('{count}', String(selected.length))}</span>
              {bulkActions?.map((action) => (
                <AxisButton
                  key={action.key}
                  role={action.destructive ? 'destructive' : 'secondary'}
                  size="small"
                  onClick={() => {
                    action.onClick(selected)
                    setSelectedKeys([])
                  }}
                >
                  {t(action.labelKey)}
                </AxisButton>
              ))}
            </Flex>
          )}
          <ConfigProvider theme={tableTheme(tokens)}>
            <Table<T>
              rowKey={rowKey}
              rowSelection={
                bulk
                  ? {
                      selectedRowKeys: selected.map(rowKey),
                      onChange: (keys) => setSelectedKeys(keys),
                      getCheckboxProps: () => ({ 'aria-label': t('shell.table.selectRow') }),
                      fixed: room.scrolls,
                      onCell: (row) => (hasMoved(row) ? { style: open } : {}),
                    }
                  : undefined
              }
              columns={antColumns}
              // The table moves columns under their row first. Only what stays still and does not fit scrolls sideways.
              scroll={{ x: true }}
              // Ant Design otherwise switches to a fixed layout once a column is fixed.
              tableLayout="auto"
              expandable={
                movedColumns.length > 0
                  ? {
                      showExpandColumn: false,
                      expandedRowKeys: [...expandedKeys],
                      expandedRowRender: (item, index) => <MovedCells lines={movedLines(item, index)} line={line} />,
                    }
                  : undefined
              }
              dataSource={pageRows as T[] | undefined}
              loading={loading}
              locale={{ emptyText: <EmptyState text={emptyText} /> }}
              pagination={
                paging
                  ? {
                      current: paging.current,
                      pageSize: paging.pageSize,
                      total: paging.total,
                      showSizeChanger: picker,
                      hideOnSinglePage: !picker,
                      itemRender: markCurrentPage(paging.current),
                      showTotal: (all, [from, to]) =>
                        t('shell.table.range')
                          .replace('{from}', number.format(from))
                          .replace('{to}', number.format(to))
                          .replace('{total}', number.format(all)),
                      ...(picker ? { pageSizeOptions: sizes } : {}),
                    }
                  : false
              }
              onRow={rowAttributes}
              onChange={view ? onViewChange : onChange}
              styles={{ header: { row }, body: { row } }}
            />
          </ConfigProvider>
        </>
      )}
    </Flex>
  )
}

/**
 * The rows of a table that holds them, filtered by the columns that give `filter`, searched by the columns that give
 * `searchText`, then sorted by the sorted column's `sortValue`. The search is case-insensitive and ignores the spaces
 * around it. An empty value comes last.
 */
function held<T>(rows: readonly T[], columns: readonly AxisColumn<T>[], query: TableQuery, locale: string): T[] {
  const { filter } = query
  const filtered = filter
    ? rows.filter((row) =>
        matchesFilter(
          row,
          filter,
          (field) => {
            const column = columns.find((candidate) => candidate.key === field)?.filter
            return column?.value ? { kind: column.kind, value: column.value } : undefined
          },
          locale,
        ),
      )
    : rows
  const needle = query.search?.trim().toLocaleLowerCase(locale)
  const searched = needle
    ? filtered.filter((row) =>
        columns.some((column) => column.searchText?.(row).toLocaleLowerCase(locale).includes(needle) === true),
      )
    : [...filtered]
  const value = columns.find((column) => column.key === query.sort?.field)?.sortValue
  if (!value || !query.sort) {
    return searched
  }
  const compare = valueCompare(locale)
  const direction = query.sort.descending ? -1 : 1
  return searched.sort((left, right) => {
    const a = value(left)
    const b = value(right)
    if (a === null || b === null) {
      return a === b ? 0 : a === null ? 1 : -1
    }
    return direction * compare(a, b)
  })
}

/** The value a column's `dataIndex` points to in a row, as Ant Design reads it. Without a `dataIndex` it is the row. */
function valueAt(row: object, dataIndex: unknown): unknown {
  if (dataIndex === undefined) {
    return row
  }
  const path: PropertyKey[] = Array.isArray(dataIndex) ? dataIndex : [dataIndex as PropertyKey]
  return path.reduce<unknown>((value, key) => (value as Record<PropertyKey, unknown> | null | undefined)?.[key], row)
}

/** The title of a column as a moved cell names it. A title that is computed falls back to the column's key. */
function titleText<T>(column: AxisColumn<T>): ReactNode {
  return typeof column.title === 'function' ? column.key : column.title
}

/** The cells of a row that moved under it, one "Title: value" line each, see docs/reference/actions-and-controls.md#tables. */
function MovedCells({
  lines,
  line,
}: {
  lines: { key: string; title: ReactNode; content: ReactNode }[]
  line: CSSProperties
}) {
  return (
    <Flex vertical style={line}>
      {lines.map((moved) => (
        <div key={moved.key}>
          <span>{moved.title}</span>: {moved.content}
        </div>
      ))}
    </Flex>
  )
}

/** A row's actions: icon or text buttons, or one "…" menu of text entries when they fold. */
function RowActions({ actions, fold }: { actions: AxisRowAction[] | null; fold: boolean }) {
  const t = useText()
  if (actions === null || actions.length === 0) {
    return null
  }
  if (fold) {
    return (
      <Dropdown
        trigger={['click']}
        autoFocus
        menu={{
          items: actions.map((action) => ({
            key: action.key,
            label: t(action.labelKey),
            danger: action.destructive,
            disabled: action.disabled,
            onClick: action.onClick,
          })),
          'aria-label': t('shell.table.rowActions'),
        }}
      >
        <AxisButton role="icon" size="small" nameKey="shell.table.rowActions" icon={<EllipsisOutlined />} />
      </Dropdown>
    )
  }
  return (
    <Flex gap="small">
      {actions.map((action) =>
        action.icon ? (
          <AxisButton
            key={action.key}
            role="icon"
            size="small"
            nameKey={action.labelKey}
            destructive={action.destructive}
            icon={action.icon}
            nameSuffix={action.nameSuffix}
            disabled={action.disabled}
            onClick={action.onClick}
            {...action.attributes}
          />
        ) : (
          // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
          <Button
            key={action.key}
            type="text"
            size="small"
            danger={action.destructive}
            disabled={action.disabled}
            onClick={action.onClick}
            {...action.attributes}
          >
            {t(action.labelKey)}
          </Button>
        ),
      )}
    </Flex>
  )
}
