import type { StepProblem } from './stepProblems'
import { apiFetch } from './api'
import { parseRecordJson, type RecordProblem } from './records'

/** The states of a process instance, as the inspection API's `state` filter takes them. */
export const instanceStates = ['running', 'waiting', 'completed', 'failed', 'cancelled'] as const

/** The states of a task, see docs/reference/process-states.md#task-states. */
export const taskStates = ['open', 'completed', 'cancelled', 'expired'] as const

/** The kinds of a call's attempt, see docs/reference/process-inspection.md#attempts. */
export const attemptKinds = ['attempt', 'lookup'] as const

/** The outcomes of an attempt or a lookup, see docs/reference/process-inspection.md#attempts. */
export const attemptOutcomes = ['succeeded', 'retryable', 'unknown', 'failed', 'found', 'notFound'] as const

/** The platform text of an instance state. A state this build does not know shows as the API sends it. */
export function stateText(t: (key: string) => string, state: string): string {
  return (instanceStates as readonly string[]).includes(state) ? t(`shell.inspector.states.${state}`) : state
}

/**
 * The process and the instance an instance detail path names, such as
 * `/api/apps/StepApp/processes/Submit/instances/{id}`, or `null` when the path is not one.
 */
export function inspectTarget(inspectPath: string): { process: string; instanceId: string } | null {
  const match = /\/processes\/([^/]+)\/instances\/([^/]+)$/.exec(inspectPath)
  return match ? { process: decodeURIComponent(match[1]), instanceId: decodeURIComponent(match[2]) } : null
}

/** What the inspection API answers when the signed-in user may not inspect the process. */
export const forbidden = 'forbidden'

/** An instance in the list of a process's instances. `endedAt` is `null` until it is completed or failed. */
export interface InstanceListItem {
  id: string
  /** The subject the instance runs for, or `null` when its process has no subject. */
  subjectId: string | null
  /**
   * Proposed, see the review on #3650: the subject record with its entity and its display field's value, as a task's
   * subject is, so the page links it, or `null` when the process has no subject.
   */
  subject?: { entity: string; id: string; label: string | null } | null
  /** The subject's display field value, or `null` without a subject or when the user may not read it. */
  subjectLabel: string | null
  state: string
  /** The step the instance runs next, or waits on. */
  step: string
  releaseId: string
  /** The number of the instance's release in the release history, or `null` when the release has no history row. */
  releaseNumber: number | null
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
  /**
   * Proposed, see the review on #3650: the text key of the transition's label, a task outcome's label or a decision
   * branch's, from the instance's release, or `null` when it has none.
   */
  labelKey?: string | null
}

/** A step of the graph. `labelKey` is set only for a task step. */
export interface GraphStep {
  name: string
  type: string
  labelKey: string | null
  transitions: GraphTransition[]
  /**
   * Proposed, see the review on #3650: the step as the release the instance is pinned to defines it, its JSON without
   * `name`, `type` and `label`, such as a decision's branches, a task's assignee and outcomes, or a call's mapping and
   * retry. A connector call also has `endpoint`, its operation's method and path from the connector resource. It holds
   * expressions and names, never a value.
   */
  definition?: StepDefinition
  /**
   * Proposed, see D155 and the review on #3650: a `parallel` block's branches, each named, with its label's text key,
   * and the rule that makes the block done, such as `all`.
   */
  parallel?: { branches: { name: string; labelKey: string | null }[]; completion: string }
  /** Proposed, see D155 and the review on #3650: the block and branch a step sits in, when it sits in one. */
  inBlock?: { block: string; branch: string }
}

/**
 * Proposed, see D155 and the review on #3650: a path of an instance that is running, waiting or failed at a step. An
 * instance has one path, or one for each branch of a block it is in.
 */
export interface InstancePath {
  id: string
  step: string
  state: string
}

/** One occurrence of a step in the instance's history. */
/**
 * Proposed, see the review on #3650: what a step's run made or changed, with its values, for the Output of its run:
 * the fields it changed on the record, before and after, an event it raised, the people it notified, and a record it
 * created. A value the reader may not read comes as `null` with `hidden`.
 */
export interface RunOutput {
  changes?: { field: string; labelKey: string | null; before: string | null; after: string | null; hidden?: boolean }[]
  event?: { name: string; labelKey: string | null; fields: Record<string, string> }
  notified?: { to: string[]; channels: string[]; subject: string }
  created?: { entity: string; id: string; label: string | null }
}

