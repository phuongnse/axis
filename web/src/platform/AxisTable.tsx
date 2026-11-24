import { DeleteOutlined, EllipsisOutlined, ReloadOutlined } from '@ant-design/icons'
import {
  ConfigProvider,
  Dropdown,
  Flex,
  Input,
  Pagination,
  Table,
  theme as antTheme,
  type TableColumnType,
  type TableProps,
} from 'antd'
import {
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type HTMLAttributes,
  type Key,
  type ReactNode,
} from 'react'
import { tableTheme } from './antTheme'
import { AxisButton } from './AxisButton'
import { Chevron } from './Chevron'
import { EmptyDash } from './EmptyValue'
import { EmptyState } from './EmptyState'
import { nameCompare } from './listOrder'
import { paginationItems } from './pagination'
import { matchesFilter, type FilterGroup, type FilterKind } from './tableFilter'
import { TableFilterButton, TableFilterChips, type FilterColumn } from './TableFilter'
import { TableColumnsButton } from './TableColumns'
import { actionText, menuOf, type AxisAction } from './menuActions'
import { TableViewsButton } from './TableViews'
import { shownColumns, type TableQuery, type TableSort } from './tableQuery'
import { makeRoom, type ColumnPriority } from './tableRoom'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'
import type { TableSavedViews } from './useTableView'
import { visuallyHidden } from './visuallyHidden'

