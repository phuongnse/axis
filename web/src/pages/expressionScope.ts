import type { StudioNames } from '../platform/studio'

/** A name an expression can use. `cursorInside` puts the cursor between the brackets of a call. */
export interface ScopeOption {
  label: string
  type: 'field' | 'parameter' | 'rule' | 'function'
  cursorInside: boolean
}

const functionNames = [
  'length',
  'contains',
  'startsWith',
  'endsWith',
  'concat',
  'lower',
  'upper',
  'trim',
  'abs',
  'round',
  'floor',
  'ceiling',
  'year',
  'month',
  'day',
  'addDays',
  'daysBetween',
  'coalesce',
  'if',
  'date',
  'dateTime',
  'now',
  'today',
  'toDate',
]

const aggregateNames = ['count', 'sum', 'avg', 'min', 'max', 'any', 'all']

const zeroArgumentFunctions = ['now', 'today', 'currentUser']

const grantFilter = /^\/grants\/\d+\/filter$/
const grantIndex = /^\/grants\/(\d+)\//

/**
 * The names an expression can use at a JSON Pointer of a resource of the given kind. With no names loaded, only
 * the functions are offered. A malformed draft yields fewer names, never an error.
 */
export function expressionScope(
  kind: string,
  draft: Record<string, unknown>,
  pointer: string,
  names: StudioNames | null,
): ScopeOption[] {
  const functions = functionOptions(kind, pointer)
  if (names === null) {
    return functions
  }
  return [
    ...parameterOptions(kind, draft, pointer),
    ...fieldOptions(kind, draft, pointer, names),
    ...names.rules.map((rule) => option(`${rule.name}()`, 'rule', true)),
    ...functions,
  ]
}

function parameterOptions(kind: string, draft: Record<string, unknown>, pointer: string): ScopeOption[] {
  const offered =
    kind === 'rule' ||
    (kind === 'dataSource' && pointer === '/filter') ||
    // A process without a subject has parameters in place of an entity.
    (kind === 'process' && !hasEntity(draft))
  return offered ? namesOf(draft.parameters).map((name) => option(name, 'parameter', false)) : []
}

function hasEntity(draft: Record<string, unknown>): boolean {
  return typeof draft.entity === 'string' && draft.entity !== ''
}

function fieldOptions(
  kind: string,
  draft: Record<string, unknown>,
  pointer: string,
  names: StudioNames,
): ScopeOption[] {
  if (kind === 'entity') {
    return namesOf(draft.fields).map((name) => option(name, 'field', false))
  }
  const entity = entityOf(kind, draft, pointer)
  const found = names.entities.find((item) => item.name.toLowerCase() === entity?.toLowerCase())
  if (!found) {
    return []
  }
  // A form condition reads only the fields the form lists.
  const listed = kind === 'form' ? listedFields(draft) : undefined
  // The record's `id` is in scope of a process and of a trigger, and it is a reference, so a path cannot start from it.
  const id = kind === 'process' || kind === 'trigger' ? [option('id', 'parameter', false)] : []
  return [
    ...id,
    ...found.fields
      .filter((field) => listed === undefined || listed.has(field.name.toLowerCase()))
      .map((field) => option(field.name, 'field', false)),
  ]
}

function listedFields(draft: Record<string, unknown>): Set<string> {
  const sections = Array.isArray(draft.sections) ? draft.sections : []
  const listed = sections.flatMap((section) => {
    const fields =
      section !== null && typeof section === 'object' ? (section as Record<string, unknown>).fields : undefined
    return Array.isArray(fields) ? fields.map((field) => stringProperty(field, 'field')) : []
  })
  return new Set(listed.filter((name): name is string => name !== undefined).map((name) => name.toLowerCase()))
}

function entityOf(kind: string, draft: Record<string, unknown>, pointer: string): string | undefined {
  if (kind === 'policy') {
    const match = grantIndex.exec(pointer)
    if (match === null || !Array.isArray(draft.grants)) {
      return undefined
    }
    return stringProperty(draft.grants[Number(match[1])], 'entity')
  }
  if (kind === 'dataSource' || kind === 'process' || kind === 'form' || kind === 'trigger') {
    return stringProperty(draft, 'entity')
  }
  return undefined
}

function functionOptions(kind: string, pointer: string): ScopeOption[] {
  const names = [...functionNames, ...aggregateNames]
  if (kind === 'policy' && grantFilter.test(pointer)) {
    names.push('currentUser')
  }
  return names.map((name) => option(`${name}()`, 'function', !zeroArgumentFunctions.includes(name)))
}

function option(label: string, type: ScopeOption['type'], cursorInside: boolean): ScopeOption {
  return { label, type, cursorInside }
}

function namesOf(list: unknown): string[] {
  if (!Array.isArray(list)) {
    return []
  }
  return list.map((item) => stringProperty(item, 'name')).filter((name): name is string => name !== undefined)
}

function stringProperty(value: unknown, key: string): string | undefined {
  if (value === null || typeof value !== 'object') {
    return undefined
  }
  const found = (value as Record<string, unknown>)[key]
  return typeof found === 'string' ? found : undefined
}
