import type { Diagnostic } from '@codemirror/lint'
import type { ErrorObject, ValidateFunction } from 'ajv'
import { escapePointerSegment } from '../platform/jsonPatch'
import { scanJson, type Range, type Span } from './jsonScan'

/** The keywords that have a text of their own. Every other keyword shows the `other` text. */
const keywords = new Set([
  'required',
  'additionalProperties',
  'type',
  'enum',
  'const',
  'minLength',
  'maxLength',
  'pattern',
  'minItems',
  'maxItems',
  'minimum',
  'maximum',
  'exclusiveMinimum',
  'exclusiveMaximum',
  'uniqueItems',
  'oneOf',
  'anyOf',
])

const textPrefix = 'shell.studio.jsonView.'

/** Whether the pointer is `parent` or lies inside it. */
function isWithin(pointer: string, parent: string): boolean {
  return pointer === parent || pointer.startsWith(`${parent}/`)
}

/** The schema a path belongs to: the root, or the `$defs` entry that a reference led into. */
function schemaRoot(schemaPath: string): string {
  const match = /^#\/\$defs\/[^/]+/.exec(schemaPath)
  return match ? match[0] : '#'
}

/**
 * The errors worth showing. A `oneOf` or `anyOf` that fails says it once, so the errors of its branches go, and so do
 * the `if` errors, which the error of the `then` or `else` that follows them repeats.
 */
function shown(errors: ErrorObject[]): ErrorObject[] {
  const choices = errors
    .map((error, index) => ({ error, index }))
    .filter(({ error }) => error.keyword === 'oneOf' || error.keyword === 'anyOf')
  return errors.filter((error, index) => {
    if (error.keyword === 'if') {
      return false
    }
    return !choices.some(
      (choice) =>
        choice.index !== index &&
        isWithin(error.instancePath, choice.error.instancePath) &&
        // The branches of a choice come before it. A branch that is a reference reports under its own schema.
        (error.schemaPath.startsWith(`${choice.error.schemaPath}/`) ||
          (index < choice.index && schemaRoot(error.schemaPath) !== schemaRoot(choice.error.schemaPath))),
    )
  })
}

/** The mark of a value: an object or an array by its key, or by its opening bracket when it has none, and a scalar whole. */
function markOf(text: string, span: Span): Range {
  const opening = text[span.value.from]
  if (opening === '{' || opening === '[') {
    return span.key ?? { from: span.value.from, to: span.value.from + 1 }
  }
  return span.value
}

/** The place in the text that an error points to. */
function placeOf(text: string, spans: Map<string, Span>, error: ErrorObject): Range {
  let pointer = error.instancePath
  if (error.keyword === 'additionalProperties') {
    pointer = `${pointer}/${escapePointerSegment(String((error.params as { additionalProperty: string }).additionalProperty))}`
  }
  const span = spans.get(pointer)
  if (span === undefined) {
    return { from: 0, to: Math.min(1, text.length) }
  }
  // An extra key is marked by its key, whatever it holds.
  if (error.keyword === 'additionalProperties') {
    return span.key ?? span.value
  }
  return markOf(text, span)
}

/** What fills the placeholders of an error's text. */
function fillings(error: ErrorObject): Record<string, string> {
  const params = error.params as Record<string, unknown>
  switch (error.keyword) {
    case 'required':
      return { property: String(params.missingProperty) }
    case 'additionalProperties':
      return { property: String(params.additionalProperty) }
    case 'type':
      return { type: Array.isArray(params.type) ? params.type.join(', ') : String(params.type) }
    case 'enum':
      return { values: (params.allowedValues as unknown[]).map((value) => JSON.stringify(value)).join(', ') }
    case 'const':
      return { value: JSON.stringify(params.allowedValue) }
    case 'pattern':
      return { pattern: String(params.pattern) }
    default:
      return { limit: String(params.limit) }
  }
}

function messageOf(error: ErrorObject, t: (key: string) => string): string {
  const key = keywords.has(error.keyword) ? error.keyword : 'other'
  let message = t(`${textPrefix}schema.${key}`)
  if (key !== 'other') {
    for (const [name, value] of Object.entries(fillings(error))) {
      message = message.replaceAll(`{${name}}`, value)
    }
  }
  return message
}

/**
 * The problems of a JSON text: the place where it stops being JSON, or, when it is JSON, each place where it breaks
 * the schema. A text without a validator has no schema problems.
 */
export function jsonProblems(
  text: string,
  validate: ValidateFunction | undefined,
  t: (key: string) => string,
): Diagnostic[] {
  const { error, spans } = scanJson(text)
  if (error !== null) {
    // At the end of the text the mark covers the last character, so it can be seen.
    const from = error >= text.length ? Math.max(text.length - 1, 0) : error
    return [{ from, to: Math.min(from + 1, text.length), severity: 'error', message: t(`${textPrefix}syntax`) }]
  }
  if (validate === undefined || validate(JSON.parse(text))) {
    return []
  }
  return shown(validate.errors ?? []).map((schemaError) => ({
    ...placeOf(text, spans, schemaError),
    severity: 'error',
    message: messageOf(schemaError, t),
  }))
}
