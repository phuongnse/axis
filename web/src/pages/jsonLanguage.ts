import { StreamLanguage, type StringStream } from '@codemirror/language'

function token(stream: StringStream): string | null {
  if (stream.eatSpace()) {
    return null
  }
  if (stream.match(/^"(?:[^"\\]|\\.)*"(?=\s*:)/)) {
    return 'propertyName'
  }
  if (stream.match(/^"(?:[^"\\]|\\.)*"?/)) {
    return 'string'
  }
  if (stream.match(/^-?\d+(\.\d+)?([eE][+-]?\d+)?/)) {
    return 'number'
  }
  if (stream.match(/^(true|false)\b/)) {
    return 'bool'
  }
  if (stream.match(/^null\b/)) {
    return 'null'
  }
  if (stream.match(/^[{}[\],:]/)) {
    return 'punctuation'
  }
  stream.next()
  return null
}

/** The JSON grammar, for highlighting only. A string before `:` is a property name. */
export const jsonLanguage = StreamLanguage.define({ name: 'json', token })
