import { Typography } from 'antd'
import type { ReactNode } from 'react'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { Reading } from './expressionReading'
import { readingInline } from './readingInline'

// Proposed: the line under the expression box that reads the expression back in words (#3265, step 1).

/**
 * The expression in words: a name by its label in bold, values and words as plain text. A group of plain conditions
 * reads on one line, joined by "and" or "or" in the author's language; a group that holds a group reads as a list
 * under "All of these" or "At least one of these", so the brackets of the expression read as indents.
 */
export function ExpressionReadingLine({ reading, locale }: { reading: Reading; locale: string }) {
  const t = useText()
  const tokens = useDesignTokens()

  const flat = (item: Reading): item is Extract<Reading, { kind: 'phrase' }> => item.kind === 'phrase'
  // A group reads as a list under its heading when it holds a group, or when it is negated, as only a heading says so.
  const asList = (item: Reading): item is Exclude<Reading, { kind: 'phrase' }> =>
    item.kind !== 'phrase' && (item.kind === 'notAll' || item.kind === 'none' || !item.items.every(flat))

  const inline = (item: Reading, key = 'r') => readingInline(item, locale, key)

  const list = (item: Exclude<Reading, { kind: 'phrase' }>): ReactNode => (
    <>
      {t(`shell.studio.expression.reads.${item.kind}`)}
      <ul style={{ margin: 0, paddingInlineStart: tokens.spacing.lg }}>
        {item.items.map((child, index) => (
          <li key={index}>{asList(child) ? list(child) : inline(child, `${index}`)}</li>
        ))}
      </ul>
    </>
  )

  return (
    <div data-testid="studio-expression-reading">
      <Typography.Text type="secondary">{t('shell.studio.expression.reads.label')}</Typography.Text>{' '}
      {asList(reading) ? list(reading) : <Typography.Text>{inline(reading)}</Typography.Text>}
    </div>
  )
}
