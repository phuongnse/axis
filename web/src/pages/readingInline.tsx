import type { ReactNode } from 'react'
import { formatter, type Reading, type ReadingPart } from './expressionReading'

// Proposed: an expression's reading on one line (#3265).

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
  const type = item.kind === 'all' ? 'conjunction' : 'disjunction'
  const joined = formatter(`list:${type}:${locale}`, () => new Intl.ListFormat(locale, { type })).format(marks)
  return joined
    .split('\u0000')
    .flatMap((piece, index): ReactNode[] =>
      index % 2 === 1
        ? readingInline(item.items[Number(piece)], locale, `${key}.${piece}`)
        : piece === ''
          ? []
          : [<span key={`${key}:${index}`}>{piece}</span>],
    )
}