export interface PathEntry {
  /** Proposed, see D155 and the review on #3650: the path that ran the step, when the instance had several. */
  path?: string
  /** Proposed, see the review on #3650: what the run made or changed. */
  made?: RunOutput
  /**
   * Proposed, see the review on #3650: the values a decision's conditions read when it ran, as Camunda Operate shows a
   * decision's inputs. A value the reader may not read comes as `null` with `hidden`.
   */
  read?: { field: string; labelKey: string | null; value: string | null; hidden?: boolean }[]
  step: string
  startedAt: string
  finishedAt: string
  decision: string | null
  output: unknown
  error: string | null
  /** Proposed, see the review on #3650: the error as a code and its values, which the reader sees in their language. */
  problem?: StepProblem | null
  /** The trace id of the run that wrote the entry, or `null` when it stored none. */
  traceId: string | null
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
  /** Proposed, see the review on #3650: when the task is due, from its step's `dueIn`, or `null` without one. */
  dueAt?: string | null
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
  /** Proposed, see the review on #3650: the error as a code and its values, which the reader sees in their language. */
  problem?: StepProblem | null
  /** The trace id of the run that wrote the row, or `null` when it stored none. */
  traceId: string | null
  /** Proposed, see the review on #3650: the answer to a connector call's attempt, `null` when none came back. */
  response?: CallResponse | null
}

/** Proposed, see the review on #3650: a step's definition, see `GraphStep.definition`. */
export type StepDefinition = Record<string, unknown> & { endpoint?: { method: string; path: string } }

/**
 * Proposed, see the review on #3650: the request a connector call sent, kept as its input is and shown under the same
 * masking rule, with every secret, such as an `Authorization` header or a `secret` binding's value, as `[secret]`.
 */
export interface CallRequest {
  method: string
  url: string
  headers: Record<string, string>
  /** The body as it was sent, its text unchanged, or `null` for none. */
  body: string | null
}

/**
 * Proposed, see the review on #3650: what the external system answered an attempt, under the input's masking rule,
 * with secrets as `[secret]`. A body over the size limit is cut, and `truncated` says so.
 */
export interface CallResponse {
  status: number
  headers: Record<string, string>
  /** The body as it came back, its text unchanged, or `null` for none. */
  body: string | null
  truncated: boolean
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
  /** Proposed, see the review on #3650: the request a connector call sent, `null` while hidden or not kept. */
  request?: CallRequest | null
  /** Proposed, see the review on #3650: the values a package operation gave back, `null` while hidden. */
  returned?: Record<string, unknown> | null
  /** Whether the signed-in user may resolve the call now. The server decides. */
  resolvable: boolean
  /** The outputs the operation declares. Only a resolvable call has them. */
  outputs?: CallOutput[]
}

/** The instance that started this one, with the sub-process step of the parent that did so. */
export interface InstanceParent {
  id: string
  process: string
  step: string
}

/** An instance that a sub-process step of this instance started. `endedAt` is `null` until it ends. */
export interface SubProcess {
  id: string
  process: string
  /** The sub-process step of this instance that started the child. */
  step: string
  state: string
  createdAt: string
  endedAt: string | null
}

/**
 * One instance of a process, the graph of the release it is pinned to, its path, its tasks and its calls.
 * `parent` and `subProcesses` name only the instances whose process the user may inspect.
 */
export interface InstanceDetail {
  id: string
  process: string
  /** Proposed, see the review on #3650: the text key of the process's title, from the instance's release. */
  processTitleKey?: string | null
  /** The subject the instance runs for, or `null` when its process has no subject. */
  subjectId: string | null
  /**
   * Proposed, see the review on #3650: the subject record with its entity and its display field's value, as a task's
   * subject is, so the page links it, or `null` when the process has no subject.
   */
  subject?: { entity: string; id: string; label: string | null } | null
  /**
   * Proposed, see the review on #3650: who or what started the instance: a person, with the action they took, a
   * trigger, an endpoint, a schedule, or the instance of a parent process.
   */
  startedBy?: { kind: 'user' | 'trigger' | 'endpoint' | 'schedule' | 'parent'; name: string; via?: string | null }
  /** Proposed, see the review on #3650: the parameters the instance started with, under the masking rule. */
  input?: Record<string, unknown> | null
  /** Proposed, see the review on #3650: the end step an ended instance reached, and the values it ended with. */
  result?: { step: string; values: Record<string, unknown> } | null
  /**
   * Proposed, see D155 and the review on #3650: every path the instance is at, when it is at more than one step; `step`
   * then names the first.
   */
  paths?: InstancePath[]
  releaseId: string
  /** Proposed, see the review on #3650: the number of the release the instance is pinned to, as the list has it. */
  releaseNumber?: number | null
  state: string
  step: string
  createdAt: string
  endedAt: string | null
  graph: { start: string; steps: GraphStep[] }
  path: PathEntry[]
  tasks: InstanceTask[]
  calls: InstanceCall[]
  parent: InstanceParent | null
  subProcesses: SubProcess[]
  /** The operator commands the signed-in user may take on the instance now, such as `cancel` and `retry`. */
  commands: string[]
  /**
   * Proposed, see the review on #3650: where the instance's process is in Studio, its application and resource id, so a
   * step opens in the process designer. The server sends it only to a person who may author the application.
   */
  studio?: { application: string; resource: string } | null
}

/**
 * When the timer the instance waits at fires, or `null` when the instance is not waiting at a timer
 * step. The time is the `dueAt` of the step's latest history entry.
 */
