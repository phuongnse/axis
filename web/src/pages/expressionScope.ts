import {
  aggregateFunctions,
  expressionFunctions,
  signatureOf,
  timeFunctions,
  type ExpressionFunction,
} from './expressionFunctions'
import type { StudioNames } from '../platform/studio'

/**
 * A name an expression can use. `label` is what completion inserts, and `cursorInside` puts the cursor between the
 * brackets of a call. Proposed: `detail` is its type, or a function's signature, `labelKey` the text key of a field's
 * label, and `fn` the function, so completion, the signature line and the panel tell an author what each one is.
 */
export interface ScopeOption {
  label: string
  type: 'field' | 'parameter' | 'rule' | 'function'
  cursorInside: boolean
  detail?: string
  labelKey?: string | null
  fn?: ExpressionFunction
  /** Proposed: a field's or a parameter's type, so completion offers the operators it takes. */
  valueType?: string
  /** Proposed: an enum field's values, which completion offers inside quotes after it. */
  values?: readonly string[]
}

/**
 * Proposed: the type an expression must give at a JSON Pointer, when the place says: a validation, a condition or a
 * filter gives a boolean, and a computed field or a default gives the field's type. Undefined where the place does not
 * fix it here.
 */
export function expressionExpectation(
  kind: string,
  draft: Record<string, unknown>,
  pointer: string,
): string | undefined {
  if (kind === 'entity') {
    if (/^\/validations\/\d+\/expression$/.test(pointer)) {
      return 'boolean'
    }
    const field = /^\/fields\/(\d+)\/(expression|default)$/.exec(pointer)
    if (field !== null && Array.isArray(draft.fields)) {
      return stringProperty(draft.fields[Number(field[1])], 'type')
    }
    return undefined
  }
  if (/(When|\/when|\/condition|\/filter|\/guard)$/.test(pointer)) {
    return 'boolean'
  }
  return undefined
}

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
      ...names.rules.map((rule) => ruleOption(rule)),
      ...functions,
    ]
  }
  return [
    ...parameterOptions(kind, draft, pointer),
    ...fieldOptions(kind, draft, pointer, names),
    ...names.rules.map((rule) => ruleOption(rule)),
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
  if (!offered || !Array.isArray(draft.parameters)) {
    return []
  }
  return draft.parameters.flatMap((parameter) => {
    const name = stringProperty(parameter, 'name')
    const type = stringProperty(parameter, 'type')
    return name === undefined ? [] : [{ ...option(name, 'parameter', false), detail: type, valueType: type }]
  })
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
    return (Array.isArray(draft.fields) ? draft.fields : []).flatMap((field) => {
      const name = stringProperty(field, 'name')
      const label = isRecord(field) && isRecord(field.label) ? stringProperty(field.label, 'textKey') : undefined
      return name === undefined
        ? []
        : [
            {
              ...option(name, 'field', false),
              detail: stringProperty(field, 'type'),
              valueType: stringProperty(field, 'type'),
              values:
                isRecord(field) && Array.isArray(field.values)
                  ? field.values.filter((v): v is string => typeof v === 'string')
                  : undefined,
              labelKey: label ?? null,
            },
          ]
    })
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
      .map((field) => ({
        ...option(field.name, 'field', false),
        detail: field.type,
        valueType: field.type,
        values: field.values ?? undefined,
        labelKey: field.labelKey ?? null,
      })),
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

/**
 * The functions an expression may call here, from the one list of functions. A default sees no child collections, so it
 * is offered no aggregate; a computed field may not read the time (AXC0086); only a policy grant's filter and a data
 * source's filter may call `currentUser()` (AXC0095).
 */
function functionOptions(kind: string, pointer: string): ScopeOption[] {
  const isDefault = kind === 'entity' && fieldDefault.test(pointer)
  const isComputed = kind === 'entity' && /^\/fields\/\d+\/expression$/.test(pointer)
  const userAllowed =
    (kind === 'policy' && grantFilter.test(pointer)) || (kind === 'dataSource' && pointer === '/filter')
  return expressionFunctions
    .filter((entry) => !(isDefault && aggregateFunctions.has(entry.name)))
    .filter((entry) => !(isComputed && timeFunctions.has(entry.name)))
    .filter((entry) => entry.name !== 'currentUser' || userAllowed)
    .map((entry) => ({
      ...option(`${entry.name}()`, 'function', entry.parameters.length > 0),
      detail: signatureOf(entry),
      fn: entry,
    }))
}

function ruleOption(rule: {
  name: string
  parameters: { name: string; type: string }[]
  resultType: string
  labelKey?: string | null
}): ScopeOption {
  return {
    ...option(`${rule.name}()`, 'rule', true),
    detail: `${rule.name}(${rule.parameters.map((parameter) => `${parameter.name} ${parameter.type}`).join(', ')}) → ${rule.resultType}`,
    labelKey: rule.labelKey ?? null,
  }
}

function option(label: string, type: ScopeOption['type'], cursorInside: boolean): ScopeOption {
  return { label, type, cursorInside }
}

function stringProperty(value: unknown, key: string): string | undefined {
  if (value === null || typeof value !== 'object') {
    return undefined
  }
  const found = (value as Record<string, unknown>)[key]
  return typeof found === 'string' ? found : undefined
}
