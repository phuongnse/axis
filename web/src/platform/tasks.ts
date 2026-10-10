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

/** What the task API answers when nobody is signed in. */
export const signInRequired = 'signInRequired'

/**
 * Loads one page of the signed-in user's open tasks, in the task API's order. `query` holds the
 * task API's `page` and `pageSize`. A 401 returns {@link signInRequired}, and any other failure throws.
 */
export async function fetchTasks(
  tasksPath: string,
  query: string,
  signal?: AbortSignal,
): Promise<TaskPage | typeof signInRequired> {
  const response = await fetch(`${tasksPath}${query ? `?${query}` : ''}`, { signal })
  if (response.status === 401) {
    return signInRequired
  }
  if (!response.ok) {
    throw new Error(`Loading the tasks of '${tasksPath}' failed with status ${response.status}.`)
  }
  return (await response.json()) as TaskPage
}
