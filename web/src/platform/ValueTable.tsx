import { Segmented, Typography } from 'antd'
import { useContext, useState } from 'react'
import { statusTints } from './antTheme'
import { AxisTable, type AxisColumn } from './AxisTable'
import { CodeBlock } from './CodeBlock'
import { useText } from './texts'
import { ThemeModeContext, useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'
import { jsonRows, parseJsonText, type ValueRow } from './valueRows'

/**
 * Proposed, see the review on #3650: names and their values as a table of two columns, as Postman and the browser's
 * developer tools show headers and Azure Logic Apps an action's inputs and outputs. It is an `AxisTable` with no
 * toolbar, compact, every row shown; a nested value is a row of its own named by its path, see `jsonRows`. A
 * value is shown as it was written, a number with its source digits; `null` and empty text read in muted
 * words, so neither passes for text. It has no header row, as the developer tools' headers have none: each name, in
 * `textMuted` as a term's, says what its row is. Its name column is `nameWidth` wide in every table, a longer name
 * wrapping, so the tables one view stacks line up from top to bottom, as the developer tools' header sections do.
 */
/** Proposed, see the review on #3650: the width of a name column, its padding in. */
const nameWidth = 160

export function ValueTable({ rows, label }: { rows: readonly ValueRow[]; label: string }) {
  const t = useText()
  const muted = (text: string) => <Typography.Text type="secondary">{text}</Typography.Text>
  const columns: AxisColumn<ValueRow>[] = [
    {
      key: 'name',
      kind: 'fixed',
      title: t('shell.valueTable.name'),
      // Every name column is as wide, so the tables a view stacks, such as a request's headers and body, line up.
      onCell: () => ({
        style: { width: nameWidth, minWidth: nameWidth, whiteSpace: 'normal', overflowWrap: 'anywhere' },
      }),
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
            // Proposed, see the review on #3650: a change reads as a diff, as GitHub shows lines and Azure DevOps a
            // work item's fields: the value before on a line of its own, "−" on a `danger` tint, then the value after,
            // "+" on a `success` tint; a value set where there was none is one "+" line, a value cleared one "−" line.
            // The sign says it as well as the colour, and assistive technology reads it as a sentence.
            return value.hidden ? (
              muted(t('shell.valueTable.hidden'))
            ) : (
              <span style={{ overflowWrap: 'anywhere' }}>
                <span aria-hidden style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                  {value.before !== null && <DiffLine sign="−" tone="danger" text={value.before} />}
                  {value.after !== null && <DiffLine sign="+" tone="success" text={value.after} />}
                </span>
                <span style={visuallyHidden}>
                  {t('shell.valueTable.changed')
                    .replace('{before}', value.before ?? t('shell.value.notSet'))
                    .replace('{after}', value.after ?? t('shell.value.notSet'))}
                </span>
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

/** One line of a change: its sign and value on the tint of what it does, a value removed or a value added. */
function DiffLine({ sign, tone, text }: { sign: string; tone: 'danger' | 'success'; text: string }) {
  const { colour, spacing } = useDesignTokens()
  const mode = useContext(ThemeModeContext)?.mode ?? 'light'
  const tint = statusTints(mode)[tone]
  return (
    <span
      style={{
        display: 'flex',
        gap: spacing.xs,
        paddingInline: spacing.xs,
        borderRadius: 2,
        background: tint.background,
        color: tone === 'danger' ? colour.dangerText : colour.successText,
      }}
    >
      <span style={{ flex: 'none' }}>{sign}</span>
      <span style={{ minWidth: 0 }}>{text}</span>
    </span>
  )
}
