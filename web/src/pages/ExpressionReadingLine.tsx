import { Typography } from 'antd'
import type { ReactNode } from 'react'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { Reading, ReadingPart } from './expressionReading'

// Proposed: the line under the expression box that reads the expression back in words (#3265, step 1).

/**
 * The expression in words: a name by its label in bold, values and words as plain text. A group of plain conditions
 * reads on one line, joined by "and" or "or" in the author's language; a group that holds a group reads as a list
 * under "All of these" or "At least one of these", so the brackets of the expression read as indents.
 */
export function ExpressionReadingLine({ reading, locale }: { reading: Reading; locale: string }) {
  const t = useText()
  const tokens = useDesignTokens()

  // One flat list of parts, each with a key of its own, as Typography flattens fragments and would mix their keys.
  const phrase = (parts: ReadingPart[], key: string): ReactNode[] =>
    parts.map((part, index) =>
      part.kind === 'name' ? (
        <strong key={`${key}.${index}`}>{part.text}</strong>
      ) : (
        <span key={`${key}.${index}`}>{part.text}</span>
      ),
    )

  const flat = (item: Reading): item is Extract<Reading, { kind: 'phrase' }> => item.kind === 'phrase'

  const inline = (item: Reading, key = 'r'): ReactNode[] => {
    if (item.kind === 'phrase') {
      return phrase(item.parts, key)
    }
    const marks = item.items.map((_, index) => `\u0000${index}\u0000`)
    const joined = new Intl.ListFormat(locale, { type: item.kind === 'all' ? 'conjunction' : 'disjunction' }).format(
      marks,
    )
    return joined
      .split('\u0000')
      .flatMap((piece, index) =>
        index % 2 === 1
          ? inline(item.items[Number(piece)], `${key}.${piece}`)
          : piece === ''
            ? []
            : [<span key={`${key}:${index}`}>{piece}</span>],
      )
  }

  const list = (item: Exclude<Reading, { kind: 'phrase' }>): ReactNode => (
    <>
      {t(`shell.studio.expression.reads.${item.kind}`)}
      <ul style={{ margin: 0, paddingInlineStart: tokens.spacing.lg }}>
        {item.items.map((child, index) => (
          <li key={index}>
            {child.kind !== 'phrase' && !child.items.every(flat) ? list(child) : inline(child, `${index}`)}
          </li>
        ))}
      </ul>
    </>
  )

  const nested = reading.kind !== 'phrase' && !reading.items.every(flat)

  return (
    <div data-testid="studio-expression-reading">
      <Typography.Text type="secondary">{t('shell.studio.expression.reads.label')}</Typography.Text>{' '}
      {nested ? list(reading) : <Typography.Text>{inline(reading)}</Typography.Text>}
    </div>
  )
}
