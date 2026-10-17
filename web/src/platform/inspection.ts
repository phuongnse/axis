import { apiFetch } from './api'
import { parseRecordJson } from './records'

/** The states of a process instance, as the inspection API's `state` filter takes them. */
export const instanceStates = ['running', 'waiting', 'completed', 'failed', 'cancelled'] as const

/** The platform text of an instance state. A state this build does not know shows as the API sends it. */
export function stateText(t: (key: string) => string, state: string): string {
  return (instanceStates as readonly string[]).includes(state) ? t(`shell.inspector.states.${state}`) : state
}

/** What the inspection API answers when the signed-in user may not inspect the process. */
export const forbidden = 'forbidden'

/** An instance in the list of a process's instances. `endedAt` is `null` until it is completed or failed. */
export interface InstanceListItem {
  id: string
  subjectId: string
  state: string
  /** The step the instance runs next, or waits on. */
  step: string
  releaseId: string
  createdAt: string
  endedAt: string | null
}

/** One page of the instances of a process. */
export interface InstancePage {
  items: InstanceListItem[]
  page: number
  pageSize: number
  totalCount: number
}

/** A transition to the step `next`. `on` is a decision branch or a task outcome, `null` for an operation. */
export interface GraphTransition {
  next: string
  on: string | null
}

/** A step of the graph. `labelKey` is set only for a task step. */
export interface GraphStep {
  name: string
  type: string
  labelKey: string | null
  transitions: GraphTransition[]
}

/** One occurrence of a step in the instance's history. */
export interface PathEntry {
  step: string
  startedAt: string
  finishedAt: string
  decision: string | null
  output: unknown
  error: string | null
}

/** A task of the instance. `outcome`, `completedBy` and `completedAt` are `null` while it is open. */
export interface InstanceTask {
  id: string
  step: string
  assigneeKind: string
  assignee: string
  state: string
  outcome: string | null
  completedBy: string | null
  createdAt: string
  completedAt: string | null
}

/** An attempt or reconciliation lookup of a call. `kind` is `attempt` or `lookup`. */
export interface CallAttempt {
  kind: string
  number: number
  startedAt: string
  finishedAt: string
  outcome: string
  httpStatus: number | null
  error: string | null
}

/**
 * A connector or package call of the instance. A connector call has `connector`, and a package call
 * has `package` and `version`. `input` is `null`, and `inputHidden` true, when the user may not see it.
 * The numbers in `input` are source text, so a decimal keeps its digits.
 */
export interface InstanceCall {
  id: string
  kind: string
  step: string
  connector?: string
  package?: string
  version?: string
  operation: string
  state: string
  attempts: CallAttempt[]
  input: unknown
  inputHidden: boolean
}

/** One instance of a process, the graph of the release it is pinned to, its path, its tasks and its calls. */
export interface InstanceDetail {
  id: string
  process: string
  subjectId: string
  releaseId: string
  state: string
  step: string
  createdAt: string
  endedAt: string | null
  graph: { start: string; steps: GraphStep[] }
  path: PathEntry[]
  tasks: InstanceTask[]
  calls: InstanceCall[]
}

/**
 * Loads one page of the instances of a process, newest first. `query` holds the inspection API's
 * `state`, `page` and `pageSize`. A 403 returns {@link forbidden}. A 401 starts sign-in and throws,
 * as any other failure does.
 */
export async function fetchInstances(
  instancesPath: string,
  query: string,
  signal?: AbortSignal,
): Promise<InstancePage | typeof forbidden> {
  const response = await apiFetch(`${instancesPath}${query ? `?${query}` : ''}`, { signal })
  if (response.status === 403) {
    return forbidden
  }
  if (!response.ok) {
    throw new Error(`Loading the instances of '${instancesPath}' failed with status ${response.status}.`)
  }
  return (await response.json()) as InstancePage
}

/**
 * Loads one instance in any state, or returns `null` when the process has none with the id. A 403
 * returns {@link forbidden}. A 401 starts sign-in and throws, as any other failure does.
 */
export async function fetchInstance(
  instancesPath: string,
  id: string,
  signal?: AbortSignal,
): Promise<InstanceDetail | null | typeof forbidden> {
  const response = await apiFetch(`${instancesPath}/${id}`, { signal })
  if (response.status === 404) {
    return null
  }
  if (response.status === 403) {
    return forbidden
  }
  if (!response.ok) {
    throw new Error(`Loading the instance '${id}' of '${instancesPath}' failed with status ${response.status}.`)
  }
  // A call's input holds field values, so every number is source text. The counts are numbers again.
  const detail = parseRecordJson<InstanceDetail>(await response.text())
  return {
    ...detail,
    calls: detail.calls.map((call) => ({
      ...call,
      attempts: call.attempts.map((attempt) => ({
        ...attempt,
        number: Number(attempt.number),
        httpStatus: attempt.httpStatus === null ? null : Number(attempt.httpStatus),
      })),
    })),
  }
}
