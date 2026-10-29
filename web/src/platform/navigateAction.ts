import { fetchRecord, type FieldValue, type RecordItem } from './records'
import type { NavigateAction } from './site'
import { siteAddress } from './siteLinks'

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
 * Builds the address from the action's rules, with `valueOf` working out each expression. It returns
 * `null` when an expression is not supported, or `record` is null or not a string.
 */
function addressOf(
  sitePath: string,
  action: NavigateAction,
  valueOf: (expression: string) => FieldValue | undefined,
): string | null {
  let path = siteAddress(sitePath, action.page.toLowerCase())
  if (action.record !== null) {
    const id = valueOf(action.record)
    if (typeof id !== 'string' || id === '') {
      return null
    }
    path += `/${encodeURIComponent(id)}`
  } else if (action.opensForm) {
    path += '/new'
  }
  const query = new URLSearchParams()
  for (const [name, expression] of Object.entries(action.parameters)) {
    const value = valueOf(expression)
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

/**
 * The address a navigate action opens, or `null` when it cannot be built: an expression is not
 * supported, or `record` is null. `record` opens `/{site}/{page}/{id}`, a form page without it opens
 * `/{site}/{page}/new`, and each parameter whose value is not null goes in the query. A toolbar has
 * no scope, so only its literals resolve. A reference path needs the record API, so it does not
 * resolve here: see {@link navigateTarget}.
 */
export function navigatePath(sitePath: string, action: NavigateAction, scope: NavigateScope | null): string | null {
  return addressOf(sitePath, action, (expression) => navigateValue(expression, scope))
}

// A reference path: a reference name, then one to three more names (`customer.parent.name`).
const pathPattern = /^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*){1,3}$/

/** Opens a navigate action on click. It resolves to the address, or to `null` when no page may open. */
export type NavigateOpen = () => Promise<string | null>

/**
 * Prepares a navigate button. It returns `null` to hide the button: an expression is not supported,
 * the first name of a path is not in the scope or is `id`, or `paths` lacks an entry for a hop.
 * Otherwise it returns the function to call on click. That function reads each record along every
 * reference path through the record API, one hop at a time, using `action.paths`. A `null` reference
 * gives `null`. A refused or failed read, or a field missing from a record, resolves to `null`, so
 * the caller opens no page and shows one message that does not say which hop failed.
 */
export function navigateTarget(
  sitePath: string,
  action: NavigateAction,
  scope: NavigateScope | null,
): NavigateOpen | null {
  const expressions = [action.record, ...Object.values(action.parameters)].filter(
    (expression): expression is string => expression !== null,
  )
  const pathExpressions = expressions.filter((expression) => pathPattern.test(expression.trim()))
  if (pathExpressions.length === 0) {
    const path = navigatePath(sitePath, action, scope)
    return path === null ? null : () => Promise.resolve(path)
  }

  const recordsPaths = new Map(Object.entries(action.paths).map(([name, path]) => [name.toLowerCase(), path]))
  for (const expression of pathExpressions) {
    const names = expression.trim().split('.')
    const root = navigateValue(names[0], scope)
    if (!scope || names[0].toLowerCase() === 'id' || (root !== null && typeof root !== 'string')) {
      return null
    }
    for (let hop = 1; hop < names.length; hop++) {
      if (!recordsPaths.has(names.slice(0, hop).join('.').toLowerCase())) {
        return null
      }
    }
  }
  // The other expressions and the shape of the address are checked now. A path stands in as a value.
  const isPath = (expression: string) => pathExpressions.includes(expression)
  if (
    addressOf(sitePath, action, (expression) => (isPath(expression) ? 'x' : navigateValue(expression, scope))) === null
  ) {
    return null
  }

  return async () => {
    const records = new Map<string, Promise<RecordItem | null>>()
    const read = (recordsPath: string, id: string) => {
      const key = `${recordsPath}/${id}`
      let record = records.get(key)
      if (!record) {
        record = fetchRecord(recordsPath, id)
        records.set(key, record)
      }
      return record
    }
    const resolve = async (expression: string): Promise<FieldValue> => {
      const names = expression.trim().split('.')
      let value = navigateValue(names[0], scope)
      for (let hop = 1; hop < names.length; hop++) {
        if (value === null) {
          return null
        }
        if (typeof value !== 'string') {
          throw new Error(`The reference '${names.slice(0, hop).join('.')}' is not a record id.`)
        }
        const recordsPath = recordsPaths.get(names.slice(0, hop).join('.').toLowerCase())
        const record = recordsPath === undefined ? null : await read(recordsPath, value)
        const key = Object.keys(record?.values ?? {}).find((name) => name.toLowerCase() === names[hop].toLowerCase())
        const next = record && key !== undefined ? record.values[key] : undefined
        if (next === undefined || Array.isArray(next)) {
          throw new Error(`The field '${names[hop]}' of '${names.slice(0, hop).join('.')}' cannot be read.`)
        }
        value = next
      }
      return value ?? null
    }
    try {
      const values = new Map<string, FieldValue>()
      for (const expression of pathExpressions) {
        values.set(expression, await resolve(expression))
      }
      return addressOf(sitePath, action, (expression) =>
        values.has(expression) ? values.get(expression) : navigateValue(expression, scope),
      )
    } catch {
      return null
    }
  }
}
