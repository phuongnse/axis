import { EllipsisOutlined, ReloadOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Dropdown, Flex, Table, type TableColumnType, type TableProps } from 'antd'
import {
  isValidElement,
  useEffect,
  useLayoutEffect,
  useReducer,
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
import { markCurrentPage } from './pagination'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'
import { visuallyHidden } from './visuallyHidden'

/**
 * How wide a column is (D129).
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
  /**
   * Proposed: a column is searchable when it says the text of a row to search in, as a field marked `searchable` is.
   * Search looks in every such column.
   */
  searchText?: (row: T) => string | null
  /** Proposed: when the table lacks room, a `low` column moves under its row first and a `high` one last. */
  priority?: 'high' | 'low'
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

/**
 * The toolbar of every table (D96). The view controls, such as search, sit on the left. On the right come Refresh,
 * the secondary actions such as Import and Export, and the main action last. Each is there only when the table has it.
 */
export interface AxisTableToolbar {
  view?: ReactNode
  /** Proposed: the Columns control, before Refresh. */
  columns?: ReactNode
  onRefresh?: () => void
  secondary?: ReactNode
  main?: ReactNode
}

export interface AxisTableProps<T> {
  columns: AxisColumn<T>[]
  rowKey: (row: T) => Key
  dataSource?: readonly T[]
  loading?: boolean
  /** Proposed: the test id of the table's wrapper. */
  testId?: string
  emptyText: string
  /** The paging, or `false` for a table that lists every row on one page. */
  pagination:
    | {
        /** Set with `total` for a table paged by the server or the URL; left out, the table pages its own rows. */
        current?: number
        pageSize: number
        total?: number
        pageSizeOptions?: number[]
        /** Proposed: a table paged by itself, such as a release list, gives no paging below one page. */
        hideOnSinglePage?: boolean
        showSizeChanger?: boolean
      }
    | false
  onChange?: TableProps<T>['onChange']
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
  /** Whether a row is a group row: its empty cells stay blank instead of showing the em dash. */
  blankRow?: (row: T) => boolean
  /** Attributes for the row's element, such as test ids and data attributes. */
  rowAttributes?: (row: T) => HTMLAttributes<HTMLElement>
}

/** Column key of the row actions. A field name starts with a letter, so no field column has it. */
const actionsKey = '$actions'

/** A width of 1 % makes an auto-layout column as narrow as its content, which `nowrap` keeps on one line. */
const fitContent = '1%'

/** The most a free-text value is wide before it wraps, in px (D129). */
const freeTextMaxWidth = 320

/** Every cell keeps spacing-lg on each side, so columns fitted to their content do not crowd each other. */
const cellInline = 24

/** Ant Design's own padding of the cell that holds the columns under a row, which `cellInline` lines up with. */
const movedCellInline = 16

const priorityRank = { low: 0, undefined: 1, high: 2 } as const

/**
 * Proposed: the columns that may move under their row, in the order they move: every column but the identifying one, the
 * first, by priority and then from the right.
 */
function moveOrder<T>(columns: readonly AxisColumn<T>[]): string[] {
  return columns
    .slice(1)
    .map((column, index) => ({ key: column.key, index, rank: priorityRank[`${column.priority}`] }))
    .sort((a, b) => a.rank - b.rank || b.index - a.index)
    .map((column) => column.key)
}

const isEmptyCell = (value: ReactNode) => value == null || (isValidElement(value) && value.type === EmptyDash)

/** The row's actions fold into one menu from this many on, and at narrow width whatever their number (D96). */
const foldFrom = 3

/**
 * The one table of the platform (D129). It draws the toolbar in its fixed order, the columns by their kind, rows and a
 * header of the `tableRow` size, the row actions, folded into one "…" menu from three on or below 992 px, the paging,
 * sideways scrolling when the page is narrower than the columns, and the em dash for an empty cell.
 */
export function AxisTable<T extends object>({
  columns,
  rowKey,
  dataSource,
  loading = false,
  testId,
  emptyText,
  pagination,
  onChange,
  toolbar = {},
  below,
  failure,
  rowActions,
  blankRow,
  rowAttributes,
}: AxisTableProps<T>) {
  const t = useText()
  const tokens = useDesignTokens()
  const { colour, spacing } = tokens
  const narrow = useNarrowWidth()
  const host = useRef<HTMLDivElement>(null)
  const [movedCount, setMovedCount] = useState(0)
  const [scrolls, setScrolls] = useState(false)
  // With no column moved, a new width changes no state, and the table must still measure itself.
  const [measured, measureAgain] = useReducer((count: number) => count + 1, 0)
  const rows = dataSource ?? []
  const order = moveOrder(columns)
  const moved = new Set(order.slice(0, movedCount))
  const shown = columns.filter((column) => !moved.has(column.key))
  const under = columns.filter((column) => moved.has(column.key))

  // Before the browser paints, one more column moves while the table is wider than its place. The place takes its width
  // from the page alone, through `contain`, or a wide table would widen it and never look too wide.
  useLayoutEffect(() => {
    const width = host.current?.clientWidth ?? 0
    const table = host.current?.querySelector('table')
    if (!table || width === 0) {
      return
    }
    const over = table.offsetWidth > width
    if (over && movedCount < order.length) {
      setMovedCount(movedCount + 1)
    } else {
      setScrolls(over)
    }
  }, [movedCount, order.length, dataSource, measured])

  // A new width starts over, as columns that moved may fit again.
  useEffect(() => {
    if (!host.current) {
      return
    }
    let width = host.current.clientWidth
    const observer = new ResizeObserver(([entry]) => {
      if (entry.contentRect.width !== width) {
        width = entry.contentRect.width
        setMovedCount(0)
        setScrolls(false)
        measureAgain()
      }
    })
    observer.observe(host.current)
    return () => observer.disconnect()
  }, [])

  /** What a column's cell shows for a row, or `null` when the cell has no value. */
  const cellOf = (column: AxisColumn<T>, row: T, index: number): ReactNode => {
    const path = column.dataIndex === undefined ? [] : [column.dataIndex].flat()
    const value = path.reduce<unknown>(
      (at, key) => (at as Record<PropertyKey, unknown> | undefined)?.[key as string],
      row,
    )
    const content = column.render ? column.render(value, row, index) : (value as ReactNode)
    const empty =
      content === null || content === undefined || (!column.render && content === '') || isEmptyCell(content)
    return empty ? null : content
  }
  const cellsUnder = (row: T, index: number) =>
    under
      .map((column) => ({ key: column.key, title: column.title as ReactNode, value: cellOf(column, row, index) }))
      .filter((cell) => cell.value !== null)
  // A row with cells under it ends below them, so the line between its two parts goes.
  const closesRow = (row: T) => under.length === 0 || cellsUnder(row, rows.indexOf(row)).length === 0
  const freeTexts = shown.filter((column) => column.kind === 'freeText').length
  // Each free-text column takes an equal share of the width. Without one, the last column takes what is left.
  const share = freeTexts > 0 ? `${100 / freeTexts}%` : undefined
  const lastKey = shown.at(-1)?.key
  const fold = narrow || (rowActions?.configured ?? 0) >= foldFrom

  const line: CSSProperties = { lineHeight: `${tokens.size.smallControl}px`, verticalAlign: 'top' }
  const antColumns: TableColumnType<T>[] = shown.map(
    ({ kind, render, searchText: _searchText, priority: _priority, onCell, onHeaderCell, ...column }, place) => {
      const freeText = kind === 'freeText'
      const width = freeText ? share : freeTexts === 0 && column.key === lastKey ? undefined : fitContent
      const sizing: CSSProperties = {
        width,
        paddingInline: cellInline,
        ...(freeText ? {} : { whiteSpace: 'nowrap' }),
        ...line,
      }
      return {
        ...column,
        fixed: scrolls && place === 0 ? 'left' : undefined,
        onHeaderCell: (data, index) => {
          const own = onHeaderCell?.(data, index)
          return { ...own, style: { ...sizing, whiteSpace: 'nowrap', ...own?.style } }
        },
        onCell: (row, index) => {
          const own = onCell?.(row, index)
          return { ...own, style: { ...sizing, ...(closesRow(row) ? {} : { borderBottom: 'none' }), ...own?.style } }
        },
        render: (value: unknown, row: T, index: number) => {
          const content = render ? render(value, row, index) : value
          const empty = content === null || content === undefined || (!render && content === '')
          if (empty) {
            return blankRow?.(row) ? null : <EmptyDash />
          }
          const node = content as ReactNode
          return freeText ? (
            <div style={{ maxWidth: freeTextMaxWidth, minWidth: 160, whiteSpace: 'normal', overflowWrap: 'anywhere' }}>
              {node}
            </div>
          ) : (
            node
          )
        },
      }
    },
  )
  if (rowActions && rowActions.configured > 0) {
    const cell = (row?: T) => ({
      style: {
        width: fitContent,
        whiteSpace: 'nowrap',
        paddingInline: cellInline,
        ...line,
        ...(row !== undefined && !closesRow(row) ? { borderBottom: 'none' } : {}),
      } satisfies CSSProperties,
    })
    antColumns.push({
      key: actionsKey,
      // A header without text fails the accessibility check, so the column is named for screen readers only.
      title: <span style={visuallyHidden}>{t('shell.table.rowActions')}</span>,
      onHeaderCell: () => cell(),
      onCell: (row: T) => cell(row),
      render: (_value: unknown, row: T) => <RowActions actions={rowActions.actions(row)} fold={fold} />,
    })
  }

  const hasToolbar =
    toolbar.view !== undefined ||
    toolbar.columns !== undefined ||
    toolbar.onRefresh !== undefined ||
    toolbar.secondary !== undefined ||
    toolbar.main !== undefined
  const row: CSSProperties = { height: tokens.size.tableRow }
  return (
    <Flex vertical gap="middle" aria-busy={loading} data-testid={testId}>
      {hasToolbar && (
        <Flex justify="space-between" align="center" gap="small" wrap>
          {toolbar.view}
          <Flex gap="small" style={{ marginInlineStart: 'auto' }}>
            {toolbar.columns}
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
      {below}
      {failure ?? (
        <div ref={host} style={{ contain: 'inline-size' }}>
          <ConfigProvider theme={tableTheme(tokens)}>
            <Table<T>
              rowKey={rowKey}
              columns={antColumns}
              // Where even the columns that stay do not fit, the table scrolls sideways.
              scroll={scrolls ? { x: 'max-content' } : undefined}
              expandable={
                under.length === 0
                  ? undefined
                  : {
                      showExpandColumn: false,
                      expandedRowKeys: rows.filter((r, index) => cellsUnder(r, index).length > 0).map(rowKey),
                      rowExpandable: (r) => cellsUnder(r, rows.indexOf(r)).length > 0,
                      expandedRowRender: (r, index) => (
                        <Flex
                          wrap
                          gap={`${spacing.xs}px ${spacing.lg}px`}
                          data-testid="axis-table-moved"
                          style={{ marginBlockStart: -spacing.sm, paddingInlineStart: cellInline - movedCellInline }}
                        >
                          {cellsUnder(r, index).map((cell) => (
                            <span key={cell.key}>
                              <span style={{ color: colour.textMuted }}>{cell.title}: </span>
                              {cell.value}
                            </span>
                          ))}
                        </Flex>
                      ),
                    }
              }
              dataSource={dataSource as T[] | undefined}
              loading={loading}
              // Proposed: an empty table keeps its header and says so in its body in textMuted. Ant Design draws the text
              // at a quarter of the text colour, 1.8:1 on surface, which fails the 4.5:1 that text needs.
              locale={{ emptyText: <EmptyState text={emptyText || undefined} /> }}
              pagination={
                pagination && {
                  showSizeChanger: true,
                  ...pagination,
                  itemRender: pagination.current === undefined ? undefined : markCurrentPage(pagination.current),
                  // Proposed: every paged table says what it shows of how many.
                  showTotal: (total, [start, end]) =>
                    total === 0
                      ? ''
                      : t('shell.table.range')
                          .replace('{start}', String(start))
                          .replace('{end}', String(end))
                          .replace('{total}', String(total)),
                }
              }
              onRow={rowAttributes}
              onChange={onChange}
              styles={{ header: { row }, body: { row } }}
            />
          </ConfigProvider>
        </div>
      )}
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
