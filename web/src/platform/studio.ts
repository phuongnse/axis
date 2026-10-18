import { apiFetch } from './api'
import { forbidden } from './inspection'

/** An application in the list of `GET /api/studio/apps`. */
export interface StudioApplication {
  name: string
}

/** A resource in the list of `GET /api/studio/apps/{application}/resources`. `name` is `null` when the file has none. */
export interface StudioResourceItem {
  id: string
  kind: string
  name: string | null
  file: string
  version: string
}

/** A compile diagnostic of a Studio folder. `resourceId` is `null` when it belongs to no resource. */
export interface StudioDiagnostic {
  code: string
  severity: 'error' | 'warning'
  message: string
  file: string
  path: string
  resourceId: string | null
}

/** The resources of one kind, sorted by name, or by file when a resource has no name. */
export interface ResourceGroup {
  kind: string
  items: StudioResourceItem[]
}

/**
 * Loads the applications Studio may edit. A 403 returns {@link forbidden}. A 401 starts sign-in and
 * throws, as any other failure does.
 */
export async function fetchStudioApps(signal?: AbortSignal): Promise<StudioApplication[] | typeof forbidden> {
  const response = await apiFetch('/api/studio/apps', { signal })
  if (response.status === 403) {
    return forbidden
  }
  if (!response.ok) {
    throw new Error(`Loading the Studio applications failed with status ${response.status}.`)
  }
  return ((await response.json()) as { items: StudioApplication[] }).items
}

/** Loads the resources of an application's folder, sorted by file. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioResources(application: string, signal?: AbortSignal): Promise<StudioResourceItem[]> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources`
  const response = await apiFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return ((await response.json()) as { items: StudioResourceItem[] }).items
}

/** Loads the compile diagnostics of an application's folder. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioDiagnostics(application: string, signal?: AbortSignal): Promise<StudioDiagnostic[]> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/diagnostics`
  const response = await apiFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return ((await response.json()) as { diagnostics: StudioDiagnostic[] }).diagnostics
}

/** The resources whose name or id contains `search`, ignoring letter case and surrounding spaces. An empty search keeps all. */
export function filterResources(items: StudioResourceItem[], search: string): StudioResourceItem[] {
  const needle = search.trim().toLowerCase()
  if (needle === '') {
    return items
  }
  return items.filter(
    (item) => item.id.toLowerCase().includes(needle) || (item.name?.toLowerCase().includes(needle) ?? false),
  )
}

/** The text a resource shows in the list: its name, or its file when it has none. */
export function resourceTitle(item: StudioResourceItem): string {
  return item.name ?? item.file
}

/** Groups resources by kind. Kinds are sorted ordinally, and each group's items by name, or by file when a resource has no name. */
export function groupByKind(items: StudioResourceItem[]): ResourceGroup[] {
  const groups = new Map<string, StudioResourceItem[]>()
  for (const item of items) {
    const group = groups.get(item.kind)
    if (group) {
      group.push(item)
    } else {
      groups.set(item.kind, [item])
    }
  }
  return [...groups.entries()]
    .sort(([left], [right]) => compareOrdinal(left, right))
    .map(([kind, members]) => ({
      kind,
      items: members.toSorted((left, right) => compareOrdinal(resourceTitle(left), resourceTitle(right))),
    }))
}

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0
}
