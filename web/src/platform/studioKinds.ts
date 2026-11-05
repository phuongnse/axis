/** The sidebar groups of Studio, in sidebar order. Kinds that no group names are listed under `other`. */
export const kindGroupNames = ['data', 'pages', 'logic', 'access', 'integration', 'texts', 'tests', 'other'] as const

/** A sidebar group of Studio. */
export type KindGroup = (typeof kindGroupNames)[number]

/** One kind of resource: its sidebar group and its Axis icon from the closed list in navigationIcons. A null icon shows the page icon. */
export interface StudioKind {
  kind: string
  group: KindGroup
  icon: string | null
}

/**
 * Every kind that Studio names, in sidebar order. The sidebar, a kind's list and the New resource dialog take the
 * kind's group and its names from here. The plural name of a kind is the text `shell.studio.nav.kind.<kind>` and the
 * singular name is `shell.studio.nav.kindOne.<kind>`.
 */
export const studioKinds: readonly StudioKind[] = [
  { kind: 'entity', group: 'data', icon: 'folder' },
  { kind: 'seed', group: 'data', icon: 'inbox' },
  { kind: 'sequence', group: 'data', icon: 'list' },
  { kind: 'dataSource', group: 'data', icon: 'device' },
  { kind: 'migration', group: 'data', icon: 'tool' },
  { kind: 'site', group: 'pages', icon: 'home' },
  { kind: 'page', group: 'pages', icon: 'document' },
  { kind: 'form', group: 'pages', icon: 'form' },
  { kind: 'process', group: 'logic', icon: 'process' },
  { kind: 'rule', group: 'logic', icon: 'tasks' },
  { kind: 'event', group: 'logic', icon: 'calendar' },
  { kind: 'trigger', group: 'logic', icon: 'clock' },
  { kind: 'role', group: 'access', icon: 'people' },
  { kind: 'policy', group: 'access', icon: 'tasks' },
  { kind: 'identityProvider', group: 'access', icon: 'person' },
  { kind: 'connector', group: 'integration', icon: 'support' },
  { kind: 'endpoint', group: 'integration', icon: 'device' },
  { kind: 'webhook', group: 'integration', icon: 'mail' },
  { kind: 'text', group: 'texts', icon: 'document' },
  { kind: 'scenario', group: 'tests', icon: 'tasks' },
  { kind: 'contract', group: 'other', icon: null },
  { kind: 'layer', group: 'other', icon: null },
]

const byKind = new Map(studioKinds.map((entry) => [entry.kind, entry]))

/** The catalogue entry of a kind, or undefined when Studio does not name the kind. */
export function studioKind(kind: string): StudioKind | undefined {
  return byKind.get(kind)
}

/** The plural name of a kind in the UI locale, or the kind itself when the kind has no name. */
export function kindName(kind: string, t: (key: string) => string): string {
  return byKind.has(kind) ? t(`shell.studio.nav.kind.${kind}`) : kind
}

/** The singular name of a kind in the UI locale, such as "Entity", or the kind itself when the kind has no name. */
export function kindSingular(kind: string, t: (key: string) => string): string {
  return byKind.has(kind) ? t(`shell.studio.nav.kindOne.${kind}`) : kind
}

/** The sidebar group of a kind, or `other` when the kind has no name. */
export function kindGroup(kind: string): KindGroup {
  return byKind.get(kind)?.group ?? 'other'
}
