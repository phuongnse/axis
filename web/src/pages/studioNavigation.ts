import type { ShellNavigationEntry, ShellNavigationGroup } from '../platform/ShellNavigation'
import type { ModuleResourceGroup, ModuleResourceItem, StudioResourceItem } from '../platform/studio'
import { kindGroup, kindGroupNames, kindName, studioKind, studioKinds } from '../platform/studioKinds'

/**
 * The sidebar entries of an open application, in the order that docs/reference/studio-workspace.md#workspace gives.
 */
export function studioNavigation(
  application: string,
  resources: readonly StudioResourceItem[],
  moduleGroups: readonly ModuleResourceGroup[],
  t: (key: string) => string,
): (ShellNavigationEntry | ShellNavigationGroup)[] {
  const path = (extra: Record<string, string> = {}) => `/studio?${new URLSearchParams({ app: application, ...extra })}`
  const manifest = resources.find((item) => item.kind === 'application')
  const kinds = new Set([
    ...resources.map((item) => item.kind),
    ...moduleGroups.filter((group) => group.items.length > 0).map((group) => group.kind),
  ])
  const kindEntry = (kind: string): ShellNavigationEntry => ({
    key: `kind:${kind}`,
    path: path({ kind }),
    icon: studioKind(kind)?.icon ?? null,
    label: kindName(kind, t),
  })
  const policyMatrix: ShellNavigationEntry = {
    key: 'policies',
    path: path({ view: 'policies' }),
    icon: 'list',
    label: t('shell.studio.view.policies'),
  }

  const entries: (ShellNavigationEntry | ShellNavigationGroup)[] = [
    { key: 'overview', path: path(), icon: 'dashboard', label: t('shell.studio.nav.overview') },
    { key: 'releases', path: path({ view: 'releases' }), icon: 'tag', label: t('shell.studio.releases') },
    { key: 'history', path: path({ view: 'history' }), icon: 'clock', label: t('shell.studio.history') },
  ]
  if (manifest !== undefined) {
    entries.push({
      key: 'settings',
      path: path({ resource: manifest.id }),
      icon: 'settings',
      label: t('shell.studio.nav.settings'),
    })
  }

  for (const name of kindGroupNames.filter((group) => group !== 'other')) {
    const groupEntries = studioKinds
      .filter((entry) => entry.group === name && kinds.has(entry.kind))
      .map((entry) => kindEntry(entry.kind))
    if (name === 'access') {
      groupEntries.push(policyMatrix)
    }
    if (groupEntries.length > 0) {
      entries.push({ key: `group:${name}`, label: t(`shell.studio.nav.group.${name}`), entries: groupEntries })
    }
  }

  const other = [...kinds].filter((kind) => kind !== 'application' && kindGroup(kind) === 'other').sort()
  if (other.length > 0) {
    entries.push({ key: 'group:other', label: t('shell.studio.nav.group.other'), entries: other.map(kindEntry) })
  }
  return entries
}

/** The key of the sidebar entry the address shows: an open resource's kind, the manifest's Settings, or a view. */
export function studioActiveEntry(
  params: URLSearchParams,
  resources: readonly StudioResourceItem[],
  moduleItems: readonly ModuleResourceItem[],
): string {
  const resource = params.get('resource')?.toLowerCase()
  if (resource) {
    const item = resources.find((candidate) => candidate.id === resource)
    if (item !== undefined) {
      return item.kind === 'application' ? 'settings' : `kind:${item.kind}`
    }
    const moduleItem = moduleItems.find((candidate) => candidate.id.toLowerCase() === resource)
    if (moduleItem !== undefined) {
      return `kind:${moduleItem.kind}`
    }
  }
  const kind = params.get('kind')
  if (kind) {
    return `kind:${kind}`
  }
  const view = params.get('view')
  if (view === 'releases' || view === 'history' || view === 'policies') {
    return view
  }
  return 'overview'
}
