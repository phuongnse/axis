import { EllipsisOutlined, ReloadOutlined } from '@ant-design/icons'
import { Button, Dropdown, Flex, Table, Tooltip, type TableColumnType, type TableProps } from 'antd'
import type { CSSProperties, HTMLAttributes, Key, ReactNode } from 'react'
import { AxisButton } from './AxisButton'
import { sizeTokens, spacingTokens } from './designTokens'
import { useText } from './texts'
import { useNarrowWidth } from './useNarrowWidth'

/**
 * Proposed: how wide a column is.
 * - `fit`: a value of a fixed form, such as a number, a date, a date and time, a status tag or the row's icon actions,
 *   as wide as its content and never wrapping.
 * - `text`: free text, as wide as its content but no narrower than the `textColumn` size and no wider than 320 px,
 *   where it wraps, so a short value such as a person's name neither crowds nor leaves empty width.
 * - `fill`: the one column of free text that takes the width the others leave, the longest and the one most worth
 *   reading. Its text still wraps at 320 px. A table that marks none gives the width left to its last `text` column,
 *   as an identifying code usually comes first and a name or a description later, else to its last column.
 */
export type ColumnKind = 'fit' | 'text' | 'fill'

/** A column of an {@link AxisTable}: Ant Design's column, whose width and cells the table sets from its kind. */
export type AxisColumn<T> = Omit<TableColumnType<T>, 'render' | 'onCell' | 'onHeaderCell' | 'width' | 'key'> & {
  key: string
  kind: ColumnKind
  /** Numbers are right-aligned, with digits of equal width so they line up. */
  numeric?: boolean
  render: (row: T, index: number) => ReactNode
}

/**
 * An action on one row. One with an `icon` is an icon button whose name is its tooltip and accessible name, as the
 * platform gives an icon only to an action whose meaning it knows; one an author configured keeps its text.
 */
export interface AxisRowAction {
  key: string
  /** The text key of its name. */
  labelKey: string
  /** Appended to an icon button's accessible name only, such as the row's number. */
  nameSuffix?: string
  icon?: ReactNode
  destructive?: boolean
  disabled?: boolean
  onClick: () => void
  /** Attributes for the button, such as a test id or the canvas's selection. */
  attributes?: object
}

/**
 * Proposed: the toolbar of every table (D96): the view controls, such as search, on the left; Refresh, the secondary
 * actions such as Import and Export, and the main action last on the right. Each is there only when the table needs
 * it, and always in that place.
 */
export interface AxisTableToolbar {
  view?: ReactNode
  refresh?: () => void
  secondary?: ReactNode
  main?: ReactNode
}

/**
 * Proposed: every row, the header's too, is the `tableRow` size high, 40 px, whatever it holds: spacing-xs above and
 * below a line of the `smallControl` size, which a line of text and a small icon button both fill. A row of more lines
 * grows and reads from the same first line.
 */
const rowLine: CSSProperties = {
  height: sizeTokens.tableRow,
  boxSizing: 'border-box',
  paddingBlock: spacingTokens.xs,
  lineHeight: `${sizeTokens.smallControl}px`,
  verticalAlign: 'top',
}

/** Every cell keeps spacing-lg on each side, so columns fitted to their content do not crowd each other. */
const cellInline = spacingTokens.lg

/** A column as wide as its content, never wrapping. */
const fitted: CSSProperties = { width: '1%', whiteSpace: 'nowrap', paddingInline: cellInline }

/** The value of a `text` or `fill` cell: no narrower than `textColumn`, wrapping at 320 px. */
const textValue: CSSProperties = {
  width: 'max-content',
  minWidth: sizeTokens.textColumn,
  maxWidth: 320,
  overflowWrap: 'anywhere',
  whiteSpace: 'normal',
}

/** The row's actions fold into one menu from this many on, and at narrow width whatever their number (D96). */
const foldFrom = 3

/**
 * Proposed: the one table of the platform, which every table is built on: the `table` widget, Studio's and the
 * console's. It draws the toolbar, the columns by their kind, rows of the `tableRow` size, the row actions, folded into one "…" menu
 * from three on or below 992 px, the paging, and sideways scrolling when the table is narrower than its columns. A
 * cell with no value shows `EmptyDash`, which the column's `render` returns.
 */
