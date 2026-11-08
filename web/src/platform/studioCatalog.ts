import { createContext } from 'react'
import type { StudioCatalog, StudioCatalogSlot } from './studio'

/** What the installed server offers the resource form, or `undefined` until it loads or when it fails to load. */
export const StudioCatalogContext = createContext<StudioCatalog | undefined>(undefined)

/** The control a field draws from the catalog, see docs/reference/studio-resource-form.md#controls-that-know-what-they-hold. */
export type CatalogControl =
  'module' | 'package' | 'moduleVersion' | 'packageVersion' | 'roles' | 'connectors' | 'timeZone'

/**
 * The fields that take the catalog's control, by kind and field path, as `fieldTexts` writes a path: the property names
 * from the root joined by dots, with list indices left out. Only these few fixed fields choose from the catalog, so the
 * schema carries no mark for it.
 */
export const catalogFieldsByKind: Record<string, Record<string, CatalogControl>> = {
  application: {
    timeZone: 'timeZone',
    'modules.id': 'module',
    'modules.version': 'moduleVersion',
    'modules.roles': 'roles',
    'modules.connectors': 'connectors',
    'packages.id': 'package',
    'packages.version': 'packageVersion',
  },
  trigger: { timeZone: 'timeZone' },
}

/** The control a field takes from the catalog, if any. */
export function catalogControlOf(kind: unknown, path: string): CatalogControl | undefined {
  return typeof kind === 'string' &&
    Object.hasOwn(catalogFieldsByKind, kind) &&
    Object.hasOwn(catalogFieldsByKind[kind], path)
    ? catalogFieldsByKind[kind][path]
    : undefined
}

/** The operator that a range has: `^` takes later versions of the same major, `~` of the same minor, none the version only. */
export type RangeOperator = '^' | '~' | ''

/** A version range as the compiler reads it, see `VersionRange` in `Axis.Configuration`. */
export interface ParsedRange {
  operator: RangeOperator
  version: string
}

const rangePattern = /^(?<operator>[\^~])?(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))$/

/** The range in `text`, or `undefined` when it is in no valid form. */
export function parseVersionRange(text: string): ParsedRange | undefined {
  const match = rangePattern.exec(text)
  return match?.groups === undefined
    ? undefined
    : { operator: (match.groups.operator ?? '') as RangeOperator, version: match.groups.version }
}

/** A range in words, as the Modules and Packages tables show it. A range in no valid form shows as written. */
export function versionRangeWords(text: string, t: (key: string) => string): string {
  const range = parseVersionRange(text)
  if (range === undefined) {
    return text
  }
  const [major, minor] = range.version.split('.')
  const key = range.operator === '^' ? 'compatible' : range.operator === '~' ? 'fixes' : 'exact'
  return t(`shell.studio.versionRange.words.${key}`)
    .replace('{version}', range.version)
    .replace('{major}', major)
    .replace('{minor}', minor)
}

/** A time zone as the select shows it, such as "(UTC+07:00) Asia/Ho Chi Minh". `offset` is minutes from UTC. */
export function timeZoneText(zone: { id: string; offset: number }): string {
  const minutes = Math.abs(zone.offset)
  const clock = `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
  return `(UTC${zone.offset < 0 ? '-' : '+'}${clock}) ${zone.id.replaceAll('_', ' ')}`
}

/** A slot's title in the locale, else in the first locale that has one, else its name. */
export function slotTitle(slot: StudioCatalogSlot, locale: string): string {
  return slot.titles[locale] || Object.values(slot.titles).find((title) => title !== '') || slot.name
}
