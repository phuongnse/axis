import { escapePointerSegment } from '../platform/jsonPatch'

/** A range of the text, from the first character to the offset after the last. */
export interface Range {
  from: number
  to: number
}

/** Where a JSON value lies in the text. `key` is the property name that holds it, and is absent for an item and for the root. */
export interface Span {
  key?: Range
  value: Range
}

export interface Scan {
  /** The offset where the text stops being JSON, or `null` when the whole text is. */
  error: number | null
  /** The span of each value by its JSON Pointer. A key that repeats counts as its last one, as in `JSON.parse`. */
  spans: Map<string, Span>
}

const digit = /[0-9]/
const hex = /[0-9a-fA-F]/
const escapes = '"\\/bfnrtu'
const literals = ['true', 'false', 'null']

/** Thrown with the offset of the first character that JSON does not allow. */
class Stop {
  offset: number

  constructor(offset: number) {
    this.offset = offset
  }
}

/**
 * Finds where a text stops being JSON, and where each value of the text lies. It accepts what `JSON.parse` accepts and
 * nothing else, so the offset does not depend on the wording of a browser's parse error. After an error, `spans` holds
 * the values before it.
 */
export function scanJson(text: string): Scan {
  const spans = new Map<string, Span>()
  let position = 0

  const fail = (): never => {
    throw new Stop(position)
  }

  function skipWhitespace() {
    while (position < text.length && ' \t\n\r'.includes(text[position])) {
      position++
    }
  }

  function digits() {
    if (!digit.test(text[position] ?? '')) {
      fail()
    }
    while (digit.test(text[position] ?? '')) {
      position++
    }
  }

  function number() {
    if (text[position] === '-') {
      position++
    }
    if (text[position] === '0') {
      position++
    } else {
      digits()
    }
    if (text[position] === '.') {
      position++
      digits()
    }
    if (text[position] === 'e' || text[position] === 'E') {
      position++
      if (text[position] === '+' || text[position] === '-') {
        position++
      }
      digits()
    }
  }

  function string(): Range {
    const from = position
    position++
    for (;;) {
      const character = text[position]
      if (character === undefined || character < ' ') {
        fail()
      } else if (character === '"') {
        position++
        return { from, to: position }
      } else if (character === '\\') {
        position++
        const escape = text[position]
        if (escape === undefined || !escapes.includes(escape)) {
          fail()
        }
        position++
        if (escape === 'u') {
          for (let index = 0; index < 4; index++) {
            if (!hex.test(text[position] ?? '')) {
              fail()
            }
            position++
          }
        }
      } else {
        position++
      }
    }
  }

  function literal() {
    const word = literals.find((candidate) => text.startsWith(candidate, position))
    if (word === undefined) {
      fail()
    } else {
      position += word.length
    }
  }

  function object(pointer: string) {
    position++
    skipWhitespace()
    if (text[position] === '}') {
      position++
      return
    }
    for (;;) {
      if (text[position] !== '"') {
        fail()
      }
      const key = string()
      const name = JSON.parse(text.slice(key.from, key.to)) as string
      const child = `${pointer}/${escapePointerSegment(name)}`
      skipWhitespace()
      if (text[position] !== ':') {
        fail()
      }
      position++
      skipWhitespace()
      value(child, key)
      skipWhitespace()
      if (text[position] === ',') {
        position++
        skipWhitespace()
      } else if (text[position] === '}') {
        position++
        return
      } else {
        fail()
      }
    }
  }

  function array(pointer: string) {
    position++
    skipWhitespace()
    if (text[position] === ']') {
      position++
      return
    }
    for (let index = 0; ; index++) {
      value(`${pointer}/${index}`)
      skipWhitespace()
      if (text[position] === ',') {
        position++
        skipWhitespace()
      } else if (text[position] === ']') {
        position++
        return
      } else {
        fail()
      }
    }
  }

  function value(pointer: string, key?: Range) {
    const from = position
    const character = text[position]
    // A repeated key replaces the earlier value, and the places inside it.
    if (spans.delete(pointer)) {
      for (const other of [...spans.keys()]) {
        if (other.startsWith(`${pointer}/`)) {
          spans.delete(other)
        }
      }
    }
    if (character === '{') {
      object(pointer)
    } else if (character === '[') {
      array(pointer)
    } else if (character === '"') {
      string()
    } else if (character === '-' || digit.test(character ?? '')) {
      number()
    } else {
      literal()
    }
    spans.set(pointer, { key, value: { from, to: position } })
  }

  try {
    skipWhitespace()
    value('')
    skipWhitespace()
    if (position < text.length) {
      fail()
    }
    return { error: null, spans }
  } catch (caught) {
    if (caught instanceof Stop) {
      return { error: caught.offset, spans }
    }
    throw caught
  }
}
