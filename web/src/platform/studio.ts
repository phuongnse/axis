import { apiFetch } from './api'
import { forbidden } from './inspection'
import type { JsonPatchOperation } from './jsonPatch'

/** The server did not answer a Studio request, or answered it with 502, 503 or 504. */
export class ServerUnreachableError extends Error {}

const unavailableStatuses = new Set([502, 503, 504])

/** The waits before each `GET /api/me` while the server is unreachable. The last wait repeats. */
export const reconnectDelays = [500, 1000, 2000, 4000]

/** `apiFetch` for Studio. A request that gets no answer, or an unavailable status, throws {@link ServerUnreachableError}. */
async function studioFetch(path: string, init?: RequestInit): Promise<Response> {
  let response: Response
  try {
    response = await apiFetch(path, init)
  } catch (error) {
    if (init?.signal?.aborted || (error instanceof Error && error.name === 'AbortError')) {
      throw error
    }
    throw new ServerUnreachableError(`The server did not answer '${path}'.`)
  }
  if (unavailableStatuses.has(response.status)) {
    throw new ServerUnreachableError(`'${path}' answered with status ${response.status}.`)
  }
  return response
}

/**
 * Resolves once `GET /api/me` answers with anything but no answer or 502, 503 or 504. It does not start sign-in on
 * a 401, because the reloads after the reconnect do. It rejects when `signal` aborts.
 */
export async function waitForServer(signal: AbortSignal, delays: readonly number[] = reconnectDelays): Promise<void> {
  for (let attempt = 0; ; attempt++) {
    await pause(delays[Math.min(attempt, delays.length - 1)], signal)
    try {
      const response = await apiFetch('/api/me', { signal }, { signInOnUnauthorized: false })
      if (!unavailableStatuses.has(response.status)) {
        return
      }
    } catch (error) {
      if (signal.aborted) {
        throw error
      }
    }
  }
}

function pause(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal.reason)
    }
    const timer = setTimeout(() => {
      signal.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    signal.addEventListener('abort', onAbort, { once: true })
  })
}

/** An application in the list of `GET /api/studio/apps`. */
export interface StudioApplication {
  name: string
}

/**
 * A resource in the list of `GET /api/studio/apps/{application}/resources`. `name` is `null` when the file has none.
 * `usedBy` counts the references that point at the resource, so one resource that refers twice counts twice.
 */
export interface StudioResourceItem {
  id: string
  kind: string
  name: string | null
  file: string
  version: string
  usedBy: number
}

/** A resource with its content, as `GET /api/studio/apps/{application}/resources/{id}` answers. */
export interface StudioResource extends StudioResourceItem {
  content: Record<string, unknown>
}

/** What a patch answers: the new version, and the diagnostics of the folder after the write. */
export type PatchResult = { status: 'saved'; version: string; diagnostics: StudioDiagnostic[] } | { status: 'conflict' }

/** A compile diagnostic of a Studio folder. `resourceId` is `null` when it belongs to no resource. */
export interface StudioDiagnostic {
  code: string
  severity: 'error' | 'warning'
  message: string
  file: string
  path: string
  resourceId: string | null
}

/** The other end of a reference. Each type writes only its own fields. */
export type StudioReferenceNode =
  | { type: 'resource'; id: string; kind: string; name: string | null; file: string }
  | { type: 'text'; key: string }
  | { type: 'package'; id: string }
  | { type: 'module'; id: string }

/**
 * One reference. `file` and `path` are always in the resource that holds the reference, and `node` is the other end.
 */
export interface StudioReference {
  relation: string
  file: string
  path: string
  node: StudioReferenceNode
}

/** What a resource refers to (`uses`) and what refers to it (`usedBy`). */
export interface StudioResourceReferences {
  uses: StudioReference[]
  usedBy: StudioReference[]
}

/** The `usedBy` entries of a text key: the labels and text resources that use it. */
export interface StudioTextReferences {
  usedBy: StudioReference[]
}

/** The answer to a delete: the folder's diagnostics after it, or `conflict` when the file changed since it was read. */
export type DeleteResult = { status: 'deleted'; diagnostics: StudioDiagnostic[] } | { status: 'conflict' }

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
  const response = await studioFetch('/api/studio/apps', { signal })
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
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return ((await response.json()) as { items: StudioResourceItem[] }).items
}

/** Loads the compile diagnostics of an application's folder. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioDiagnostics(application: string, signal?: AbortSignal): Promise<StudioDiagnostic[]> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/diagnostics`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return ((await response.json()) as { diagnostics: StudioDiagnostic[] }).diagnostics
}

/** Loads one resource with its content. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioResource(
  application: string,
  id: string,
  signal?: AbortSignal,
): Promise<StudioResource> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources/${encodeURIComponent(id)}`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioResource
}

/**
 * Patches a resource, with the version the editor loaded in `If-Match`. A 409 means the file changed since,
 * and nothing was written. A 401 starts sign-in and throws, as any other failure does.
 */
export async function patchStudioResource(
  application: string,
  id: string,
  version: string,
  operations: JsonPatchOperation[],
): Promise<PatchResult> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources/${encodeURIComponent(id)}`
  const response = await studioFetch(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json-patch+json', 'If-Match': `"${version}"` },
    body: JSON.stringify(operations),
  })
  if (response.status === 409) {
    return { status: 'conflict' }
  }
  if (!response.ok) {
    throw new Error(`Patching '${path}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { version: string; diagnostics: StudioDiagnostic[] }
  return { status: 'saved', version: body.version, diagnostics: body.diagnostics }
}

