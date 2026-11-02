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

const aggregateNames = ['count', 'sum', 'avg', 'min', 'max', 'any', 'all', 'first']

const zeroArgumentFunctions = ['now', 'today', 'currentUser']

const fieldDefault = /^\/fields\/\d+\/default$/
const grantFilter = /^\/grants\/\d+\/filter$/
const grantIndex = /^\/grants\/(\d+)\//
const waitEventWhen = /^\/steps\/(\d+)\/when$/
const stepInput = /^\/steps\/(\d+)\/input\/(.+)$/

/**
 * The names an expression can use at a JSON Pointer of a resource of the given kind. With no names loaded, only
 * the functions are offered. A field's `default` has no record in scope, so it offers rules and functions only. A malformed draft yields fewer names, never an error.
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
  const payload = waitEventPayload(kind, draft, pointer, names)
  if (payload !== undefined) {
    return [
      ...payload.map((name) => option(name, 'field', false)),
      ...names.rules.map((rule) => option(`${rule.name}()`, 'rule', true)),
      ...functions,
    ]
  }
  return [
    ...parameterOptions(kind, draft, pointer),
    ...fieldOptions(kind, draft, pointer, names),
    ...names.rules.map((rule) => option(`${rule.name}()`, 'rule', true)),
    ...functions,
  ]
}

/**
 * The payload fields of the event a wait for event step waits for, when the pointer is that step's `when`. The
 * `when` reads the payload, as an event trigger's does, so the subject's fields, `id` and parameters are not in
 * scope. An unknown event has no fields. Undefined when the pointer is not a wait for event step's `when`.
 */
function waitEventPayload(
  kind: string,
  draft: Record<string, unknown>,
  pointer: string,
  names: StudioNames,
): string[] | undefined {
  const match = kind === 'process' ? waitEventWhen.exec(pointer) : null
  if (match === null || !Array.isArray(draft.steps)) {
    return undefined
  }
  const step: unknown = draft.steps[Number(match[1])]
  if (stringProperty(step, 'type') !== 'waitEvent') {
    return undefined
  }
  const event = stringProperty(step, 'event')?.toLowerCase()
  return names.events.find((item) => item.name.toLowerCase() === event)?.fields.map((field) => field.name) ?? []
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
    // A default is evaluated with no record, so no field is in scope.
    if (fieldDefault.test(pointer)) {
      return []
    }
    return namesOf(draft.fields).map((name) => option(name, 'field', false))
  }
  const entity = (kind === 'process' ? itemEntity(draft, pointer, names) : undefined) ?? entityOf(kind, draft, pointer)
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

/**
 * The entity of the row an expression reads, when the pointer is inside a `list` input of a step's `input`. A `list`
 * maps from a child collection of the entity around it, so its nested expressions read a row of that collection's
 * entity. An object input keeps the entity around it. An unknown collection yields an empty name, which has no
 * fields. Undefined when the pointer crosses no `from`.
 */
export function itemEntity(draft: Record<string, unknown>, pointer: string, names: StudioNames): string | undefined {
  const match = stepInput.exec(pointer)
  if (match === null || !Array.isArray(draft.steps)) {
    return undefined
  }
  const segments = match[2].split('/').map((segment) => segment.replaceAll('~1', '/').replaceAll('~0', '~'))
  let entity = stringProperty(draft, 'entity') ?? ''
  let mapping: unknown = (draft.steps[Number(match[1])] as Record<string, unknown> | null | undefined)?.input
  let crossed = false
  for (let index = 0; index + 2 < segments.length && segments[index + 1] === 'fields'; index += 2) {
    const entry: unknown = isRecord(mapping) ? mapping[segments[index]] : undefined
    const from = stringProperty(entry, 'from')
    if (from !== undefined) {
      const collection = names.entities
        .find((item) => item.name.toLowerCase() === entity.toLowerCase())
        ?.fields.find((field) => field.type === 'child-collection' && field.name.toLowerCase() === from.toLowerCase())
      entity = collection?.target ?? ''
      crossed = true
    }
    mapping = isRecord(entry) ? entry.fields : undefined
  }
  return crossed ? entity : undefined
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
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
  // A default sees no child collections, so an aggregate can never be valid in it.
  const names =
    kind === 'entity' && fieldDefault.test(pointer) ? [...functionNames] : [...functionNames, ...aggregateNames]
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
