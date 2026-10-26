import { apiFetch } from './api'
import type { RecordProblem } from './records'
import type { EntityMetadata, FormMetadata } from './site'

/** The subject record of a task. `label` is its display field value, or `null` when there is none. */
export interface TaskSubject {
  entity: string
  id: string
  label: string | null
}

/**
 * An open task as the task API lists it. `dueAt` is `null` when the step has no `dueIn`. `claimedBy`
 * is the user id of the claimer, or `null` while the task is unclaimed. `overdue` is set once the
 * due date has passed.
 */
export interface TaskListItem {
  id: string
  /** The release the task started on. Its labels come from that release's texts. */
  releaseId: string
  process: string
  step: string
  labelKey: string
  subject: TaskSubject
  dueAt: string | null
  claimedBy: string | null
  overdue: boolean
  createdAt: string
}

/** One page of the signed-in user's open tasks. */
export interface TaskPage {
  items: TaskListItem[]
  page: number
  pageSize: number
  totalCount: number
}

/** One outcome of a task's step: its name, as the complete endpoint takes it, and its label. */
export interface TaskOutcome {
  name: string
  labelKey: string
}

/** The form a task is completed on, with its entity's metadata so the task page knows each field's type. */
export type TaskForm = FormMetadata & { entity: EntityMetadata }

/** A role of the active release that a task may be reassigned to. `labelKey` is `null` for a role with no label. */
export interface ReassignRole {
  name: string
  labelKey: string | null
}

/**
 * One task in any state, as the task API reads it. `assignee` has the user id or the role name, and
 * the other is `null`. `completedAt`, `completedBy` and `outcome` are `null` while the task is open.
 * `claimedBy` is the claimer's user id, or `null`. `overdue` is set once the due date has passed.
 * `commands` lists what the signed-in user may do on the task now, from `claim`, `release` and
 * `reassign`. `reassignRoles` is empty unless `commands` holds `reassign`.
 */
export interface TaskItem {
  id: string
  /** The release the task started on. Its labels come from that release's texts. */
  releaseId: string
  process: string
  instanceId: string
  step: string
  labelKey: string
  state: string
  assignee: { user: string | null; role: string | null }
  subject: TaskSubject
  form: TaskForm
  outcomes: TaskOutcome[]
  dueAt: string | null
  claimedBy: string | null
  overdue: boolean
  commands: string[]
  reassignRoles: ReassignRole[]
  createdAt: string
  completedAt: string | null
  completedBy: string | null
  outcome: string | null
}

/** The outcome of completing a task, or the problem the server reported. */
export type CompleteResult = { ok: true } | { ok: false; problem: RecordProblem }

/** The task after a claim or release, or the status the task API refused it with. */
export type TaskCommandResult = { ok: true; task: TaskItem } | { ok: false; status: number }

/** What the task API answers when the signed-in user may not act on the task. */
export const assignedElsewhere = 'assignedElsewhere'

/**
 * Loads one page of the signed-in user's open tasks, in the task API's order. `query` holds the
 * task API's `page` and `pageSize`. A 401 starts sign-in and throws, as any other failure does.
 */
export async function fetchTasks(tasksPath: string, query: string, signal?: AbortSignal): Promise<TaskPage> {
  const response = await apiFetch(`${tasksPath}${query ? `?${query}` : ''}`, { signal })
  if (!response.ok) {
    throw new Error(`Loading the tasks of '${tasksPath}' failed with status ${response.status}.`)
  }
  return (await response.json()) as TaskPage
}

/**
 * Loads one task in any state, or returns `null` when the task API has no task with the id. A 403
 * returns {@link assignedElsewhere}. A 401 starts sign-in and throws, as any other failure does.
 */