/** Loads what a resource uses and what uses it. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchResourceReferences(
  application: string,
  id: string,
  signal?: AbortSignal,
): Promise<StudioResourceReferences> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources/${encodeURIComponent(id)}/references`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioResourceReferences
}

/** Loads what uses a text key. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchTextReferences(
  application: string,
  key: string,
  signal?: AbortSignal,
): Promise<StudioTextReferences> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/texts/${encodeURIComponent(key)}/references`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioTextReferences
}

/**
 * Deletes a resource, with the version the editor loaded in `If-Match`. A 409 means the file changed since,
 * and nothing was deleted. A 401 starts sign-in and throws, as any other failure does.
 */
export async function deleteStudioResource(application: string, id: string, version: string): Promise<DeleteResult> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources/${encodeURIComponent(id)}`
  const response = await studioFetch(path, { method: 'DELETE', headers: { 'If-Match': `"${version}"` } })
  if (response.status === 409) {
    return { status: 'conflict' }
  }
  if (!response.ok) {
    throw new Error(`Deleting '${path}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { diagnostics: StudioDiagnostic[] }
  return { status: 'deleted', diagnostics: body.diagnostics }
}

/** The status of a publish request. `succeeded` and `failed` are final. */
export type StudioRequestStatus = 'queued' | 'running' | 'succeeded' | 'failed'

/** A request, as `GET /api/studio/apps/{application}/requests/{id}` answers. The diagnostics are empty until it ends. */
export interface StudioRequest {
  id: string
  application: string
  kind: 'validate' | 'publish' | 'rollback'
  target: 'tenant' | 'preview'
  status: StudioRequestStatus
  label: string | null
  note: string | null
  releaseId: string | null
  diagnostics: StudioDiagnostic[]
}

/** One activation in the release history. `actor` is a user id, `startup`, `operator` or `unknown`. */
export interface StudioReleaseItem {
  releaseId: string
  number: number
  contentHash: string
  activatedAt: string
  actor: string
  actorName: string | null
  label: string | null
  note: string | null
  requestId: string | null
}

/**
 * The release history of `GET /api/studio/apps/{application}/releases`. `folderContentHash` is `null` when the folder
 * has errors, so it does not compile.
 */
export interface StudioReleases {
  active: { releaseId: string; number: number; contentHash: string } | null
  folderContentHash: string | null
  items: StudioReleaseItem[]
}

/** A row of the release list. `active` is set on one row only. */
export type StudioReleaseRow = StudioReleaseItem & { active: boolean }

/** The optional label and note of a publish. */
export interface PublishDetails {
  label?: string
  note?: string
}

/** Sends a validate request. It answers at once with the queued request. A 401 starts sign-in and throws, as any other failure does. */
export async function validateStudioApp(application: string): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/validate`
  const response = await studioFetch(path, { method: 'POST' })
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioRequest
}

/** Sends a preview request, which publishes the folder to the preview tenant. It answers at once with the queued request. A 401 starts sign-in and throws, as any other failure does. */
export async function previewStudioApp(application: string): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/preview`
  const response = await studioFetch(path, { method: 'POST' })
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioRequest
}

/**
 * The address of a site on the preview host. It takes the protocol and the port of the page, because the preview host
 * is served on the same ones as the main host.
 */
export function previewSiteUrl(
  host: string,
  sitePath: string,
  location: Pick<Location, 'protocol' | 'port'> = window.location,
): string {
  return `${location.protocol}//${host}${location.port ? `:${location.port}` : ''}/${sitePath}`
}

/** Sends a rollback request to an earlier release. It answers at once with the queued request. A 401 starts sign-in and throws, as any other failure does. */
export async function rollbackStudioRelease(application: string, releaseId: string): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/releases/${encodeURIComponent(releaseId)}/rollback`
  const response = await studioFetch(path, { method: 'POST' })
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioRequest
}

/** Sends a publish request. Empty strings are left out of the body. A 401 starts sign-in and throws, as any other failure does. */
export async function publishStudioApp(application: string, details: PublishDetails = {}): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/publish`
  const body: PublishDetails = {}
  if (details.label) {
    body.label = details.label
  }
  if (details.note) {
    body.note = details.note
  }
  const response = await studioFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioRequest
}

/** Loads a request with its status and diagnostics. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioRequest(
  application: string,
  id: string,
  signal?: AbortSignal,
): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/requests/${encodeURIComponent(id)}`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioRequest
}

/** Loads the release history of an application. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioReleases(application: string, signal?: AbortSignal): Promise<StudioReleases> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/releases`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioReleases
}

/**
 * The rows of the release list, newest activation first. Rows with the same time keep the server's order. A release
 * that was activated more than once has a row for each activation, and only its newest row is active.
 */
export function releaseRows(history: StudioReleases): StudioReleaseRow[] {
  const sorted = history.items.toSorted((left, right) => Date.parse(right.activatedAt) - Date.parse(left.activatedAt))
  const activeId = history.active?.releaseId
  let activeSeen = false
  return sorted.map((item) => {
    const active = !activeSeen && activeId !== undefined && item.releaseId === activeId
    activeSeen ||= active
    return { ...item, active }
  })
}

/** Whether a release is active and the folder compiles to other content than that release. */
export function folderDiffers(history: StudioReleases): boolean {
  return (
    history.active !== null &&
    history.folderContentHash !== null &&
    history.folderContentHash !== history.active.contentHash
  )
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