export function timerDueAt(instance: InstanceDetail): string | null {
  if (
    instance.state !== 'waiting' ||
    instance.graph.steps.find((step) => step.name === instance.step)?.type !== 'timer'
  ) {
    return null
  }
  const entry = instance.path.findLast((candidate) => candidate.step === instance.step)
  const output = entry?.output
  if (typeof output !== 'object' || output === null || !('dueAt' in output)) {
    return null
  }
  return typeof output.dueAt === 'string' ? output.dueAt : null
}

/** The operator commands the instance page offers. */
export const instanceCommands = ['cancel', 'retry'] as const

/** An operator command the instance page offers. */
export type InstanceCommand = (typeof instanceCommands)[number]

/** The outcome of an operator command, or the problem the server reported. */
export type CommandResult = { ok: true } | { ok: false; problem: RecordProblem }

/** The operator commands the dead letter list offers. */
export const deadLetterCommands = ['redeliver', 'discard'] as const

/** An operator command the dead letter list offers. */
export type DeadLetterCommand = (typeof deadLetterCommands)[number]

/**
 * A dead letter in the list of a process's dead letters. The ids that its kind lacks are `null`.
 * `commands` holds the operator commands the signed-in user may take on it now. The server decides.
 */
export interface DeadLetterItem {
  id: string
  kind: string
  eventId: string | null
  triggerId: string | null
  processInstanceId: string | null
  emailId: string | null
  webhookId: string | null
  occurrenceAt: string | null
  attempts: number
  lastError: string
  state: string
  reason: string | null
  settledBy: string | null
  createdAt: string
  settledAt: string | null
  commands: string[]
}

/** One page of the dead letters of a process. */
export interface DeadLetterPage {
  items: DeadLetterItem[]
  page: number
  pageSize: number
  totalCount: number
}

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
 * `state`, `page` and `pageSize`. A 403 returns {@link forbidden}. A 401 shows the sign-in prompt and throws,
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
 * returns {@link forbidden}. A 401 shows the sign-in prompt and throws, as any other failure does.
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

/** Reads a 400, 403, 404 or 409 answer as the problem, or returns `null` for any other status. */
async function readProblem(response: Response): Promise<CommandResult | null> {
  if (![400, 403, 404, 409].includes(response.status)) {
    return null
  }
  const problem = JSON.parse(await response.text()) as Partial<RecordProblem>
  return {
    ok: false,
    problem: { status: response.status, title: problem.title ?? '', errors: problem.errors ?? {} },
  }
}

/**
 * Sends an operator command to an instance with the reason the audit keeps. A 400, 403, 404 or 409
 * comes back as the problem. A 401 shows the sign-in prompt and throws, as any other failure does.
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
  const problem = await readProblem(response)
  if (problem) {
    return problem
  }
  throw new Error(
    `Sending '${command}' to the instance '${id}' of '${instancesPath}' failed with status ${response.status}.`,
  )
}

/**
 * Loads one page of the dead letters of a process, newest first. `query` holds the dead letter API's
 * `state`, `page` and `pageSize`. A 403 returns {@link forbidden}. A 401 shows the sign-in prompt and throws,
 * as any other failure does.
 */
export async function fetchDeadLetters(
  deadLettersPath: string,
  query: string,
  signal?: AbortSignal,
): Promise<DeadLetterPage | typeof forbidden> {
  const response = await apiFetch(`${deadLettersPath}${query ? `?${query}` : ''}`, { signal })
  if (response.status === 403) {
    return forbidden
  }
  if (!response.ok) {
    throw new Error(`Loading the dead letters of '${deadLettersPath}' failed with status ${response.status}.`)
  }
  return (await response.json()) as DeadLetterPage
}

/**
 * Sends an operator command to a dead letter with the reason the audit keeps. A 400, 403, 404 or 409
 * comes back as the problem. A 401 shows the sign-in prompt and throws, as any other failure does.
 */
export async function sendDeadLetterCommand(
  deadLettersPath: string,
  id: string,
  command: DeadLetterCommand,
  reason: string,
): Promise<CommandResult> {
  const response = await apiFetch(`${deadLettersPath}/${id}/${command}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  if (response.ok) {
    return { ok: true }
  }
  const problem = await readProblem(response)
  if (problem) {
    return problem
  }
  throw new Error(
    `Sending '${command}' to the dead letter '${id}' of '${deadLettersPath}' failed with status ${response.status}.`,
  )
}

/**
 * Sends the resolve of an `unknown` call. `body` is the text {@link buildResolveBody} wrote. A 400,
 * 403, 404 or 409 comes back as the problem. A 401 shows the sign-in prompt and throws, as any other failure does.
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
  const problem = await readProblem(response)
  if (problem) {
    return problem
  }
  throw new Error(
    `Resolving the call '${callId}' of the instance '${instanceId}' of '${instancesPath}' failed with status ${response.status}.`,
  )
}
