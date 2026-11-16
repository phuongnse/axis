import { createContext } from 'react'
import { textIn, type StudioNames } from './studio'
import type { ReferenceMark } from './studioSchemas'

/** The names the application defines, or `undefined` until they load. */
export const StudioNamesContext = createContext<StudioNames | undefined>(undefined)

/** A choice of a name: the name the file keeps and the key of its label, or no key when its format has no label. */
export type ReferenceOption = { name: string; labelKey?: string | null }

/**
 * The label for `labelKey` in the author's language, else in `en`, or `undefined` when there is no key or no text,
 * see [reference pickers](docs/reference/studio.md#reference-pickers). The texts of a module's key are merged in.
 */
export function labelText(names: StudioNames, labelKey: string | null | undefined, locale: string): string | undefined {
  if (labelKey === undefined || labelKey === null) {
    return undefined
  }
  return textIn(names.texts.find((text) => text.key === labelKey)?.texts, locale)
}

/** The texts of a key, one entry per locale, with an empty text for a locale the key does not have. */
export function textsOf(names: StudioNames, key: string): Record<string, string> {
  const known = names.texts.find((text) => text.key === key)
  return Object.fromEntries(names.locales.map((locale) => [locale, known?.texts[locale] ?? '']))
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
 * steps with that name. Names match without regard to case. A choice carries its `labelKey` where its format has a
 * label. Columns, parameters, operations, outcomes and text keys have none, so they come as names only.
 */
export function referenceOptions(
  names: StudioNames,
  mark: ReferenceMark,
  draft: unknown,
  pointer: string,
): ReferenceOption[] {
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
      return names.entities.find(named)?.fields.map(({ name, labelKey }) => ({ name, labelKey })) ?? []
    case 'step':
      return names.processes.find(named)?.steps.map(({ name, labelKey }) => ({ name, labelKey })) ?? []
    case 'parameter':
      return names.dataSources.find(named)?.parameters.map(({ name }) => ({ name })) ?? []
    case 'processParameter':
      return names.processes.find(named)?.parameters.map(({ name }) => ({ name })) ?? []
    case 'column':
      return names.dataSources.find(named)?.columns.map(({ name }) => ({ name })) ?? []
    case 'operation':
      return names.connectors.find(named)?.operations.map(({ name }) => ({ name })) ?? []
    case 'outcome':
      return [
        ...new Set(names.processes.flatMap((process) => process.steps.filter(named).flatMap((step) => step.outcomes))),
      ].map((name) => ({ name }))
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

function kindNames(names: StudioNames, kind: string): ReferenceOption[] {
  const of = (items: ReferenceOption[]) => items.map(({ name, labelKey }) => ({ name, labelKey }))
  switch (kind) {
    case 'entity':
      return of(names.entities)
    case 'dataSource':
      return of(names.dataSources)
    case 'process':
      return of(names.processes)
    case 'role':
      return of(names.roles)
    case 'rule':
      return of(names.rules)
    case 'connector':
      return of(names.connectors)
    case 'event':
      return of(names.events)
    case 'sequence':
      return of(names.sequences)
    case 'form':
      return of(names.forms)
    case 'page':
      return of(names.pages)
    case 'text':
      return names.texts.map((item) => ({ name: item.key }))
    default:
      return []
  }
}