export async function fetchTask(
  tasksPath: string,
  id: string,
  signal?: AbortSignal,
): Promise<TaskItem | null | typeof assignedElsewhere> {
  const response = await apiFetch(`${tasksPath}/${id}`, { signal })
  if (response.status === 404) {
    return null
  }
  if (response.status === 403) {
    return assignedElsewhere
  }
  if (!response.ok) {
    throw new Error(`Loading the task '${id}' of '${tasksPath}' failed with status ${response.status}.`)
  }
  // A task read holds no field values, so plain numbers are safe.
  return JSON.parse(await response.text()) as TaskItem
}

/**
 * Completes a task. `body` is the prepared body text, so number text reaches the server unchanged.
 * A 400, 403 or 409 comes back as the problem. A 401 starts sign-in and throws, as any other
 * failure does.
 */
export async function completeTask(tasksPath: string, id: string, body: string): Promise<CompleteResult> {
  const response = await apiFetch(`${tasksPath}/${id}/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body,
  })
  if (response.ok) {
    return { ok: true }
  }
  if (response.status === 400 || response.status === 403 || response.status === 409) {
    const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
    return {
      ok: false,
      problem: { status: response.status, title: problem.title ?? '', errors: problem.errors ?? {} },
    }
  }
  throw new Error(`Completing the task '${id}' of '${tasksPath}' failed with status ${response.status}.`)
}

/**
 * Claims or releases a task and returns the task as the route answers it. A 403 or 409 comes back
 * as the status. A 401 starts sign-in and throws, as any other failure does.
 */
export async function runTaskCommand(
  tasksPath: string,
  id: string,
  command: 'claim' | 'release',
): Promise<TaskCommandResult> {
  const response = await apiFetch(`${tasksPath}/${id}/${command}`, { method: 'POST' })
  if (response.ok) {
    // A task read holds no field values, so plain numbers are safe.
    return { ok: true, task: JSON.parse(await response.text()) as TaskItem }
  }
  if (response.status === 403 || response.status === 409) {
    return { ok: false, status: response.status }
  }
  throw new Error(`Running '${command}' on the task '${id}' of '${tasksPath}' failed with status ${response.status}.`)
}

/** The body of a reassign: exactly one of `role` and `user`, and the required reason. */
export type ReassignBody = { role: string; reason: string } | { user: string; reason: string }

/** The task after a reassign, the problem of a 400, or the status the task API refused it with. */
export type ReassignResult =
  { ok: true; task: TaskItem } | { ok: false; problem: RecordProblem } | { ok: false; status: number }

/** A user profile as the user picker lists it. */
export interface UserOption {
  id: string
  displayName: string
}

/**
 * Reassigns a task to a role or a user. A 400 comes back as the problem, and a 403 or 409 as the
 * status. A 401 starts sign-in and throws, as any other failure does.
 */
export async function reassignTask(tasksPath: string, id: string, body: ReassignBody): Promise<ReassignResult> {
  const response = await apiFetch(`${tasksPath}/${id}/reassign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (response.ok) {
    // A task read holds no field values, so plain numbers are safe.
    return { ok: true, task: JSON.parse(await response.text()) as TaskItem }
  }
  if (response.status === 400) {
    const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
    return { ok: false, problem: { status: 400, title: problem.title ?? '', errors: problem.errors ?? {} } }
  }
  if (response.status === 403 || response.status === 409) {
    return { ok: false, status: response.status }
  }
  throw new Error(`Reassigning the task '${id}' of '${tasksPath}' failed with status ${response.status}.`)
}

/** The address of the user picker of the application whose task API is at `tasksPath`. */
export function usersPath(tasksPath: string): string {
  return tasksPath.replace(/\/tasks$/, '/users')
}

/**
 * Lists up to 20 users whose display name contains `search`, or the first 20 for an empty search.
 * It throws when the answer is not a success.
 */
export async function searchUsers(path: string, search: string, signal?: AbortSignal): Promise<UserOption[]> {
  const response = await apiFetch(`${path}?search=${encodeURIComponent(search)}`, { signal })
  if (!response.ok) {
    throw new Error(`Searching the users of '${path}' failed with status ${response.status}.`)
  }
  return ((await response.json()) as { items: UserOption[] }).items
}
