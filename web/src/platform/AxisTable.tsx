import { EllipsisOutlined, ReloadOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Dropdown, Flex, Table, type TableColumnType, type TableProps } from 'antd'
import type { CSSProperties, HTMLAttributes, ReactNode } from 'react'
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
   * when `pageSizeOptions` offers sizes, and a pager with no picker hides on a single page.
   */
  pagination?: { current: number; pageSize: number; total: number; pageSizeOptions?: number[] } | false
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
  rowAttributes?: (row: T, index?: number) => HTMLAttributes<HTMLElement>
  /** The test id of the table's outer element. */
  testId?: string
}

/** Column key of the row actions. A field name starts with a letter, so no field column has it. */
const actionsKey = '$actions'

/** A width of 1 % makes an auto-layout column as narrow as its content, which `nowrap` keeps on one line. */
const fitContent = '1%'

/** The most a free-text value is wide before it wraps, in px, see docs/reference/actions-and-controls.md#tables. */
const freeTextMaxWidth = 320

/**
 * The row's actions fold into one menu from this many on, and at narrow width whatever their number, see
 * docs/reference/actions-and-controls.md#tables.
 */
const foldFrom = 3

/**
 * The one table of the platform, see docs/reference/actions-and-controls.md#tables. It draws the toolbar in its fixed
 * order, the columns by their kind, rows and a header of the `tableRow` size, the row actions, folded into one "…" menu
 * from three on or below 992 px, the paging, sideways scrolling when the page is narrower than the columns, and the em
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
  toolbar = {},
  below,
  failure,
  rowActions,
  blankRow,
  rowAttributes,
  testId,
}: AxisTableProps<T>) {
  const t = useText()
  const tokens = useDesignTokens()
  const narrow = useNarrowWidth()
  const freeTexts = columns.filter((column) => column.kind === 'freeText').length
  // Each free-text column takes an equal share of the width. Without one, the last column takes what is left.
  const share = freeTexts > 0 ? `${100 / freeTexts}%` : undefined
  const lastKey = columns.at(-1)?.key
  const fold = narrow || (rowActions?.configured ?? 0) >= foldFrom

  const line: CSSProperties = { lineHeight: `${tokens.size.smallControl}px`, verticalAlign: 'top' }
  const antColumns: TableColumnType<T>[] = columns.map(({ kind, render, onCell, onHeaderCell, ...column }) => {
    const freeText = kind === 'freeText'
    const width = freeText ? share : freeTexts === 0 && column.key === lastKey ? undefined : fitContent
    const sizing: CSSProperties = { width, ...(freeText ? {} : { whiteSpace: 'nowrap' }), ...line }
    return {
      ...column,
      onHeaderCell: (data, index) => {
        const own = onHeaderCell?.(data, index)
        return { ...own, style: { ...sizing, ...own?.style } }
      },
      onCell: (row, index) => {
        const own = onCell?.(row, index)
        return { ...own, style: { ...sizing, ...own?.style } }
      },
      render: (value: unknown, row: T, index: number) => {
        const shown = render ? render(value, row, index) : value
        const empty = shown === null || shown === undefined || (!render && shown === '')
        if (empty) {
          return blankRow?.(row) ? null : <EmptyDash />
        }
        const content = shown as ReactNode
        return freeText ? (
          <div style={{ maxWidth: freeTextMaxWidth, whiteSpace: 'normal', overflowWrap: 'anywhere' }}>{content}</div>
        ) : (
          content
        )
      },
    }
  })
  if (rowActions && rowActions.configured > 0) {
    const cell = { style: { width: fitContent, whiteSpace: 'nowrap', ...line } satisfies CSSProperties }
    antColumns.push({
      key: actionsKey,
      // A header without text fails the accessibility check, so the column is named for screen readers only.
      title: <span style={visuallyHidden}>{t('shell.table.rowActions')}</span>,
      onHeaderCell: () => cell,
      onCell: () => cell,
      render: (_value: unknown, row: T) => <RowActions actions={rowActions.actions(row)} fold={fold} />,
    })
  }

  const hasToolbar =
    toolbar.view !== undefined ||
    toolbar.onRefresh !== undefined ||
    toolbar.secondary !== undefined ||
    toolbar.main !== undefined
  const row: CSSProperties = { height: tokens.size.tableRow }
  const sizes = pagination ? (pagination.pageSizeOptions ?? []) : []
  const picker = sizes.length > 0
  return (
    <Flex vertical gap="middle" aria-busy={loading} data-testid={testId}>
      {hasToolbar && (
        <Flex justify="space-between" align="center" gap="small" wrap>
          {toolbar.view}
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
      {below}
      {failure ?? (
        <ConfigProvider theme={tableTheme(tokens)}>
          <Table<T>
            rowKey={rowKey}
            columns={antColumns}
            // Where the page is narrower than the columns' content, the table scrolls sideways.
            scroll={{ x: 'max-content' }}
            dataSource={dataSource as T[] | undefined}
            loading={loading}
            locale={{ emptyText: <EmptyState text={emptyText} /> }}
            pagination={
              pagination
                ? {
                    current: pagination.current,
                    pageSize: pagination.pageSize,
                    total: pagination.total,
                    showSizeChanger: picker,
                    hideOnSinglePage: !picker,
                    itemRender: markCurrentPage(pagination.current),
                    ...(picker ? { pageSizeOptions: sizes } : {}),
                  }
                : false
            }
            onRow={rowAttributes}
            onChange={onChange}
            styles={{ header: { row }, body: { row } }}
          />
        </ConfigProvider>
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
