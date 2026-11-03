/**
 * Proposed: the parts of an application's workspace that the sidebar opens. `kind` lists the resources of one kind;
 * a resource open in the editor belongs to its kind's entry.
 */
export type StudioView =
  | { name: 'overview' }
  | { name: 'settings' }
  | { name: 'kind'; kind: string }
  | { name: 'policies' }
  | { name: 'releases' }
  // Proposed: one release's own page, under the Releases entry.
  | { name: 'release'; number: number }

/**
 * Proposed: the resource kinds by what an author does with them, each with its Axis icon. A kind outside these groups
 * shows under Other.
 */
export const studioKindGroups: readonly { key: string; kinds: readonly { kind: string; icon: string }[] }[] = [
  {
    key: 'data',
    kinds: [
      { kind: 'entity', icon: 'folder' },
      // Proposed: a module's contract, a lifecycle on its entity, sits beside the entities.
      { kind: 'contract', icon: 'process' },
      { kind: 'seed', icon: 'document' },
      { kind: 'sequence', icon: 'tag' },
      { kind: 'dataSource', icon: 'chart' },
      { kind: 'migration', icon: 'tool' },
    ],
  },
  {
    key: 'pages',
    kinds: [
      { kind: 'site', icon: 'home' },
      { kind: 'page', icon: 'document' },
      { kind: 'form', icon: 'form' },
    ],
  },
  {
    key: 'logic',
    kinds: [
      { kind: 'process', icon: 'process' },
      { kind: 'rule', icon: 'tool' },
      { kind: 'event', icon: 'tag' },
      { kind: 'trigger', icon: 'clock' },
    ],
  },
  {
    key: 'access',
    kinds: [
      { kind: 'role', icon: 'person' },
      { kind: 'policy', icon: 'people' },
      { kind: 'identityProvider', icon: 'person' },
    ],
  },
  {
    key: 'integration',
    kinds: [
      { kind: 'connector', icon: 'support' },
      { kind: 'endpoint', icon: 'mail' },
      { kind: 'webhook', icon: 'support' },
    ],
  },
  { key: 'texts', kinds: [{ kind: 'text', icon: 'document' }] },
  { key: 'tests', kinds: [{ kind: 'scenario', icon: 'tasks' }] },
]

export const groupedKinds = new Set(studioKindGroups.flatMap((group) => group.kinds.map((entry) => entry.kind)))

/** The key of the sidebar entry a view belongs to. */
export function studioViewKey(view: StudioView): string {
  if (view.name === 'release') {
    return 'releases'
  }
  return view.name === 'kind' ? `kind:${view.kind}` : view.name
}

/** A kind's name for one resource, such as Entity, or the kind as written when no text names it. */
export function kindName(kind: string, t: (key: string) => string): string {
  const key = `shell.studio.kindOne.${kind}`
  const name = t(key)
  return name === key ? kind : name
}

/**
 * Proposed: where a kind sorts among others in a list that mixes kinds, such as the changes or Uses and Used by: the
 * application first, then the sidebar's order of its groups and kinds, then a kind outside them, then modules and
 * packages last.
 */
export function kindOrder(kind: string | null | undefined): number {
  if (kind === 'application') {
    return -1
  }
  const index = studioKindGroups.flatMap((group) => group.kinds.map((entry) => entry.kind)).indexOf(kind ?? '')
  if (index >= 0) {
    return index
  }
  return kind === 'module' ? 1000 : kind === 'package' ? 1001 : 999
}

/** Proposed: the order of names as a person reads them, in the page's language, so Đ sorts after D in Vietnamese. */
export function nameCollator(): Intl.Collator {
  return new Intl.Collator(document.documentElement.lang || 'en', { sensitivity: 'base', numeric: true })
}
