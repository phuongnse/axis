import { apiFetch } from './api'
import { parseRecordJson, type RecordProblem } from './records'

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

/** An output the operation of a resolvable call declares. `type` is the field type, such as `text` or `decimal`. */
export interface CallOutput {
  name: string
  type: string
  required: boolean
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
  /** Whether the package operation is idempotent. Only a package call has it. */
  idempotent?: boolean
  operation: string
  state: string
  attempts: CallAttempt[]
  input: unknown
  inputHidden: boolean
  /** Whether the signed-in user may resolve the call now. The server decides. */
  resolvable: boolean
  /** The outputs the operation declares. Only a resolvable call has them. */
  outputs?: CallOutput[]
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
  /** The operator commands the signed-in user may take on the instance now, such as `cancel` and `retry`. */
  commands: string[]
}

/** The operator commands the instance page offers. */
export const instanceCommands = ['cancel', 'retry'] as const

/** An operator command the instance page offers. */
export type InstanceCommand = (typeof instanceCommands)[number]

/** The outcome of an operator command, or the problem the server reported. */
export type CommandResult = { ok: true } | { ok: false; problem: RecordProblem }

/**
 * Whether retrying the instance needs a warning. It does when a call on the instance's current step
 * has failed, and that call is a package call whose operation is not `idempotent: true`. Such an
 * operation may have acted before it failed.
 */
export function retryWarns(instance: InstanceDetail): boolean {
  return instance.calls.some(
    (call) =>
      call.step === instance.step && call.state === 'failed' && call.kind === 'package' && call.idempotent !== true,
  )
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

/**
 * Sends an operator command to an instance with the reason the audit keeps. A 400, 403, 404 or 409
 * comes back as the problem. A 401 starts sign-in and throws, as any other failure does.
 */
export async function sendCommand(
  instancesPath: string,
  id: string,
  command: InstanceCommand,
  reason: string,
): Promise<CommandResult> {
  const response = await apiFetch(`${instancesPath}/${id}/${command}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  if (response.ok) {
    return { ok: true }
  }
  if ([400, 403, 404, 409].includes(response.status)) {
    const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
    return {
      ok: false,
      problem: { status: response.status, title: problem.title ?? '', errors: problem.errors ?? {} },
    }
  }
  throw new Error(
    `Sending '${command}' to the instance '${id}' of '${instancesPath}' failed with status ${response.status}.`,
  )
}

/**
 * Sends the resolve of an `unknown` call. `body` is the text {@link buildResolveBody} wrote. A 400,
 * 403, 404 or 409 comes back as the problem. A 401 starts sign-in and throws, as any other failure does.
 */
export async function resolveCall(
  instancesPath: string,
  instanceId: string,
  callId: string,
  body: string,
): Promise<CommandResult> {
  const response = await apiFetch(`${instancesPath}/${instanceId}/calls/${callId}/resolve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body,
  })
  if (response.ok) {
    return { ok: true }
  }
  if ([400, 403, 404, 409].includes(response.status)) {
    const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
    return {
      ok: false,
      problem: { status: response.status, title: problem.title ?? '', errors: problem.errors ?? {} },
    }
  }
  throw new Error(
    `Resolving the call '${callId}' of the instance '${instanceId}' of '${instancesPath}' failed with status ${response.status}.`,
  )
}
