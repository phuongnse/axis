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

/** A diagnostic of an expression check. `start` and `end` are UTF-16 offsets into the expression, and `end` is exclusive. */
export interface ExpressionDiagnostic {
  code: string
  message: string
  start: number
  end: number
}

/** The answer of an expression check. `type` is `null` when there is a diagnostic. */
export interface ExpressionCheck {
  type: string | null
  diagnostics: ExpressionDiagnostic[]
}

/**
 * Checks one expression where it stands, without saving it. Any status other than 200 answers `null`, and the
 * check does not start sign-in on a 401. It rejects when the server does not answer.
 */
export async function checkStudioExpression(
  application: string,
  resourceId: string,
  path: string,
  expression: string,
  signal?: AbortSignal,
): Promise<ExpressionCheck | null> {
  const response = await apiFetch(
    `/api/studio/apps/${encodeURIComponent(application)}/expressions/check`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ resourceId, path, expression }),
      signal,
    },
    { signInOnUnauthorized: false },
  )
  if (!response.ok) {
    return null
  }
  return (await response.json()) as ExpressionCheck
}

/** An application in the list of `GET /api/studio/apps`. */
export interface StudioApplication {
  name: string
}

/** An application template in the list of `GET /api/studio/templates`. `title` and `description` hold `en` and `vi`. */
export interface StudioTemplate {
  id: string
  version: string
  title: Record<string, string>
  description: Record<string, string>
}

/** The answer of a create: the new application's name and diagnostics, or why it was refused. */
export type CreateApplicationResult =
  { status: 'created'; name: string; diagnostics: StudioDiagnostic[] } | { status: 'conflict' } | { status: 'invalid' }

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

/** A field of an entity in `GET .../names`. `target` and `values` are `null` when the field has none. */
export interface StudioNameField {
  name: string
  type: string
  target: string | null
  values: string[] | null
}

/** The names of one kind in `GET .../names`. `module` is the module id, or `null` for the application's own. */
export interface StudioNameEntity {
  id: string
  name: string
  module: string | null
  fields: StudioNameField[]
}

/** A process step in `GET .../names`. `outcomes` is empty for a step that is not a task. */
export interface StudioNameStep {
  name: string
  type: string
  outcomes: string[]
}

/** A named resource in `GET .../names`, such as a role, form or page. */
export interface StudioNameResource {
  id: string
  name: string
}

/** A text key in `GET .../names`, with its text in each locale that has it. */
export interface StudioNameText {
  key: string
  texts: Record<string, string>
  usedBy: number
}

/** Every name an application defines, as `GET /api/studio/apps/{application}/names` answers it. */
export interface StudioNames {
  locales: string[]
  entities: StudioNameEntity[]
  dataSources: { id: string; name: string; module: string | null }[]
  processes: { id: string; name: string; steps: StudioNameStep[] }[]
  rules: {
    id: string
    name: string
    module: string | null
    parameters: { name: string; type: string }[]
    resultType: string
  }[]
  connectors: { id: string; name: string; operations: { name: string }[] }[]
  events: { id: string; name: string; fields: { name: string; type: string }[] }[]
  roles: StudioNameResource[]
  sequences: StudioNameResource[]
  forms: StudioNameResource[]
  pages: StudioNameResource[]
  texts: StudioNameText[]
}

/**
 * The answer to a create: the new resource as the list shows it and the folder's diagnostics after the write, `conflict`
 * when the id or the target file exists, or `invalid` when the name or the locale is refused.
 */
export type CreateResult =
  | { status: 'created'; item: StudioResourceItem; diagnostics: StudioDiagnostic[] }
  | { status: 'conflict' }
  | { status: 'invalid' }

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

/** Loads the newest installed version of each application template. A 401 starts sign-in and throws, as any other failure does. */
export async function fetchStudioTemplates(signal?: AbortSignal): Promise<StudioTemplate[]> {
  const response = await studioFetch('/api/studio/templates', { signal })
  if (!response.ok) {
    throw new Error(`Loading the Studio templates failed with status ${response.status}.`)
  }
  return ((await response.json()) as { items: StudioTemplate[] }).items
}

/**
 * Creates an application, blank without a `template`, and returns its name and diagnostics. A `409` is a name that is
 * taken and a `400` a refused name. Any other failure throws.
 */
