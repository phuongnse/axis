import { AxisTable, type AxisColumn } from './AxisTable'
import { fillText } from './fillText'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useEmptyStyle } from './useEmptyStyle'
import { valueRows, type ValueRow, type ValueWord } from './valueRows'

/** The share of the 12 columns that the names take, and the values take the rest. */
const nameColumns = 5

/**
 * Names and their values, such as a run's input, as a compact table of two columns with no header row, see
 * docs/reference/actions-and-controls.md#text. The names are in `textMuted` over 5 of 12 columns, and a word in place of a value,
 * such as `null`, Empty, a count or Hidden, takes the empty value's style. Shared widgets are in
 * docs/reference/shared-widgets.md#which-widget.
 */
export function ValueTable({ value, testId }: { value: unknown; testId?: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const empty = useEmptyStyle()
  const words: Record<ValueWord, (count?: number) => string> = {
    null: () => 'null',
    empty: () => t('shell.value.empty'),
    hidden: () => t('shell.value.hidden'),
    fields: (count) =>
      count === 1 ? t('shell.value.field') : fillText(t('shell.value.fields'), { count: count ?? 0 }),
    items: (count) => (count === 1 ? t('shell.value.item') : fillText(t('shell.value.items'), { count: count ?? 0 })),
  }
  const columns: AxisColumn<ValueRow>[] = [
    {
      key: 'name',
      kind: 'freeText',
      dataIndex: 'name',
      onCell: () => ({ style: { width: `${(nameColumns / 12) * 100}%`, color: colour.textMuted } }),
    },
    {
      key: 'value',
      kind: 'freeText',
      onCell: () => ({ style: { width: `${((12 - nameColumns) / 12) * 100}%` } }),
      render: (_value, row) =>
        row.word ? <span style={empty}>{words[row.word](row.count)}</span> : row.text === '' ? null : row.text,
    },
  ]
  return (
    <AxisTable
      compact
      headless
      columns={columns}
      rowKey={(row) => String(row.id)}
      dataSource={valueRows(value)}
      testId={testId}
    />
  )
}
