import { StreamLanguage, type StringStream } from '@codemirror/language'

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