export async function createStudioApp(name: string, template?: string): Promise<CreateApplicationResult> {
  const response = await studioFetch('/api/studio/apps', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(template === undefined ? { name } : { name, template }),
  })
  if (response.status === 409) {
    return { status: 'conflict' }
  }
  if (response.status === 400) {
    return { status: 'invalid' }
  }
  if (!response.ok) {
    throw new Error(`Posting '/api/studio/apps' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { name: string; diagnostics: StudioDiagnostic[] }
  return { status: 'created', name: body.name, diagnostics: body.diagnostics }
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

/** Loads every name the application defines, for the reference pickers and expression completion. */
export async function fetchStudioNames(application: string, signal?: AbortSignal): Promise<StudioNames> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/names`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioNames
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

/**
 * Creates a resource from `content`, which holds its `id`, `kind` and `name`. A 409 means the id or the file exists, and
 * a 400 means the name or the locale is refused. Nothing is written for either. A 401 starts sign-in and throws, as any
 * other failure does.
 */
export async function createStudioResource(
  application: string,
  content: Record<string, unknown>,
): Promise<CreateResult> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources`
  const response = await studioFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(content),
  })
  if (response.status === 409) {
    return { status: 'conflict' }
  }
  if (response.status === 400) {
    return { status: 'invalid' }
  }
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as StudioResourceItem & { diagnostics: StudioDiagnostic[] }
  const { diagnostics, ...item } = body
  return {
    status: 'created',
    item: {
      id: item.id,
      kind: item.kind,
      name: item.name,
      file: item.file,
      version: item.version,
      usedBy: 0,
    },
    diagnostics,
  }
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

/** What a text write answers: the new version of every locale's file, or `invalid` for a `400`. */
export type TextKeyWriteResult =
  | { status: 'written'; resources: { id: string; locale: string; version: string }[]; diagnostics: StudioDiagnostic[] }
  | { status: 'invalid' }

/**
 * Writes one text key in every locale at once. `texts` holds the text of each locale. A 400 means the locales or
 * the key are refused, and nothing was written. A 401 starts sign-in and throws, as any other failure does.
 */
export async function writeStudioText(
  application: string,
  key: string,
  texts: Record<string, string>,
): Promise<TextKeyWriteResult> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/texts/${encodeURIComponent(key)}`
  const response = await studioFetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ texts }),
  })
  if (response.status === 400) {
    return { status: 'invalid' }
  }
  if (!response.ok) {
    throw new Error(`Writing '${path}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as {
    resources: { id: string; locale: string; version: string }[]
    diagnostics: StudioDiagnostic[]
  }
  return { status: 'written', resources: body.resources, diagnostics: body.diagnostics }
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

/** The answer to a text write: the new version of each text resource that was written, and the folder's diagnostics. */
export interface TextWriteResult {
  resources: { id: string; locale: string; version: string }[]
  diagnostics: StudioDiagnostic[]
}

/**
 * Writes one text key in every locale at once. `texts` holds exactly the folder's locales. It takes no version, so the
 * last write to a key wins. Any status other than 200 throws, so the author's input stays. A 401 starts sign-in and
 * throws, as any other failure does.
 */
export async function putStudioTexts(
  application: string,
  key: string,
  texts: Record<string, string>,
): Promise<TextWriteResult> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/texts/${encodeURIComponent(key)}`
  const response = await studioFetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ texts }),
  })
  if (!response.ok) {
    throw new Error(`Putting '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as TextWriteResult
}

/** The status of a publish request. `succeeded` and `failed` are final. */
export type StudioRequestStatus = 'queued' | 'running' | 'succeeded' | 'failed'

/**
 * One scenario of a `test` request. `step` is the 0-based index of the failing step and `path` is `/steps/{step}`, both
 * `null` when the scenario passed or failed before any step.
 */
export interface StudioScenarioResult {
  name: string
  resourceId: string
  file: string
  outcome: 'passed' | 'failed'
  durationMs: number
  step: number | null
  path: string | null
  message: string | null
}

/**
 * A request, as `GET /api/studio/apps/{application}/requests/{id}` answers. The diagnostics are empty until it ends.
 * `scenarios` is `null` unless the kind is `test`, and then it is empty until the request ends.
 */
export interface StudioRequest {
  id: string
  application: string
  kind: 'validate' | 'publish' | 'rollback' | 'test'
  target: 'tenant' | 'preview'
  status: StudioRequestStatus
  label: string | null
  note: string | null
  releaseId: string | null
  diagnostics: StudioDiagnostic[]
  scenarios: StudioScenarioResult[] | null
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

/**
 * Sends a test request, which runs the scenarios named in `scenarios`, or every scenario when it is undefined. It
 * answers at once with the queued request. A 401 starts sign-in and throws, as any other failure does.
 */
export async function testStudioApp(application: string, scenarios?: string[]): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/test`
  const response = await studioFetch(path, {
    method: 'POST',
    headers: scenarios === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: scenarios === undefined ? undefined : JSON.stringify({ scenarios }),
  })
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
