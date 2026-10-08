import type { FieldValue, RecordRow, RecordValue } from './records'
import type { FieldMetadata } from './site'

// The JSON number grammar of RFC 8259.
const jsonNumber = /^-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?$/

/** Whether the text is a JSON number as written, such as `12.50` or `-1e3`. */
export function isJsonNumber(text: string): boolean {
  return jsonNumber.test(text)
}

/**
 * Writes a create or update body for the record API. The text is written by hand so an integer or
 * decimal goes out as the typed text, with every digit. Text that is not a JSON number goes out as
 * a JSON string, and the server rejects it on its field. Values are written in field declaration
 * order, and `version` only when it is given. A child collection goes out as its whole row list,
 * each row with its non-null child fields in declaration order.
 */
export function buildRecordBody(
  fields: readonly FieldMetadata[],
  values: Record<string, FieldValue>,
  version?: number,
): string {
  const entries = fields
    .filter((field) => Object.hasOwn(values, field.name))
    .map((field) => `${JSON.stringify(field.name)}:${fieldText(field, values[field.name])}`)
  const versionText = version === undefined ? '' : `"version":${String(version)},`
  return `{${versionText}"values":{${entries.join(',')}}}`
}

function fieldText(field: FieldMetadata, value: FieldValue): string {
  if (Array.isArray(value)) {
    return `[${value.map((row) => rowText(field.fields ?? [], row)).join(',')}]`
  }
  return valueText(field, value)
}

// A row is read like a create body, so a field left out is stored as null.
function rowText(fields: readonly FieldMetadata[], row: RecordRow): string {
  const entries = fields
    .filter((field) => (row[field.name] ?? null) !== null)
    .map((field) => `${JSON.stringify(field.name)}:${valueText(field, row[field.name])}`)
  return `{${entries.join(',')}}`
}

function valueText(field: FieldMetadata, value: RecordValue): string {
  if ((field.type === 'integer' || field.type === 'decimal') && typeof value === 'string' && isJsonNumber(value)) {
    return value
  }
  return JSON.stringify(value)
}
