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
  return view.name === 'kind' ? `kind:${view.kind}` : view.name
}
