// The rows of a value that `ValueTable` draws, see docs/reference/actions-and-controls.md#text.

/** A number that keeps the digits it was written with, which a parsed number may not hold. */
export class JsonNumber {
  readonly source: string

  constructor(source: string) {
    this.source = source
  }
}

/** A word written in place of a value, see docs/reference/actions-and-controls.md#text. */
export type ValueWord = 'null' | 'empty' | 'hidden' | 'fields' | 'items'

/** One row of a {@link ValueTable}: a name, which is a path, and its value as text or as a word in its place. */
export interface ValueRow {
  /** The row's place among the rows, so two paths that read the same stay two rows. */
  id: number
  name: string
  text?: string
  word?: ValueWord
  /** The size of an object or a list, for the words `fields` and `items`. */
  count?: number
}

/** What the server writes in place of a secret value, see docs/reference/actions-and-controls.md#text. */
const secretPlaceholder = '[secret]'

/** A value the reader may not read, which a row shows as the word Hidden. {@link namedValues} puts it in place of the value. */
export const hiddenValue = Symbol('hidden')

/**
 * The API's `[{ name, value, hidden }]` as one value for {@link valueRows}, a member for each name. A value marked
 * `hidden` is {@link hiddenValue}, so it reads Hidden whatever the API sent in its place, see
 * docs/reference/process-inspection.md#values-and-masking. The parameter is typed by its shape to leave `inspection.ts` out of
 * this module's imports.
 */
export function namedValues(
  values: readonly { name: string; value: unknown; hidden: boolean }[],
): Record<string, unknown> {
  return Object.fromEntries(values.map(({ name, value, hidden }) => [name, hidden ? hiddenValue : value]))
}

/**
 * The rows of a value, depth first. A member of an object is named `parent.key` and one of a list `parent[0]`. An object
 * or a list that is not empty has a row of its own with its size, then its members. An empty one reads Empty.
 */
export function valueRows(value: unknown): ValueRow[] {
  const rows: ValueRow[] = []
  const add = (name: string, member: unknown) => {
    const id = rows.length
    if (Array.isArray(member)) {
      rows.push(member.length === 0 ? { id, name, word: 'empty' } : { id, name, word: 'items', count: member.length })
      member.forEach((item, index) => add(`${name}[${index}]`, item))
    } else if (member !== null && typeof member === 'object' && !(member instanceof JsonNumber)) {
      const entries = Object.entries(member)
      rows.push(
        entries.length === 0 ? { id, name, word: 'empty' } : { id, name, word: 'fields', count: entries.length },
      )
      entries.forEach(([key, item]) => add(name === '' ? key : `${name}.${key}`, item))
    } else if (member === null) {
      rows.push({ id, name, word: 'null' })
    } else if (member === secretPlaceholder || member === hiddenValue) {
      rows.push({ id, name, word: 'hidden' })
    } else {
      rows.push({ id, name, text: member instanceof JsonNumber ? member.source : String(member) })
    }
  }
  if (Array.isArray(value)) {
    value.forEach((item, index) => add(`[${index}]`, item))
  } else if (value !== null && typeof value === 'object' && !(value instanceof JsonNumber)) {
    Object.entries(value).forEach(([key, item]) => add(key, item))
  } else {
    add('', value)
  }
  return rows
}
