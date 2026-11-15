/**
 * Proposed: a text with a value at each `{name}` placeholder, beside FilledText, for a text that is a string, such as an announcement or
 * an accessible name, so a sentence keeps its word order in every language. A placeholder with no value stays as
 * written. See docs/reference/ui-writing.md#placeholders-and-numbers.
 */
export function fillText(text: string, values: Record<string, string | number>): string {
  return text.replace(/\{([A-Za-z0-9]+)\}/g, (piece, name: string) =>
    Object.hasOwn(values, name) ? String(values[name]) : piece,
  )
}
