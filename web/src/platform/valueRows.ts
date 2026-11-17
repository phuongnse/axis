import { parseRecordJson } from './records'

/**
 * Proposed, see the review on #3650: one row of a `ValueTable`, a name and its value. A nested value is flattened into
 * a row of its own, named by its path, such as `lines[0].description`, as Kibana and Datadog show a JSON document as a
 * table, so a panel `sidePanel` wide never squeezes indented levels. An empty object or list keeps a row that says so.
 */
export interface ValueRow {
  key: string
  name: string
  value:
    | { kind: 'text' | 'number' | 'boolean' | 'expression'; text: string }
    | { kind: 'null' | 'empty' }
    | { kind: 'fields' | 'items'; count: number }
}

/**
 * Parses JSON text with every number kept as its source digits, or gives `undefined` for text that is not JSON. A
 * number reads back as text, as the record API's values do, see `parseRecordJson`.
 */
export function parseJsonText(text: string): unknown {
  try {
    return parseRecordJson<unknown>(text)
  } catch {
    return undefined
  }
}

/** The path of a field or an item under `path`: `name`, `path.name` or `path[0]`. */
function pathOf(path: string, name: string, item: boolean): string {
  return item ? `${path}[${name}]` : path === '' ? name : `${path}.${name}`
}

/** The rows of a JSON value: a row for each value in it, at any depth, named by its path. */
export function jsonRows(value: unknown, path = ''): ValueRow[] {
  const entries: [string, unknown][] = Array.isArray(value)
    ? value.map((item, index) => [String(index), item])
    : typeof value === 'object' && value !== null
      ? Object.entries(value)
      : []
  return entries.flatMap(([name, item]) => {
    const at = pathOf(path, name, Array.isArray(value))
    if (Array.isArray(item)) {
      return item.length === 0 ? [{ key: at, name: at, value: { kind: 'items', count: 0 } }] : jsonRows(item, at)
    }
    if (typeof item === 'object' && item !== null) {
      return Object.keys(item).length === 0
        ? [{ key: at, name: at, value: { kind: 'fields', count: 0 } }]
        : jsonRows(item, at)
    }
    return [{ key: at, name: at, value: leaf(item) }]
  })
}

function leaf(value: unknown): ValueRow['value'] {
  if (value === null || value === undefined) return { kind: 'null' }
  if (value === '') return { kind: 'empty' }
  if (typeof value === 'boolean') return { kind: 'boolean', text: String(value) }
  // A number kept as its source digits is text that reads as a number.
  const text = String(value)
  return { kind: /^-?\d+(\.\d+)?([eE][+-]?\d+)?$/.test(text) ? 'number' : 'text', text }
}

/** The rows of a flat map of names to text, such as headers or query parameters. */
export function textRows(values: Record<string, string>): ValueRow[] {
  return Object.entries(values).map(([name, text]) => ({
    key: name,
    name,
    value: text === '' ? { kind: 'empty' } : { kind: 'text', text },
  }))
}

/**
 * The rows of a step's mapping, each a name and the expression it maps to. A list's mapping, `{ "from", "fields" }`,
 * is a row that reads the list from `from`, then a row for each of its fields, named `list[].field`.
 */
export function mappingRows(mapping: unknown, path = ''): ValueRow[] {
  const entries = typeof mapping === 'object' && mapping !== null ? Object.entries(mapping) : []
  return entries.flatMap(([name, expression]): ValueRow[] => {
    const at = pathOf(path, name, false)
    if (typeof expression === 'object' && expression !== null && 'from' in expression) {
      const { from, fields } = expression as { from: unknown; fields?: unknown }
      return [
        { key: at, name: at, value: { kind: 'expression', text: String(from) } },
        ...mappingRows(fields, `${at}[]`),
      ]
    }
    return [{ key: at, name: at, value: { kind: 'expression', text: String(expression) } }]
  })
}
