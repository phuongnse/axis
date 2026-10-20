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
 * The names a property may hold. A kind lists the application's names of that kind. A part lists the fields of
 * the entity or the steps of the process that the draft names at `of`, which is found from `pointer`.
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
  if (mark.part === 'field') {
    const entity = names.entities.find((item) => item.name.toLowerCase() === wanted)
    return entity?.fields.map((field) => field.name) ?? []
  }
  const process = names.processes.find((item) => item.name.toLowerCase() === wanted)
  return process?.steps.map((step) => step.name) ?? []
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
