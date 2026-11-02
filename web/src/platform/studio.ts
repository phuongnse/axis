import { apiFetch } from './api'
import { forbidden } from './inspection'
import type { JsonPatchOperation } from './jsonPatch'
import type { EntityWidgetMetadata, PageMetadata } from './site'

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
 * Resolves once `GET /api/me` answers with anything but no answer or 502, 503 or 504. It does not show the sign-in prompt on
 * a 401, because the reloads after the reconnect do. It rejects when `signal` aborts.
 */
export async function waitForServer(signal: AbortSignal, delays: readonly number[] = reconnectDelays): Promise<void> {
  for (let attempt = 0; ; attempt++) {
    await pause(delays[Math.min(attempt, delays.length - 1)], signal)
    try {
      const response = await apiFetch('/api/me', { signal }, { promptSignInOnUnauthorized: false })
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
 * check does not show the sign-in prompt on a 401. It rejects when the server does not answer.
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
    { promptSignInOnUnauthorized: false },
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

/** What the draft page metadata route answers: the page as the site would serve it, or `null` when it cannot be drawn. */
export interface StudioPageMetadataAnswer {
  page: PageMetadata | null
  diagnostics: StudioDiagnostic[]
}

/** What the draft page metadata route answers for a form: the form widget as the site would serve it, or `null`. */
export interface StudioFormMetadataAnswer {
  widget: EntityWidgetMetadata | null
  diagnostics: StudioDiagnostic[]
}

async function postDraftMetadata(
  application: string,
  id: string,
  draft: Record<string, unknown>,
  signal?: AbortSignal,
): Promise<unknown> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources/${encodeURIComponent(id)}/page-metadata`
  const response = await studioFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(draft),
    signal,
  })
  if (response.status !== 200) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  return response.json()
}

/**
 * Asks for the metadata an unsaved page would have, without saving it. Any status other than 200 throws, as does a
 * server that does not answer.
 */
export async function fetchStudioPageMetadata(
  application: string,
  id: string,
  draft: Record<string, unknown>,
  signal?: AbortSignal,
): Promise<StudioPageMetadataAnswer> {
  return (await postDraftMetadata(application, id, draft, signal)) as StudioPageMetadataAnswer
}

/**
 * Loads the metadata of a page of the module version that the application pins, so Studio can draw it read-only. `id` is
 * the id written in the module's file. Any status other than 200 throws, as does a server that does not answer.
 */
export async function fetchStudioModulePageMetadata(
  application: string,
  module: string,
  id: string,
  signal?: AbortSignal,
): Promise<StudioPageMetadataAnswer> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/modules/${encodeURIComponent(module)}/resources/${encodeURIComponent(id)}/page-metadata`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioPageMetadataAnswer
}

/** Asks for the form widget an unsaved form would have, in the same way as {@link fetchStudioPageMetadata}. */
export async function fetchStudioFormMetadata(
  application: string,
  id: string,
  draft: Record<string, unknown>,
  signal?: AbortSignal,
): Promise<StudioFormMetadataAnswer> {
  return (await postDraftMetadata(application, id, draft, signal)) as StudioFormMetadataAnswer
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

/**
 * A data source in `GET .../names`. `groupBy` lists the group fields of a grouped data source, and is `null` for a plain
 * one. A grouped data source's `columns` are its group fields, then its measures.
 */
export interface StudioNameDataSource {
  id: string
  name: string
  module: string | null
  parameters: { name: string; type: string }[]
  columns: { name: string; type: string | null }[]
  groupBy: string[] | null
}

/** A process step in `GET .../names`. `outcomes` is empty for a step that is not a task. */
export interface StudioNameStep {
  name: string
  type: string
  outcomes: string[]
}

/** A named resource in `GET .../names`, such as a role, sequence or form. */
export interface StudioNameResource {
  id: string
  name: string
}

/** A page in `GET .../names`, with the parameters it declares. `module` is the module id, or `null` for the application's own. */
export interface StudioNamePage {
  id: string
  name: string
  module: string | null
  parameters: { name: string; type: string; required: boolean }[]
}

/** A policy in `GET .../names`. `module` is the module id, or `null` for the application's own. */
export interface StudioNamePolicy {
  id: string
  name: string
  module: string | null
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
  dataSources: StudioNameDataSource[]
  processes: {
    id: string
    name: string
    module: string | null
    entity: string | null
    parameters: { name: string; type: string }[]
    steps: StudioNameStep[]
  }[]
  rules: {
    id: string
    name: string
    module: string | null
    parameters: { name: string; type: string }[]
    resultType: string
  }[]
  connectors: { id: string; name: string; operations: { name: string }[] }[]
  events: { id: string; name: string; entity: string; fields: { name: string; type: string }[] }[]
  roles: StudioNameResource[]
  sequences: StudioNameResource[]
  forms: StudioNameResource[]
  pages: StudioNamePage[]
  policies: StudioNamePolicy[]
  contracts: { id: string; name: string; module: string }[]
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
 * Loads the applications Studio may edit. A 403 returns {@link forbidden}. A 401 shows the sign-in prompt and
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

/** Loads the newest installed version of each application template. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** Loads the resources of an application's folder, sorted by file. A 401 shows the sign-in prompt and throws, as any other failure does. */
export async function fetchStudioResources(application: string, signal?: AbortSignal): Promise<StudioResourceItem[]> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/resources`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return ((await response.json()) as { items: StudioResourceItem[] }).items
}

/** Loads the compile diagnostics of an application's folder. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** A column of the policy matrix: a root entity, data source, process or endpoint. */
export interface StudioMatrixTarget {
  kind: 'entity' | 'dataSource' | 'process' | 'endpoint'
  id: string
  name: string
  module: string | null
  actions: string[]
  entity: string | null
}

/**
 * One grant behind a matrix cell, as written in its policy. `fields` is null for every field. `module` is the
 * module id when the grant comes from a module's policy, which Studio does not edit.
 */
export interface StudioMatrixGrant {
  policyId: string
  policy: string
  index: number
  actions: string[]
  filter: string | null
  fields: string[] | null
  shared: boolean
  module: string | null
}

/** What one role may do on one target. `missingEntityRead` names the root entity a data source `read` lacks. */
export interface StudioMatrixCell {
  kind: string
  target: string
  actions: string[]
  missingEntityRead: string | null
  grants: StudioMatrixGrant[]
}

/** A row of the policy matrix. `cells[i]` belongs to `targets[i]`. */
export interface StudioMatrixRole {
  id: string
  name: string
  accessPolicy: { name: string; id: string | null }
  editable: boolean
  readOnlyReason: string | null
  cells: StudioMatrixCell[]
}

/** What each role may do on each root entity, data source, process and endpoint. */
export interface StudioPolicyMatrix {
  targets: StudioMatrixTarget[]
  roles: StudioMatrixRole[]
}

/** Loads the policy matrix of an application's folder. A 401 shows the sign-in prompt and throws, as any other failure does. */
export async function fetchStudioPolicyMatrix(application: string, signal?: AbortSignal): Promise<StudioPolicyMatrix> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/policy-matrix`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioPolicyMatrix
}

/** Loads one resource with its content. A 401 shows the sign-in prompt and throws, as any other failure does. */
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
 * Loads one file of the module version that the application pins, with its content. The file is read-only, and its
 * `file` is `modules/<module>/<path>`. A 401 shows the sign-in prompt and throws, as any other failure does.
 */
export async function fetchStudioModuleResource(
  application: string,
  module: string,
  id: string,
  signal?: AbortSignal,
): Promise<StudioResource> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/modules/${encodeURIComponent(module)}/resources/${encodeURIComponent(id)}`
  const response = await studioFetch(path, { signal })
  if (!response.ok) {
    throw new Error(`Loading '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioResource
}

/**
 * Patches a resource, with the version the editor loaded in `If-Match`. A 409 means the file changed since,
 * and nothing was written. A 401 shows the sign-in prompt and throws, as any other failure does.
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

/** One change of a changes call: a full resource to create, or a patch of a resource with the version it was made from. */
export type StudioChange =
  { create: Record<string, unknown> } | { id: string; version: string; patch: JsonPatchOperation[] }

/** The answer of a changes call: the id and new version of each change in request order, or why nothing was written. */
export type ChangesResult =
  | { status: 'saved'; items: { id: string; version: string }[]; diagnostics: StudioDiagnostic[] }
  | { status: 'conflict' }

/**
 * Writes several changes in one call, in list order. A 409 means a patched version is stale or a created id or file
 * exists, and nothing was written. A 401 shows the sign-in prompt and throws, as any other failure does, including a 400.
 */
export async function saveStudioChanges(application: string, changes: StudioChange[]): Promise<ChangesResult> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/changes`
  const response = await studioFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ changes }),
  })
  if (response.status === 409) {
    return { status: 'conflict' }
  }
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { items: { id: string; version: string }[]; diagnostics: StudioDiagnostic[] }
  return { status: 'saved', items: body.items, diagnostics: body.diagnostics }
}

/**
 * Creates a resource from `content`, which holds its `id`, `kind` and `name`. A 409 means the id or the file exists, and
 * a 400 means the name or the locale is refused. Nothing is written for either. A 401 shows the sign-in prompt and throws, as any
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

/** Loads what a resource uses and what uses it. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** Loads what uses a text key. A 401 shows the sign-in prompt and throws, as any other failure does. */
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
 * the key are refused, and nothing was written. A 401 shows the sign-in prompt and throws, as any other failure does.
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
 * and nothing was deleted. A 401 shows the sign-in prompt and throws, as any other failure does.
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
 * last write to a key wins. Any status other than 200 throws, so the author's input stays. A 401 shows the sign-in prompt and
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
 * One scenario of a `test` request, or a contract scenario of a `publish` or `validate`. `step` is the 0-based index of
 * the failing step and `path` is `/steps/{step}`, both `null` when the scenario passed or failed before any step.
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
 * A contract scenario that a relax entry skipped in a `publish` or `validate`. `point` is the relaxed point it covers.
 */
export interface StudioSkippedScenarioResult {
  name: string
  file: string
  outcome: 'skipped'
  point: string
}

/**
 * A request, as `GET /api/studio/apps/{application}/requests/{id}` answers. The diagnostics are empty until it ends.
 * `scenarios` holds the stored results of a `test`, `publish` or `validate`, and is `null` when none were stored. For a
 * `test` it is empty until the request ends.
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
  scenarios: (StudioScenarioResult | StudioSkippedScenarioResult)[] | null
}

/**
 * One activation in the release history. `actor` is a user id, `startup`, `operator` or `unknown`. `actorName` is the
 * name in the actor's user profile, else the one stored with the activation, else `null`.
 */
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

/** Sends a validate request. It answers at once with the queued request. A 401 shows the sign-in prompt and throws, as any other failure does. */
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
 * answers at once with the queued request. A 401 shows the sign-in prompt and throws, as any other failure does.
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

/** Sends a preview request, which publishes the folder to the preview tenant. It answers at once with the queued request. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** Sends a rollback request to an earlier release. It answers at once with the queued request. A 401 shows the sign-in prompt and throws, as any other failure does. */
export async function rollbackStudioRelease(application: string, releaseId: string): Promise<StudioRequest> {
  const path = `/api/studio/apps/${encodeURIComponent(application)}/releases/${encodeURIComponent(releaseId)}/rollback`
  const response = await studioFetch(path, { method: 'POST' })
  if (!response.ok) {
    throw new Error(`Posting '${path}' failed with status ${response.status}.`)
  }
  return (await response.json()) as StudioRequest
}

/** Sends a publish request. Empty strings are left out of the body. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** Loads a request with its status and diagnostics. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** Loads the release history of an application. A 401 shows the sign-in prompt and throws, as any other failure does. */
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

/** The kind of a module resource that Studio lists and opens read-only. */
export type ModuleResourceKind = 'entity' | 'process' | 'policy' | 'contract' | 'page'

/** An entity, process, policy, contract or page of a module the application uses, as the names give it. `name` is `<module>/<name>`. */
export interface ModuleResourceItem {
  id: string
  kind: ModuleResourceKind
  name: string
  module: string
}

/** The module resources of one kind, in the order the names give them. */
export interface ModuleResourceGroup {
  kind: ModuleResourceKind
  items: ModuleResourceItem[]
}

/**
 * The module entities, processes, policies, contracts and pages in the names, in that order, with the application's own left out.
 * An item stays when its name or id contains `search`, ignoring letter case and surrounding spaces. A kind with no
 * item has no group.
 */
export function moduleResources(names: StudioNames | null, search: string): ModuleResourceGroup[] {
  if (names === null) {
    return []
  }
  const needle = search.trim().toLowerCase()
  const sources: [ModuleResourceKind, { id: string; name: string; module: string | null }[]][] = [
    ['entity', names.entities],
    ['process', names.processes],
    ['policy', names.policies],
    ['contract', names.contracts],
    ['page', names.pages],
  ]
  return sources
    .map(([kind, source]) => ({
      kind,
      items: source
        .filter(
          (item) =>
            item.module !== null &&
            (needle === '' || item.name.toLowerCase().includes(needle) || item.id.toLowerCase().includes(needle)),
        )
        .map((item): ModuleResourceItem => ({ id: item.id, kind, name: item.name, module: item.module! })),
    }))
    .filter((group) => group.items.length > 0)
}

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0
}
