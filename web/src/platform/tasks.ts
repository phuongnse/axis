import { apiFetch } from './api'
import type { RecordProblem } from './records'
import type { EntityMetadata, FormMetadata } from './site'

/** The subject record of a task. `label` is its display field value, or `null` when there is none. */
export interface TaskSubject {
  entity: string
  id: string
  label: string | null
}

/** An open task as the task API lists it. `dueAt` is `null` when the step has no `dueIn`. */
export interface TaskListItem {
  id: string
  process: string
  step: string
  labelKey: string
  subject: TaskSubject
  dueAt: string | null
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

/**
 * One task in any state, as the task API reads it. `assignee` has the user id or the role name, and
 * the other is `null`. `completedAt`, `completedBy` and `outcome` are `null` while the task is open.
 */
export interface TaskItem {
  id: string
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
  createdAt: string
  completedAt: string | null
  completedBy: string | null
  outcome: string | null
}

/** The outcome of completing a task, or the problem the server reported. */
export type CompleteResult = { ok: true } | { ok: false; problem: RecordProblem }

/** What the task API answers when nobody is signed in. */
export const signInRequired = 'signInRequired'

/** What the task API answers when the signed-in user may not act on the task. */
export const assignedElsewhere = 'assignedElsewhere'

/**
 * Loads one page of the signed-in user's open tasks, in the task API's order. `query` holds the
 * task API's `page` and `pageSize`. A 401 returns {@link signInRequired}, and any other failure throws.
 */
export async function fetchTasks(
  tasksPath: string,
  query: string,
  signal?: AbortSignal,
): Promise<TaskPage | typeof signInRequired> {
  const response = await apiFetch(`${tasksPath}${query ? `?${query}` : ''}`, { signal })
  if (response.status === 401) {
    return signInRequired
  }
  if (!response.ok) {
    throw new Error(`Loading the tasks of '${tasksPath}' failed with status ${response.status}.`)
  }
  return (await response.json()) as TaskPage
}

/**
 * Loads one task in any state, or returns `null` when the task API has no task with the id. A 401
 * returns {@link signInRequired}, a 403 returns {@link assignedElsewhere}, and any other failure throws.
 */
export async function fetchTask(
  tasksPath: string,
  id: string,
  signal?: AbortSignal,
): Promise<TaskItem | null | typeof signInRequired | typeof assignedElsewhere> {
  const response = await apiFetch(`${tasksPath}/${id}`, { signal })
  if (response.status === 404) {
    return null
  }
  if (response.status === 401) {
    return signInRequired
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
 * A 400, 403 or 409 comes back as the problem, and any other failure throws.
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
