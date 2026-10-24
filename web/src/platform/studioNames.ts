import { createContext } from 'react'
import type { StudioNames } from './studio'
import type { ReferenceMark } from './studioSchemas'

/** The names the application defines, or `undefined` until they load. */
export const StudioNamesContext = createContext<StudioNames | undefined>(undefined)

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
export function referenceOptions(names: StudioNames, mark: ReferenceMark, draft: unknown, pointer: string): string[] {
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
      return names.entities.find(named)?.fields.map((field) => field.name) ?? []
    case 'step':
      return names.processes.find(named)?.steps.map((step) => step.name) ?? []
    case 'parameter':
      return names.dataSources.find(named)?.parameters.map((parameter) => parameter.name) ?? []
    case 'column':
      return names.dataSources.find(named)?.columns.map((column) => column.name) ?? []
    case 'operation':
      return names.connectors.find(named)?.operations.map((operation) => operation.name) ?? []
    case 'outcome':
      return [
        ...new Set(names.processes.flatMap((process) => process.steps.filter(named).flatMap((step) => step.outcomes))),
      ]
  }
}

/** The type of the data source parameter that a `parameter` mark names with `key`, or `undefined` when it is not found. */
export function parameterType(
  names: StudioNames,
  mark: ReferenceMark,
  draft: unknown,
  pointer: string,
  key: string,
): string | undefined {
  if (typeof mark === 'string' || mark.part !== 'parameter') {
    return undefined
  }
  const owner = getAt(draft, referencePointer(pointer, mark.of))
  if (typeof owner !== 'string') {
    return undefined
  }
  return names.dataSources
    .find((item) => item.name.toLowerCase() === owner.toLowerCase())
    ?.parameters.find((parameter) => parameter.name.toLowerCase() === key.toLowerCase())?.type
}

function kindNames(names: StudioNames, kind: string): string[] {
  switch (kind) {
    case 'entity':
      return names.entities.map((item) => item.name)
    case 'dataSource':
      return names.dataSources.map((item) => item.name)
    case 'process':
      return names.processes.map((item) => item.name)
    case 'role':
      return names.roles.map((item) => item.name)
    case 'rule':
      return names.rules.map((item) => item.name)
    case 'connector':
      return names.connectors.map((item) => item.name)
    case 'event':
      return names.events.map((item) => item.name)
    case 'sequence':
      return names.sequences.map((item) => item.name)
    case 'form':
      return names.forms.map((item) => item.name)
    case 'page':
      return names.pages.map((item) => item.name)
    case 'text':
      return names.texts.map((item) => item.key)
    default:
      return []
  }
}
