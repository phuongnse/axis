import { StreamLanguage, StringStream } from '@codemirror/language'

const keywords = new Set(['and', 'or', 'not', 'is', 'in', 'null', 'true', 'false'])

function token(stream: StringStream): string | null {
  if (stream.eatSpace()) {
    return null
  }
  if (stream.eat("'")) {
    while (!stream.eol()) {
      if (stream.next() === "'" && !stream.eat("'")) {
        break
      }
    }
    return 'string'
  }
  if (stream.match(/^\d+(\.\d+)?/)) {
    return 'number'
  }
  if (stream.match(/^[A-Za-z][A-Za-z0-9-]*(?=:)/)) {
    return 'namespace'
  }
  if (stream.match(/^[A-Za-z][A-Za-z0-9]*/)) {
    return keywords.has(stream.current().toLowerCase()) ? 'keyword' : 'variableName'
  }
  if (stream.match(/^(==|!=|<=|>=|[<>+\-*/])/)) {
    return 'operator'
  }
  if (stream.match(/^[.,():]/)) {
    return 'punctuation'
  }
  stream.next()
  return null
}

/** The expression grammar of docs/reference/expressions.md, for highlighting only. The compile is the checker. */
export const expressionLanguage = StreamLanguage.define({ name: 'axis-expression', token })

/** One token of an expression, as the highlighter reads it. `type` is the highlighter's token type, `null` for a character it does not know. */
export interface ExpressionToken {
  type: string | null
  text: string
  from: number
}

/**
 * The tokens of a text, without the white space, from the same `token` function that highlights, so completion and
 * highlighting read one grammar. A `string` token is still open when its text holds an odd number of quotes.
 */
export function expressionTokens(text: string): ExpressionToken[] {
  const tokens: ExpressionToken[] = []
  let offset = 0
  for (const line of text.split('\n')) {
    const stream = new StringStream(line, 4, 2)
    while (!stream.eol()) {
      stream.start = stream.pos
      const type = token(stream)
      const word = stream.current()
      if (word.trim() !== '') {
        tokens.push({ type, text: word, from: offset + stream.start })
      }
    }
    offset += line.length + 1
  }
  return tokens
}

/** Whether a `string` token has no closing quote yet. */
export function isOpenString(item: ExpressionToken): boolean {
  return item.type === 'string' && (item.text.match(/'/g) ?? []).length % 2 === 1
}
