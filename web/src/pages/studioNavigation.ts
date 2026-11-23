import type { ShellNavigationEntry, ShellNavigationNode } from '../platform/ShellNavigation'
import type { ModuleResourceGroup, ModuleResourceItem, StudioResourceItem } from '../platform/studio'
import {
  studioApplicationAddress,
  studioKindAddress,
  studioRequestsAddress,
  studioResourceAddress,
  studioViewAddress,
} from '../platform/studioAddress'
import { kindGroup, kindGroupNames, kindName, studioKind, studioKinds } from '../platform/studioKinds'
import { type FilterGroup, parseFilter } from '../platform/tableFilter'
import { requestFields } from './studioAuthoringRequest'

/**
 * The filter of the Needs action view: the reader's own requests that wait for them, in Awaiting approval or Ready. Its
 * keys are in the order `parseFilter` writes them, so the address's filter compares equal to it.
 */
export function needsActionFilter(reader: string): FilterGroup {
  return {
    all: [
      { field: 'author', op: 'eq', value: reader },
      {
        any: [
          { field: 'state', op: 'eq', value: 'awaitingApproval' },
          { field: 'state', op: 'eq', value: 'ready' },
        ],
      },
    ],
  }
}

/**
 * The sidebar entries of an open application, in the order that docs/reference/studio-workspace.md#workspace gives.
 * `reader` is the display name of a person who may ask AI, or `null`: the AI requests entry shows only for a name.
 */
export function studioNavigation(
  application: string,
  resources: readonly StudioResourceItem[],
  moduleGroups: readonly ModuleResourceGroup[],
  t: (key: string) => string,
  reader: string | null = null,
): ShellNavigationNode[] {
  const manifest = resources.find((item) => item.kind === 'application')
  const kinds = new Set([
    ...resources.map((item) => item.kind),
    ...moduleGroups.filter((group) => group.items.length > 0).map((group) => group.kind),
  ])
  const kindEntry = (kind: string): ShellNavigationEntry => ({
    key: `kind:${kind}`,
    path: studioKindAddress(application, kind),
    icon: studioKind(kind)?.icon ?? null,
    label: kindName(kind, t),
  })
  const policyMatrix: ShellNavigationEntry = {
    key: 'policies',
    path: studioViewAddress(application, 'policies'),
    icon: 'list',
    label: t('shell.studio.view.policies'),
  }
  const packages: ShellNavigationEntry = {
    key: 'packages',
    path: studioViewAddress(application, 'packages'),
    icon: 'tool',
    label: t('shell.studio.nav.packages'),
  }

  const entries: ShellNavigationNode[] = [
    {
      key: 'overview',
      path: studioApplicationAddress(application),
      icon: 'dashboard',
      label: t('shell.studio.nav.overview'),
    },
    {
      key: 'releases',
      path: studioViewAddress(application, 'releases'),
      icon: 'tag',
      label: t('shell.studio.releases'),
    },
    {
      key: 'history',
      path: studioViewAddress(application, 'history'),
      icon: 'clock',
      label: t('shell.studio.history'),
    },
  ]
  if (reader !== null) {
    entries.push({
      key: 'requests',
      icon: 'inbox',
      label: t('shell.studio.aiRequests.title'),
      children: [
        {
          key: 'requests:all',
          path: studioRequestsAddress(application),
          icon: 'list',
          label: t('shell.studio.aiRequests.all'),
        },
        {
          key: 'requests:needsAction',
          path: studioRequestsAddress(application, needsActionFilter(reader)),
          icon: 'tasks',
          label: t('shell.studio.aiRequests.needsAction'),
        },
      ],
    })
  }
  if (manifest !== undefined) {
    entries.push({
      key: 'settings',
      path: studioResourceAddress(application, manifest.id),
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
    if (name === 'integration') {
      groupEntries.push(packages)
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

/**
 * The key of the sidebar entry the address shows: an open resource's kind, the manifest's Settings, or a view. The publish page marks none.
 * Packages stays marked on a package's page, `&package=<id>`.
 */
export function studioActiveEntry(
  params: URLSearchParams,
  resources: readonly StudioResourceItem[],
  moduleItems: readonly ModuleResourceItem[],
  reader: string | null = null,
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
  if (view === 'publish') {
    return ''
  }
  if (view === 'release') {
    return 'releases'
  }
  if (view === 'releases' || view === 'history' || view === 'policies' || view === 'packages') {
    return view
  }
  // The AI requests table and a request's page are under the AI requests entry, see docs/reference/ai-authoring-in-studio.md#the-request-panel.
  // Needs action is marked only while the table shows exactly its filter, so search, sort and paging do not count.
  if (view === 'requests') {
    const filter = parseFilter(
      params.get('filter') ?? '',
      requestFields.map((field) => field.name),
    )
    const needsAction =
      reader !== null && !params.has('request') && JSON.stringify(filter) === JSON.stringify(needsActionFilter(reader))
    return needsAction ? 'requests:needsAction' : 'requests:all'
  }
  return 'overview'
}
