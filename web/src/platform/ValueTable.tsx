import { Segmented, Typography } from 'antd'
import { useState } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import { CodeBlock } from './CodeBlock'
import { useText } from './texts'
import { jsonRows, parseJsonText, type ValueRow } from './valueRows'

/**
 * Proposed, see the review on #3650: names and their values as a table of two columns, as Postman and the browser's
 * developer tools show headers and Azure Logic Apps an action's inputs and outputs. It is an `AxisTable` with no
 * toolbar, compact, every row shown; a nested value is a row of its own named by its path, see `jsonRows`. A
 * value is shown as it was written, a number with its source digits; `null` and empty text read in muted
 * words, so neither passes for text. It has no header row, as the developer tools' headers have none: each name, in
 * `textMuted` as a term's, says what its row is.
 */
export function ValueTable({ rows, label }: { rows: readonly ValueRow[]; label: string }) {
  const t = useText()
  const muted = (text: string) => <Typography.Text type="secondary">{text}</Typography.Text>
  const columns: AxisColumn<ValueRow>[] = [
    {
      key: 'name',
      kind: 'fixed',
      title: t('shell.valueTable.name'),
      render: (_value, row) => <Typography.Text type="secondary">{row.name}</Typography.Text>,
    },
    {
      key: 'value',
      kind: 'freeText',
      title: t('shell.valueTable.value'),
      render: (_value, row) => {
        const value = row.value
        switch (value.kind) {
          case 'null':
            return muted('null')
          case 'empty':
            return muted(t('shell.valueTable.empty'))
          case 'fields':
            return muted(t('shell.valueTable.fields').replace('{count}', String(value.count)))
          case 'items':
            return muted(t('shell.valueTable.items').replace('{count}', String(value.count)))
          case 'expression':
            return <Typography.Text code>{value.text}</Typography.Text>
          case 'change':
            // A change reads as the value after it, with the value before it under, muted.
            return value.hidden ? (
              muted(t('shell.valueTable.hidden'))
            ) : (
              <span style={{ display: 'flex', flexDirection: 'column' }}>
                <span style={{ overflowWrap: 'anywhere' }}>{value.after ?? t('shell.value.notSet')}</span>
                <Typography.Text type="secondary">
                  {t('shell.valueTable.before').replace('{value}', value.before ?? t('shell.value.notSet'))}
                </Typography.Text>
              </span>
            )
          default:
            return <span style={{ overflowWrap: 'anywhere' }}>{value.text}</span>
        }
      },
    },
  ]
  return (
    <section aria-label={label}>
      <AxisTable<ValueRow>
        rowKey={(row) => row.key}
        dataSource={rows}
        columns={columns}
        emptyText={t('shell.valueTable.none')}
        compact
        headless
      />
    </section>
  )
}

/**
 * Proposed, see the review on #3650: a body as it was sent or came back. JSON shows as a `ValueTable` by default, with
 * a switch to the JSON itself, laid out and copyable, as Postman's Pretty and Raw and Logic Apps' raw inputs; any other
 * text, such as HTML, shows as it is.
 */
export function BodyView({ text, label }: { text: string; label: string }) {
  const t = useText()
  const [shown, setShown] = useState<'table' | 'json'>('table')
  const parsed = parseJsonText(text)
  const structured = typeof parsed === 'object' && parsed !== null
  if (!structured) {
    return <CodeBlock label={label} value={text} />
  }
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <Segmented
        aria-label={t('shell.valueTable.show')}
        size="small"
        value={shown}
        onChange={(value) => setShown(value as 'table' | 'json')}
        options={[
          { value: 'table', label: t('shell.valueTable.table') },
          { value: 'json', label: 'JSON' },
        ]}
        style={{ alignSelf: 'flex-start' }}
      />
      {shown === 'table' ? (
        <ValueTable label={label} rows={jsonRows(parsed)} />
      ) : (
        <CodeBlock label={label} value={text} />
      )}
    </div>
  )
}