/**
 * How wide a column is, see docs/reference/actions-and-controls.md#tables.
 * - `fixed`: a value of a fixed form, such as a number, a date, a boolean, an enum or the row's actions, as wide as
 *   its content and never wrapping.
 * - `freeText`: text, a reference or a file. The free-text columns share the width the others leave in equal parts, and
 *   their content wraps at the edge of the column. A table with none gives that width to its last column.
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
   * A column without `filter` has no filter. Dates compare as ISO text. A column whose header is not plain text gives a
   * `title`, the name the builder and its chips show. Without it they show the header text, or the key.
   */
  filter?: {
    kind: FilterKind
    title?: string
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
  /** The person's saved views of a table with a key, which `useTableView` gives. The Views button shows when it is set. */
  saved?: TableSavedViews
  /**
   * Whether the table pages its rows. With `false`, every row shows and there is no pager. Only a table that holds its
   * rows reads it, see docs/reference/actions-and-controls.md#tables.
   */
  paging?: boolean
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
 * The toolbar of every table, see docs/reference/actions-and-controls.md#tables. AxisTable draws the view's Search, Filter
 * Columns and Views on the left. The toolbar gives the right side: Refresh, the secondary actions such as Import and Export, and
 * the main action last. Each is there only when the table has it.
 */
export interface AxisTableToolbar {
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
  /**
   * Whether a row is one the filter never hides, such as a row added since the last save. Only a table that holds its
   * rows reads it, and only for the filter, see docs/reference/actions-and-controls.md#tables.
   */
  keep?: (row: T) => boolean
  toolbar?: AxisTableToolbar
  /** A title at the left end of the toolbar, before the search, see docs/reference/actions-and-controls.md#tables. */
  title?: ReactNode
  /** Whether each cell has `spacing-sm` on each side, see docs/reference/actions-and-controls.md#tables. */
  compact?: boolean
  /** Whether the table has no header row, see docs/reference/actions-and-controls.md#tables. */
  headless?: boolean
  /** What sits between the toolbar and the rows, such as a filter bar, messages and dialogs. */
  below?: ReactNode
  /** What shows in place of the rows, such as a message that they did not load. The toolbar stays. */
  failure?: ReactNode
  /**
   * The actions of a row. `configured` is how many an author configured, the same for every row, so every row has one
   * layout. `actions` gives the ones a row shows, or `null` for none.
   */
  rowActions?: { configured: number; actions: (row: T) => AxisAction[] | null }
  /**
   * The actions on the selected rows. Rows show checkboxes, and a selection shows the "N selected" bar, only when the
   * table has one.
   */
  bulkActions?: AxisBulkAction<T>[]
  /** Whether a row is a group row: its empty cells stay blank instead of showing the em dash. */
  blankRow?: (row: T) => boolean
  /**
   * What a row folds open to when it has more to show than its columns, such as a call's attempt, or `null` for a row
   * with none. A fold in the row's first cell opens and closes it, see docs/reference/actions-and-controls.md#tables.
   */
  details?: (row: T) => ReactNode | null
  /**
   * The rows a row folds open to, or `null` for a row with none: its group rows, closed at first, which open from the
   * fold in its first cell and may have rows of their own, see docs/reference/actions-and-controls.md#tables. Paging, search,
   * filter and sort work on the rows of `dataSource` alone, and a row's rows follow it. Every row's key must be unique in the
   * table, so a caller prefixes the key of a row inside another with the key of the row that holds it. A row that has rows
   * in it opens those, so `details` is for the rows that have none.
   */
  rowsIn?: (row: T) => readonly T[] | null
  /** Attributes for the row's element, such as test ids and data attributes. */
  rowAttributes?: (row: T, index?: number) => HTMLAttributes<HTMLElement>
  /** The test id of the table's outer element. */
  testId?: string
}

/** Column key of the row actions. A field name starts with a letter, so no field column has it. */
const actionsKey = '$actions'

/** The class of the sideways-scrolling region, which Ant Design's `classNames.content` puts on its element. */
const scrollClass = 'axis-table-scroll'

/** A width of 1 % makes an auto-layout column as narrow as its content, which `nowrap` keeps on one line. */
const fitContent = '1%'
/** The height of the moved lines of a row, set on the row for the cells that stay. */
const movedHeight = '--axis-moved-height'

function sameSort(a: TableSort | null, b: TableSort | null): boolean {
  return a?.field === b?.field && a?.descending === b?.descending
}

/** Compares two sort values: a number by its size, any other by the language's collation. */
function valueCompare(locale: string): (left: string | number, right: string | number) => number {
  const compareNames = nameCompare(locale)
  return (left, right) =>
    typeof left === 'number' && typeof right === 'number' ? left - right : compareNames(String(left), String(right))
}

/** The least a free-text value is wide before a column moves under its row, in px, see docs/reference/actions-and-controls.md#tables. */
const freeTextMinWidth = 160

/**
 * A row's actions fold into one menu from this many on, and at narrow width from two on, see
 * docs/reference/actions-and-controls.md#tables. A table set up for this many actions folds each row with two or more.
 */
const foldFrom = 3

/**
 * The one table of the platform, see docs/reference/actions-and-controls.md#tables. It draws the toolbar in its fixed
 * order, with the Columns button after Search and Filter for a view that turns it on, then the Views button for a view with `saved`, the columns by their kind, rows and a header of the `tableRow` size, the row actions, which a row
 * folds into one "…" menu from three on, or below 992 px from two on, the row checkboxes and the selection bar of the bulk actions, the paging, the columns that move under their row and then the sideways scrolling when the page is narrower than the columns, and the em
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
  keep,
  toolbar = {},
  title,
  compact = false,
  headless = false,
  below,
  failure,
  rowActions,
  bulkActions,
  blankRow,
  details,
  rowsIn,
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
  const [openRows, setOpenRows] = useState<string[]>([])
  const [openGroups, setOpenGroups] = useState<string[]>([])
  const tableRef = useRef<HTMLDivElement>(null)
  // Ant Design has no prop for the focus of its scrolling region, so the attribute is set on the element.
  useEffect(() => {
    tableRef.current?.querySelector(`.${scrollClass}`)?.setAttribute('tabindex', '0')
  })
  const tokens = useDesignTokens()
  // Ant Design gives the checkbox column its control height by default, so the room is known before the table draws.
  const selectionWidth = antTheme.useToken().token.controlHeight
  // The same theme object between draws, so the cells under the table are not drawn again for nothing.
  const theme = useMemo(() => tableTheme(tokens, compact), [tokens, compact])
  const cellPadding = compact ? tokens.spacing.sm : tokens.spacing.lg
  // A table with folds gives its first column a start padding as wide as a fold and the gap after it, and the folds sit in it.
  const foldable = rowsIn !== undefined || details !== undefined
  const foldWidth = tokens.size.smallControl
  const foldIndent = foldWidth + tokens.spacing.xs
  // The widths the columns need when drawn and not stretched. A moved column is not drawn, so it is remembered.
  const naturalWidths = useRef(new Map<string, number>())
  const [room, setRoom] = useState<{ moved: string[]; scrolls: boolean; lineWidth: number }>({
    moved: [],
    scrolls: false,
    lineWidth: 0,
  })
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

  const bulk = (bulkActions?.length ?? 0) > 0
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
      // The checkbox column is there only with a bulk action, and then it is as wide as the theme gives it.
      const selection = bulk ? selectionWidth : 0
      const others = selection + (heads.get(actionsKey)?.offsetWidth ?? 0)
      const drawn = shown.reduce((sum, column) => sum + (heads.get(column.key)?.offsetWidth ?? 0), others)
      const widths = naturalWidths.current
      const sizes = shown.map((column) => {
        const head = heads.get(column.key)
        if (column.kind === 'freeText') {
          return { key: column.key, width: freeTextMinWidth + 2 * cellPadding, priority: column.priority }
        }
        // With slack, a table with no free text stretches its last column past what it needs.
        const stretched = freeTexts === 0 && column.key === lastKey && drawn <= available
        if (head && (!stretched || !widths.has(column.key))) {
          widths.set(column.key, head.offsetWidth)
        }
        return { key: column.key, width: widths.get(column.key) ?? 0, priority: column.priority }
      })
      const { moved, scrolls } = makeRoom(available, others, sizes)
      // The moved lines of a row are as wide as the row's content, which starts after the checkbox and its padding.
      const next = {
        moved,
        scrolls,
        lineWidth: Math.max(0, available - selection - 2 * cellPadding - (foldable ? foldIndent : 0)),
      }
      setRoom((current) =>
        current.scrolls === next.scrolls &&
        current.lineWidth === next.lineWidth &&
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
                  title: column.filter.title ?? (typeof column.title === 'string' ? column.title : column.key),
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
      const rows = held(dataSource ?? [], columns, query, locale, keep)
      total = rows.length
      if (view.paging === false) {
        pageRows = rows
      } else {
        current = Math.min(query.page, Math.max(1, Math.ceil(total / query.pageSize)))
        pageRows = rows.slice((current - 1) * query.pageSize, current * query.pageSize)
      }
    } else {
      total = view.total ?? 0
      current = query.page
    }
  }

  // The rows of the page, each followed by the rows of its open groups, and how far in each row is.
  const depths = new Map<string, number>()
  let tableRows = pageRows
  if (rowsIn && pageRows) {
    const flat: T[] = []
    const add = (rows: readonly T[], depth: number) => {
      for (const item of rows) {
        const key = rowKey(item)
        flat.push(item)
        depths.set(key, depth)
        const inner = openGroups.includes(key) ? rowsIn(item) : null
        if (inner) {
          add(inner, depth + 1)
        }
      }
    }
    add(pageRows, 0)
    tableRows = flat
  }

  const line: CSSProperties = { lineHeight: `${tokens.size.smallControl}px`, verticalAlign: 'top' }
  // The cells that stay leave room under their content for the moved lines, which run across the whole row.
  const staysRoom: CSSProperties = { paddingBottom: `calc(${tokens.spacing.xs}px + var(${movedHeight}, 0px))` }
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
            const folds: CSSProperties =
              position === 0 && foldable ? { paddingInlineStart: cellPadding + foldIndent } : {}
            return { ...own, 'data-column-key': column.key, style: { ...sizing, ...folds, ...own?.style } }
          },
          onCell: (row, index) => {
            const own = onCell?.(row, index)
            // Each level of rows starts one fold further in.
            const folds: CSSProperties =
              position === 0 && foldable
                ? { paddingInlineStart: cellPadding + foldIndent + (depths.get(rowKey(row)) ?? 0) * foldWidth }
                : {}
            return { ...own, style: { ...sizing, ...(position === 0 ? {} : staysRoom), ...folds, ...own?.style } }
          },
          render: (_value: unknown, row: T, index: number) => {
            const content = contentOf(original, row, index)
            // The moved cells sit in the cell of the identifying column, so they stay in the row they belong to.
            const lines = position === 0 && !blankRow?.(row) ? movedLines(row, index) : []
            const own =
              content === null ? (
                blankRow?.(row) ? null : (
                  <EmptyDash />
                )
              ) : freeText ? (
                <div
                  style={{
                    minWidth: freeTextMinWidth,
                    whiteSpace: 'normal',
                    overflowWrap: 'anywhere',
                  }}
                >
                  {content}
                </div>
              ) : (
                content
              )
            const cell =
              lines.length === 0 ? (
                own
              ) : (
                <>
                  {own}
                  <MovedCells lines={lines} width={room.lineWidth} />
                </>
              )
            // The fold sits in the first cell's start padding, for a row that has rows in it or details.
            if (position !== 0 || !foldable) {
              return cell
            }
            const key = rowKey(row)
            const group = (rowsIn?.(row)?.length ?? 0) > 0
            if (!group && (details === undefined || details(row) == null)) {
              return cell
            }
            const [folded, setFolded] = group ? [openGroups, setOpenGroups] : [openRows, setOpenRows]
            const open = folded.includes(key)
            return (
              <div style={{ position: 'relative' }}>
                <span style={{ position: 'absolute', insetInlineStart: -foldIndent, top: 0 }}>
                  <RowFold
                    group={group}
                    open={open}
                    onToggle={() =>
                      setFolded((keys) => (open ? keys.filter((candidate) => candidate !== key) : [...keys, key]))
                    }
                  />
                </span>
                {cell}
              </div>
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
      onCell: () => ({ style: { ...cell.style, ...staysRoom } }),
      render: (_value: unknown, row: T) => {
        const actions = rowActions.actions(row)
        return <RowActions actions={actions} fold={rowActions.configured >= foldFrom} />
      },
    })
  }

  // Only the rows that show count, so the count and the action's rows match what the person sees.
  const selected = bulk ? (pageRows ?? []).filter((item) => selectedKeys.includes(rowKey(item))) : []
  const hasToolbar =
    title !== undefined ||
    searchable ||
    filterable ||
    arrangeable ||
    view?.saved !== undefined ||
    toolbar.onRefresh !== undefined ||
    toolbar.secondary !== undefined ||
    toolbar.main !== undefined
  const row: CSSProperties = { height: tokens.size.tableRow }
  const paging = view
    ? view.paging === false && local
      ? undefined
      : { current, pageSize: query!.pageSize, total, pageSizeOptions: view.pageSizes ?? [] }
    : pagination || undefined
  const sizes = paging?.pageSizeOptions ?? []
  const picker = sizes.length > 0
  const number = new Intl.NumberFormat(locale)
  const grouped = rowsIn !== undefined
  const pagerProps = paging && {
    current: paging.current,
    pageSize: paging.pageSize,
    total: paging.total,
    showSizeChanger: picker,
    hideOnSinglePage: !picker,
    itemRender: paginationItems(paging.current, t),
    // A shared widget keeps no space around itself, see docs/reference/shared-widgets.md#the-rule.
    style: { marginBottom: 0 },
    showTotal: (all: number, [from, to]: [number, number]) =>
      t('shell.table.range')
        .replace('{from}', number.format(from))
        .replace('{to}', number.format(to))
        .replace('{total}', number.format(all)),
    ...(picker ? { pageSizeOptions: sizes } : {}),
  }
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
  // The pager of a table with group rows, which Ant Design cannot page: a new page size starts again at page 1.
  const onPage = (page: number, pageSize: number) => {
    if (view && query) {
      view.onChange({ ...query, page: pageSize !== query.pageSize ? 1 : page, pageSize })
    } else {
      onChange?.({ current: page, pageSize }, {}, [], { action: 'paginate', currentDataSource: [] })
    }
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
          {/* The search box keeps its own width, so Filter, Columns and Views follow it on one line, see
              docs/reference/actions-and-controls.md#tables. */}
          {(title !== undefined || searchable || filterable || arrangeable || view?.saved !== undefined) && (
            <Flex gap="small" align="center" wrap style={{ minWidth: 0 }}>
              {title !== undefined && <div data-testid="axis-table-title">{title}</div>}
              {searchable && (
                <Input.Search
                  allowClear
                  maxLength={200}
                  style={{ width: 320, maxWidth: '100%' }}
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
              {view?.saved && <TableViewsButton saved={view.saved} />}
            </Flex>
          )}
          <Flex gap="small" style={{ marginInlineStart: 'auto' }}>
            {toolbar.onRefresh && (
              <AxisButton
                role="subtle"
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
                  role="secondary"
                  destructive={action.destructive}
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
          <ConfigProvider theme={theme}>
            <Table<T>
              rowKey={rowKey}
              rowSelection={
                bulk
                  ? {
                      selectedRowKeys: selected.map(rowKey),
                      onChange: (keys) => setSelectedKeys(keys),
                      getCheckboxProps: () => ({ 'aria-label': t('shell.table.selectRow') }),
                      columnWidth: selectionWidth,
                      fixed: room.scrolls,
                      onCell: () => ({ style: staysRoom }),
                    }
                  : undefined
              }
              columns={antColumns}
              expandable={
                details
                  ? {
                      expandedRowKeys: openRows,
                      expandedRowRender: (row) => (
                        <div style={{ padding: `${tokens.spacing.xs}px ${cellPadding}px` }}>{details(row)}</div>
                      ),
                      rowExpandable: (row) => details(row) != null,
                      showExpandColumn: false,
                    }
                  : undefined
              }
              showHeader={!headless}
              // The table moves columns under their row first. Only what stays still and does not fit scrolls sideways.
              scroll={{ x: true }}
              classNames={{ content: scrollClass }}
              // Ant Design otherwise switches to a fixed layout once a column is fixed.
              tableLayout="auto"
              dataSource={tableRows as T[] | undefined}
              loading={loading}
              // The table gives the space that EmptyState no longer keeps, so an empty table keeps its height.
              locale={{
                emptyText: (
                  <div style={{ padding: `${tokens.spacing.xl}px ${tokens.spacing.xs}px` }}>
                    <EmptyState text={emptyText} />
                  </div>
                ),
              }}
              // Ant Design pages the rows it is given, which here include open group rows, so a table with them draws its pager below.
              pagination={paging && !grouped ? pagerProps : false}
              onRow={rowAttributes}
              onChange={view ? onViewChange : onChange}
              styles={{ header: { row }, body: { row } }}
            />
          </ConfigProvider>
          {grouped && pagerProps && (
            <Flex justify="flex-end">
              <Pagination {...pagerProps} onChange={onPage} />
            </Flex>
          )}
        </>
      )}
    </Flex>
  )
}

