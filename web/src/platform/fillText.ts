/** The `{name}` placeholders of a text. Split on it, each name is at an odd index and each piece of text at an even one. */
export const placeholderPattern = /(\{[A-Za-z0-9]+\})/

/**
 * A string text with a value at each `{name}` placeholder, the string twin of `FilledText`, so a sentence such as an
 * announcement keeps its word order in every language. A placeholder with no value stays as written, and a value is
 * never filled again. See docs/reference/shared-widgets.md#which-widget.
 */
export function fillText(text: string, values: Record<string, string | number>): string {
  return text
    .split(placeholderPattern)
    .map((piece, index) => {
      if (index % 2 === 0) {
        return piece
      }
      const name = piece.slice(1, -1)
      return Object.hasOwn(values, name) ? String(values[name]) : piece
    })
    .join('')
}
