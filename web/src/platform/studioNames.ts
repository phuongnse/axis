import { createContext } from 'react'
import type { StudioNames } from './studio'
import type { ReferenceMark } from './studioSchemas'

/** The names the application defines, or `undefined` until they load. */
export const StudioNamesContext = createContext<StudioNames | undefined>(undefined)

/**
 * Proposed: one choice of a picker: the name the file keeps, and the text key of the label people know it by, which
 * `ReferencePicker` resolves. Every picker gets its choices from {@link referenceOptions}, so none builds a label itself.
 */
export interface ReferenceChoice {
  name: string
  labelKey?: string | null
}

function choice(name: string, labelKey?: string | null): ReferenceChoice {
  return { name, labelKey: labelKey ?? null }
}

/** What a picker takes: a choice, or a plain name, which is a choice with no label. */
export type ReferenceOption = string | ReferenceChoice

/** The choice of an option. */
export function asChoice(option: ReferenceOption): ReferenceChoice {
  return typeof option === 'string' ? { name: option, labelKey: null } : option
}

/** The names of options, for a caller that works on names alone. */
export function choiceNames(options: readonly ReferenceOption[]): string[] {
  return options.map((option) => asChoice(option).name)
}

/** The JSON Pointer that a part mark's `of` names, given the pointer of the property that holds the mark. */
export function referencePointer(pointer: string, of: string): string {
  const relative = /^(\d+)(\/.*)$/.exec(of)
  if (relative === null) {
    return of
  }
  const segments = pointer.split('/')
  return segments.slice(0, segments.length - Number(relative[1])).join('/') + relative[2]
}

/** The value at a JSON Pointer, or `undefined` when the path leaves the document. */
export function getAt(root: unknown, pointer: string): unknown {
  let current = root
  for (const segment of pointer.split('/').slice(1)) {
    if (typeof current !== 'object' || current === null) {
      return undefined
    }
    current = (current as Record<string, unknown>)[segment.replaceAll('~1', '/').replaceAll('~0', '~')]
  }
  return current
}

/**
 * The names a property may hold. A kind lists the application's names of that kind. A part lists names that belong
 * to the resource the draft names at `of`, which is found from `pointer`: the fields of an entity, the steps of a
 * process, the parameters or columns of a data source, the operations of a connector or the outcomes of the task
 * steps with that name. Names match without regard to case.
 */
export function referenceOptions(
  names: StudioNames,
  mark: ReferenceMark,
  draft: unknown,
  pointer: string,
): ReferenceChoice[] {
  if (typeof mark === 'string') {
    return kindNames(names, mark)
  }
  const owner = getAt(draft, referencePointer(pointer, mark.of))
  if (typeof owner !== 'string') {
    return []
  }
  const wanted = owner.toLowerCase()
  const named = (item: { name: string }) => item.name.toLowerCase() === wanted
  switch (mark.part) {
    case 'field':
      return names.entities.find(named)?.fields.map((field) => choice(field.name, field.labelKey)) ?? []
    case 'step':
      return names.processes.find(named)?.steps.map((step) => choice(step.name, step.labelKey)) ?? []
    case 'parameter':
      return names.dataSources.find(named)?.parameters.map((parameter) => choice(parameter.name)) ?? []
    case 'processParameter':
      return names.processes.find(named)?.parameters.map((parameter) => choice(parameter.name)) ?? []
    case 'column':
      return names.dataSources.find(named)?.columns.map((column) => choice(column.name)) ?? []
    case 'operation':
      return names.connectors.find(named)?.operations.map((operation) => choice(operation.name)) ?? []
    case 'outcome':
      return [
        ...new Set(names.processes.flatMap((process) => process.steps.filter(named).flatMap((step) => step.outcomes))),
      ].map((outcome) => choice(outcome))
  }
}

/**
 * The type of the parameter that a `parameter` mark (of a data source) or a `processParameter` mark (of a process)
 * names with `key`, or `undefined` when it is not found.
 */
export function parameterType(
  names: StudioNames,
  mark: ReferenceMark,
  draft: unknown,
  pointer: string,
  key: string,
): string | undefined {
  if (typeof mark === 'string' || (mark.part !== 'parameter' && mark.part !== 'processParameter')) {
    return undefined
  }
  const owner = getAt(draft, referencePointer(pointer, mark.of))
  if (typeof owner !== 'string') {
    return undefined
  }
  return (mark.part === 'parameter' ? names.dataSources : names.processes)
    .find((item) => item.name.toLowerCase() === owner.toLowerCase())
    ?.parameters.find((parameter) => parameter.name.toLowerCase() === key.toLowerCase())?.type
}

function kindNames(names: StudioNames, kind: string): ReferenceChoice[] {
  const named = (items: { name: string; labelKey?: string | null }[]) =>
    items.map((item) => choice(item.name, item.labelKey))
  switch (kind) {
    case 'entity':
      return named(names.entities)
    case 'dataSource':
      return named(names.dataSources)
    case 'process':
      return named(names.processes)
    case 'role':
      return named(names.roles)
    case 'rule':
      return named(names.rules)
    case 'connector':
      return named(names.connectors)
    case 'event':
      return named(names.events)
    case 'sequence':
      return named(names.sequences)
    case 'form':
      return named(names.forms)
    case 'page':
      return named(names.pages)
    case 'text':
      return names.texts.map((item) => choice(item.key))
    default:
      return []
  }
}