/**
 * The fold of a row, a subtle `AxisButton` with `Chevron`: named "Show the rows in it" or "Hide the rows in it"
 * for a row with group rows, and "Show the details" or "Hide the details" for a row with details, see
 * docs/reference/actions-and-controls.md#tables.
 */
function RowFold({ group, open, onToggle }: { group: boolean; open: boolean; onToggle: () => void }) {
  const names = group
    ? { show: 'shell.table.showRows', hide: 'shell.table.hideRows' }
    : { show: 'shell.table.showDetails', hide: 'shell.table.hideDetails' }
  return (
    <AxisButton
      role="subtle"
      size="small"
      icon={<Chevron point={open ? 'down' : 'right'} />}
      nameKey={open ? names.hide : names.show}
      aria-expanded={open}
      onClick={onToggle}
    />
  )
}

/**
 * The rows of a table that holds them, filtered by the columns that give `filter` (a row `keep` names stays), searched by the columns that give
 * `searchText`, then sorted by the sorted column's `sortValue`. The search is case-insensitive and ignores the spaces
 * around it. An empty value comes last.
 */
function held<T>(
  rows: readonly T[],
  columns: readonly AxisColumn<T>[],
  query: TableQuery,
  locale: string,
  keep?: (row: T) => boolean,
): T[] {
  const { filter } = query
  const filtered = filter
    ? rows.filter(
        (row) =>
          keep?.(row) === true ||
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

/**
 * The cells of a row that moved under it, one "Title: value" line each, see docs/reference/actions-and-controls.md#tables.
 * They sit in the row's first cell, so the row holds its own values. The zero-width box keeps the lines from widening
 * that column, and the lines are as wide as the row.
 */
function MovedCells({
  lines,
  width,
}: {
  lines: { key: string; title: ReactNode; content: ReactNode }[]
  width: number
}) {
  const box = useRef<HTMLDivElement>(null)
  // The cells that stay leave this much room, so a wrapped line of theirs never runs under the moved lines.
  useLayoutEffect(() => {
    const element = box.current
    const row = element?.closest('tr')
    if (!element || !row) {
      return
    }
    const publish = () => row.style.setProperty(movedHeight, `${element.offsetHeight}px`)
    publish()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(publish)
    observer?.observe(element)
    return () => {
      observer?.disconnect()
      row.style.removeProperty(movedHeight)
    }
  }, [])
  return (
    <div style={{ width: 0 }}>
      <Flex
        ref={box}
        vertical
        data-moved-cells
        style={{ position: 'relative', width: width || undefined, whiteSpace: 'normal' }}
      >
        {lines.map((moved) => (
          <div key={moved.key}>
            <span>{moved.title}</span>: {moved.content}
          </div>
        ))}
      </Flex>
    </div>
  )
}

/**
 * A row's actions: icon or text buttons, or one "…" menu of text entries when they fold. A row folds from three actions
 * on, and at narrow width from two on, so a menu never holds one entry. `fold` folds a row with two or more actions, as
 * `AxisTable` asks for a table set up for three or more, so a menu never holds one entry either. A known Remove,
 * `meaning: 'remove'`, is the trash icon in `dangerText` and a red entry in a menu. It is exported for a list that is
 * not a table, see docs/reference/actions-and-controls.md#tables.
 */
export function RowActions({ actions, fold = false }: { actions: AxisAction[] | null; fold?: boolean }) {
  const t = useText()
  const narrow = useNarrowWidth()
  if (actions === null || actions.length === 0) {
    return null
  }
  const shown = actions.map((action) =>
    action.meaning === 'remove' ? { ...action, icon: <DeleteOutlined />, destructive: true } : action,
  )
  const folded = shown.length > 1 && (fold || narrow || shown.length >= foldFrom)
  if (folded) {
    return (
      <Dropdown
        trigger={['click']}
        autoFocus
        menu={{
          ...menuOf(shown, t),
          'aria-label': t('shell.table.rowActions'),
        }}
      >
        <AxisButton role="subtle" size="small" nameKey="shell.table.rowActions" icon={<EllipsisOutlined />} />
      </Dropdown>
    )
  }
  return (
    <Flex gap="small">
      {shown.map((action) =>
        action.icon ? (
          <AxisButton
            key={action.key}
            role="subtle"
            size="small"
            nameKey={action.labelKey!}
            destructive={action.destructive}
            icon={action.icon}
            nameSuffix={action.nameSuffix}
            disabled={action.disabled}
            onClick={action.onClick}
            {...action.attributes}
          />
        ) : (
          <AxisButton
            key={action.key}
            role="subtle"
            size="small"
            destructive={action.destructive}
            disabled={action.disabled}
            onClick={action.onClick}
            {...action.attributes}
          >
            {actionText(action, t)}
          </AxisButton>
        ),
      )}
    </Flex>
  )
}
