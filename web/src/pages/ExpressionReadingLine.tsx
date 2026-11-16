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

/** A part of a phrase: a name by its label in bold, values and words as plain text. */
function phrase(parts: ReadingPart[], key: string): ReactNode[] {
  // One flat list of parts, each with a key of its own, as Typography flattens fragments and would mix their keys.
  return parts.map((part, index) =>
    part.kind === 'name' ? (
      <strong key={`${key}.${index}`}>{part.text}</strong>
    ) : (
      <span key={`${key}.${index}`}>{part.text}</span>
    ),
  )
}

/**
 * A reading on one line: a phrase, or a group whose conditions are joined by "and" or "or" in the author's language.
 * Proposed (#3265): the starting points read through it too.
 */
export function readingInline(item: Reading, locale: string, key = 'r'): ReactNode[] {
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
        ? readingInline(item.items[Number(piece)], locale, `${key}.${piece}`)
        : piece === ''
          ? []
          : [<span key={`${key}:${index}`}>{piece}</span>],
    )
}
