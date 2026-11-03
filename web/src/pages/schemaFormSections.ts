import { createContext } from 'react'

/**
 * Proposed: the sections a resource kind's form shows, in order, each with the root properties it holds. A property
 * that no section names goes to a last section with no heading. A kind without sections shows its properties in one.
 * Each section's heading and line are the texts `shell.studio.section.<kind>.<key>` and `….description`.
 */
export const schemaFormSections: Record<string, readonly { key: string; properties: readonly string[] }[]> = {
  application: [
    { key: 'general', properties: ['name', 'label', 'description', 'timeZone'] },
    { key: 'modules', properties: ['modules'] },
    { key: 'packages', properties: ['packages'] },
    { key: 'fileStorage', properties: ['fileStorage'] },
  ],
}

/** Proposed: the root properties that are the file's identity, which the author never edits, so the form hides them. */
export const identityProperties = new Set(['id', 'kind', 'formatVersion'])

/** Proposed: the kind whose texts name the form's fields, or null for a form that names them as written. */
export const SchemaFormKindContext = createContext<string | null>(null)

/**
 * Proposed: the text key of a field: `shell.studio.field.<kind>.<path>`, the path being the property names from the
 * root joined by dots, with array positions left out, such as `application.fileStorage.binding`. Its help text is the
 * same key ending in `.help`.
 */
export function fieldTextKey(kind: string, pointer: string): string {
  const path = pointer
    .split('/')
    .slice(1)
    .filter((segment) => !/^\d+$/.test(segment))
    .map((segment) => segment.replace(/~1/g, '/').replace(/~0/g, '~'))
    .join('.')
  return `shell.studio.field.${kind}.${path}`
}

/** Proposed: the installed modules and packages, which a form names and offers by their labels. */
export const StudioCatalogContext = createContext<import('../platform/studio').StudioCatalog | null>(null)

/**
 * Proposed: the fields whose control knows what they hold, by their path under the kind: a module or package to choose
 * from those installed, or the map of a module's role or connector slots to the application's roles or connectors.
 */
export const fieldControls: Record<
  string,
  'module' | 'package' | 'moduleRoles' | 'moduleConnectors' | 'relaxedPoints' | 'timeZone'
> = {
  'application.modules.id': 'module',
  'application.packages.id': 'package',
  'application.modules.roles': 'moduleRoles',
  'application.modules.connectors': 'moduleConnectors',
  'application.modules.relax': 'relaxedPoints',
  'application.timeZone': 'timeZone',
  'trigger.timeZone': 'timeZone',
}

/**
 * Proposed: the list columns whose values have a fixed, short form, such as a version range or a tag, which are as
 * wide as their content, as the table rules have it; every other column shares the width as free text.
 */
export const fitColumns = new Set([
  'application.modules.version',
  'application.modules.relax',
  'application.packages.version',
])

/**
 * Proposed: the lists that are sets, whose order means nothing to the author, which the form shows by name; every other
 * list keeps the order it is written in. The file keeps its order either way.
 */
export const setLists = new Set(['application.modules', 'application.packages'])
