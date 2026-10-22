import type { FieldValue } from './records'
import type { NavigateAction } from './site'

/** What a navigate expression reads: the id of a record and its field values, or a data source row's columns. */
export interface NavigateScope {
  id: string | null
  values: Record<string, FieldValue>
}

const namePattern = /^[A-Za-z][A-Za-z0-9]*$/
export const numberPattern = /^-?\d+(\.\d+)?$/
const textPattern = /^'((?:[^']|'')*)'$/

/**
 * Works out a navigate expression, or returns `undefined` when the SPA cannot. It supports a bare
 * name (`id`, a column or a field) and a literal: text, a number, `true`, `false` or `null`. A
 * name is matched ignoring case, and `id` is the record id before any field of that name.
 */
export function navigateValue(expression: string, scope: NavigateScope | null): FieldValue | undefined {
  const text = expression.trim()
  const lower = text.toLowerCase()
  if (lower === 'true') {
    return true
  }
  if (lower === 'false') {
    return false
  }
  if (lower === 'null') {
    return null
  }
  if (numberPattern.test(text)) {
    return text
  }
  const literal = textPattern.exec(text)
  if (literal) {
    return literal[1].replaceAll("''", "'")
  }
  if (!namePattern.test(text) || !scope) {
    return undefined
  }
  if (lower === 'id') {
    return scope.id ?? undefined
  }
  const key = Object.keys(scope.values).find((name) => name.toLowerCase() === lower)
  const value = key === undefined ? undefined : scope.values[key]
  // A child collection is not a value a URL can carry.
  return Array.isArray(value) ? undefined : value
}

/**
 * The address a navigate action opens, or `null` when it cannot be built: an expression is not
 * supported, or `record` is null. `record` opens `/{site}/{page}/{id}`, a form page without it opens
 * `/{site}/{page}/new`, and each parameter whose value is not null goes in the query. A toolbar has
 * no scope, so only its literals resolve.
 */
export function navigatePath(sitePath: string, action: NavigateAction, scope: NavigateScope | null): string | null {
  let path = `/${sitePath}/${action.page.toLowerCase()}`
  if (action.record !== null) {
    const id = navigateValue(action.record, scope)
    if (typeof id !== 'string' || id === '') {
      return null
    }
    path += `/${encodeURIComponent(id)}`
  } else if (action.opensForm) {
    path += '/new'
  }
  const query = new URLSearchParams()
  for (const [name, expression] of Object.entries(action.parameters)) {
    const value = navigateValue(expression, scope)
    if (value === undefined) {
      return null
    }
    if (value !== null) {
      query.append(name, String(value))
    }
  }
  const search = query.toString()
  return search ? `${path}?${search}` : path
}
