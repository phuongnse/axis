import type { CallOutput } from './inspection'
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
 * each row with its non-null child fields in declaration order. A references field goes out as its
 * whole array of ids in their order. A computed, sequence or creator field is never sent, because
 * the server sets it and rejects a body that does.
 */
export function buildRecordBody(
  fields: readonly FieldMetadata[],
  values: Record<string, FieldValue>,
  version?: number,
): string {
  const entries = fields
    .filter((field) => !field.computed && !field.sequence && !field.creator && Object.hasOwn(values, field.name))
    .map((field) => `${JSON.stringify(field.name)}:${fieldText(field, values[field.name])}`)
  const versionText = version === undefined ? '' : `"version":${String(version)},`
  return `{${versionText}"values":{${entries.join(',')}}}`
}

function fieldText(field: FieldMetadata, value: FieldValue): string {
  if (field.type === 'references') {
    return JSON.stringify(value ?? [])
  }
  if (Array.isArray(value)) {
    return `[${(value as RecordRow[]).map((row) => rowText(field.fields ?? [], row)).join(',')}]`
  }
  return valueText(field, value)
}

// A row is read like a create body, so a field left out is stored as null.
function rowText(fields: readonly FieldMetadata[], row: RecordRow): string {
  const entries = fields
    .filter((field) => !field.computed && !field.sequence && !field.creator && (row[field.name] ?? null) !== null)
    .map((field) => `${JSON.stringify(field.name)}:${valueText(field, row[field.name])}`)
  return `{${entries.join(',')}}`
}

function valueText(field: FieldMetadata, value: RecordValue): string {
  if (
    (field.type === 'integer' || field.type === 'decimal' || field.type === 'money') &&
    typeof value === 'string' &&
    isJsonNumber(value)
  ) {
    return value
  }
  return JSON.stringify(value)
}

/**
 * Writes a complete body for the task API: the outcome, the subject record's `version` and the
 * changed values, written as {@link buildRecordBody} writes them.
 */
export function buildCompleteBody(
  outcome: string,
  fields: readonly FieldMetadata[],
  values: Record<string, FieldValue>,
  version: number,
): string {
  return `{"outcome":${JSON.stringify(outcome)},${buildRecordBody(fields, values, version).slice(1)}`
}

/**
 * Writes the body of a form state call: the record's id or `null`, the draft values as
 * {@link buildRecordBody} writes them, and the touched field names. With no `recordId` the key is
 * left out, for an endpoint that knows its record and rejects the key, such as a task's.
 */
export function buildFormStateBody(
  fields: readonly FieldMetadata[],
  values: Record<string, FieldValue>,
  recordId: string | null | undefined,
  touched: readonly string[],
): string {
  const head = recordId === undefined ? '' : `"recordId":${JSON.stringify(recordId)},`
  return `{${head}${buildRecordBody(fields, values).slice(1, -1)},"touched":${JSON.stringify(touched)}}`
}

/**
 * Writes the body of a call resolve: the reason, the outcome and, for `succeeded`, the outputs in
 * declared order. Integer and decimal text that is a JSON number goes out as written, with every
 * digit, and other text as a JSON string. A boolean goes out as `true` or `false`. A value that is
 * `undefined` or empty is left out.
 */
export function buildResolveBody(
  reason: string,
  outcome: 'succeeded' | 'failed',
  outputs: readonly CallOutput[],
  values: Record<string, string | boolean | undefined>,
): string {
  const head = `"reason":${JSON.stringify(reason)},"outcome":${JSON.stringify(outcome)}`
  if (outcome === 'failed') {
    return `{${head}}`
  }
  const entries = outputs
    .filter((output) => values[output.name] !== undefined && values[output.name] !== '')
    .map((output) => {
      const value = values[output.name]!
      const written =
        typeof value === 'string' && (output.type === 'integer' || output.type === 'decimal') && isJsonNumber(value)
          ? value
          : JSON.stringify(value)
      return `${JSON.stringify(output.name)}:${written}`
    })
  return `{${head},"output":{${entries.join(',')}}}`
}
