/**
 * Lays JSON text out with two spaces, token by token, so every value stays exactly as it was written: a number keeps
 * its digits, as a decimal past what a double holds must. Text that is not JSON comes back as it is.
 */
export function formatJsonText(text: string): string {
  const source = text.trim()
  if (!(source.startsWith('{') || source.startsWith('['))) {
    return text
  }
  try {
    JSON.parse(source)
  } catch {
    return text
  }
  let out = ''
  let depth = 0
  let inString = false
  const newline = () => `\n${'  '.repeat(depth)}`
  for (let index = 0; index < source.length; index++) {
    const char = source[index]
    if (inString) {
      out += char
      if (char === '\\') {
        out += source[++index]
      } else if (char === '"') {
        inString = false
      }
      continue
    }
    if (char === '"') {
      inString = true
      out += char
    } else if (char === '{' || char === '[') {
      const close = char === '{' ? '}' : ']'
      const next = source.slice(index + 1).trimStart()[0]
      if (next === close) {
        out += char + close
        index = source.indexOf(close, index + 1)
      } else {
        depth++
        out += char + newline()
      }
    } else if (char === '}' || char === ']') {
      depth--
      out += newline() + char
    } else if (char === ',') {
      out += char + newline()
    } else if (char === ':') {
      out += ': '
    } else if (!/\s/.test(char)) {
      out += char
    }
  }
  return out
}