export function AxisTable<T extends object>({
  rows,
  rowKey,
  columns,
  rowActions,
  toolbar,
  above,
  loading,
  emptyText,
  pagination,
  onChange,
  onRow,
  testId,
  instead,
}: {
  rows: readonly T[] | undefined
  rowKey: (row: T) => Key
  columns: AxisColumn<T>[]
  /** The actions of a row, or none. Every row of a table counts the same configured actions, so it has one layout. */
  rowActions?: { count: number; of: (row: T) => AxisRowAction[] }
  toolbar?: AxisTableToolbar
  /** What sits between the toolbar and the rows, such as a filter bar or a message. */
  above?: ReactNode
  loading?: boolean
  emptyText: string
  pagination: TableProps<T>['pagination']
  onChange?: TableProps<T>['onChange']
  onRow?: (row: T) => HTMLAttributes<HTMLElement>
  testId?: string
  /** What shows in place of the rows, such as a message that they did not load. The toolbar stays. */
  instead?: ReactNode
}) {
  const narrow = useNarrowWidth()
  const fillKey =
    columns.find((column) => column.kind === 'fill')?.key ??
    columns.findLast((column) => column.kind === 'text')?.key ??
    columns.at(-1)?.key
  const fold = narrow || (rowActions?.count ?? 0) >= foldFrom

  const antColumns: TableColumnType<T>[] = columns.map(({ kind, numeric, render, ...column }) => {
    const fills = column.key === fillKey
    const numbers: CSSProperties = numeric ? { textAlign: 'right', fontVariantNumeric: 'tabular-nums' } : {}
    const cell: CSSProperties = fills
      ? { paddingInline: cellInline, ...rowLine, ...numbers }
      : kind === 'fit'
        ? { ...fitted, ...rowLine, ...numbers }
        : { width: '1%', paddingInline: cellInline, ...rowLine }
    const header: CSSProperties = fills
      ? { paddingInline: cellInline, ...rowLine, ...(numeric ? { textAlign: 'right' } : {}) }
      : { ...fitted, ...rowLine, ...(numeric ? { textAlign: 'right' } : {}) }
    return {
      ...column,
      onHeaderCell: () => ({ style: header }),
      onCell: () => ({ style: cell }),
      render: (_: unknown, row: T, index: number) =>
        kind === 'fit' ? render(row, index) : <div style={textValue}>{render(row, index)}</div>,
    }
  })
  if (rowActions && rowActions.count > 0) {
    antColumns.push({
      key: '$actions',
      onHeaderCell: () => ({ style: { ...fitted, ...rowLine } }),
      onCell: () => ({ style: { ...fitted, ...rowLine } }),
      render: (_: unknown, row: T) => <RowActions actions={rowActions.of(row)} fold={fold} />,
    })
  }

  const hasToolbar =
    toolbar !== undefined &&
    (toolbar.view !== undefined ||
      toolbar.refresh !== undefined ||
      toolbar.secondary !== undefined ||
      toolbar.main !== undefined)
  return (
    <Flex vertical gap="middle" aria-busy={loading} data-testid={testId}>
      {hasToolbar && (
        <Flex justify="space-between" align="center" gap="small" wrap>
          {toolbar.view}
          <Flex gap="small" style={{ marginInlineStart: 'auto' }}>
            {toolbar.refresh && (
              <AxisButton
                role="icon"
                nameKey="shell.table.refresh"
                icon={<ReloadOutlined />}
                onClick={toolbar.refresh}
              />
            )}
            {toolbar.secondary}
            {toolbar.main}
          </Flex>
        </Flex>
      )}
      {above}
      {instead ?? (
        <Table<T>
          rowKey={rowKey}
          columns={antColumns}
          // Where the page is narrower than the columns' content, the table scrolls sideways.
          scroll={{ x: 'max-content' }}
          dataSource={rows as T[] | undefined}
          loading={loading}
          locale={{ emptyText }}
          pagination={pagination}
          onChange={onChange}
          onRow={onRow}
        />
      )}
    </Flex>
  )
}

/** A row's actions: icon or text buttons, or one "…" menu of text entries when they fold. */
function RowActions({ actions, fold }: { actions: AxisRowAction[]; fold: boolean }) {
  const t = useText()
  if (actions.length === 0) {
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
        <Tooltip title={t('shell.table.rowActions')} trigger={['hover', 'focus']}>
          <Button type="text" size="small" icon={<EllipsisOutlined />} aria-label={t('shell.table.rowActions')} />
        </Tooltip>
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
            nameSuffix={action.nameSuffix}
            destructive={action.destructive}
            icon={action.icon}
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
