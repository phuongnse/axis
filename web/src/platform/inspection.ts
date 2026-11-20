import { apiFetch } from './api'
import { fillText } from './fillText'
import { formatDuration } from './formatValue'
import { parseRecordJson, type RecordProblem } from './records'
import type { StatusTone } from './statusTones'

/** The states of a process instance, as the inspection API's `state` filter takes them. */
export const instanceStates = ['running', 'waiting', 'completed', 'failed', 'cancelled'] as const

/** The states of a task, see docs/reference/process-states.md#task-states. */
export const taskStates = ['open', 'completed', 'cancelled', 'expired'] as const

/** The kinds of a call's attempt, see docs/reference/process-inspection.md#attempts. */
export const attemptKinds = ['attempt', 'lookup'] as const

/** The outcomes of an attempt or a lookup, see docs/reference/process-inspection.md#attempts. */
export const attemptOutcomes = ['succeeded', 'retryable', 'unknown', 'failed', 'found', 'notFound'] as const

/** The tone of a known state, or `neutral` for a state this build does not know. */
function toneOf(tones: Record<string, StatusTone>, state: string): StatusTone {
  return Object.hasOwn(tones, state) ? tones[state] : 'neutral'
}

/** The text of a known state under `prefix`, or the state as the API sends it when this build does not know it. */
function textOf(t: (key: string) => string, prefix: string, known: readonly string[], state: string): string {
  return known.includes(state) ? t(`${prefix}${state}`) : state
}

const instanceTones: Record<(typeof instanceStates)[number], StatusTone> = {
  running: 'neutral',
  waiting: 'neutral',
  completed: 'success',
  failed: 'danger',
  cancelled: 'muted',
}

/** The tone of an instance state, see docs/reference/shared-widgets.md#which-widget. An unknown state is `neutral`. */
export function stateTone(state: string): StatusTone {
  return toneOf(instanceTones, state)
}

/** The platform text of an instance state. A state this build does not know shows as the API sends it. */
export function stateText(t: (key: string) => string, state: string): string {
  return textOf(t, 'shell.inspector.states.', instanceStates, state)
}

/**
 * The run states of a step, the vocabulary of its state on the graph, see
 * docs/reference/process-inspector-widget.md#process-graph. A step not reached has none.
 */
export const runStates = ['done', 'running', 'waitingForEvent', 'waitingForPerson', 'failed', 'cancelled'] as const

/** The run state of a step. */
export type RunState = (typeof runStates)[number]

const runTones: Record<RunState, StatusTone> = {
  done: 'success',
  running: 'neutral',
  waitingForEvent: 'neutral',
  waitingForPerson: 'attention',
  failed: 'danger',
  cancelled: 'muted',
}

/** The tone of a run state. An unknown state is `neutral`. */
export function runStateTone(state: string): StatusTone {
  return toneOf(runTones, state)
}

/** The platform text of a run state. A state this build does not know shows as sent. */
export function runStateText(t: (key: string) => string, state: string): string {
  return textOf(t, 'shell.inspector.runStates.', runStates, state)
}

const attemptTones: Record<(typeof attemptOutcomes)[number], StatusTone> = {
  succeeded: 'success',
  found: 'success',
  failed: 'danger',
  retryable: 'muted',
  notFound: 'muted',
  unknown: 'attention',
}

/** The tone of an attempt's outcome. An unknown outcome is `neutral`. */
export function attemptOutcomeTone(outcome: string): StatusTone {
  return toneOf(attemptTones, outcome)
}

/** The platform text of an attempt's outcome. An outcome this build does not know shows as sent. */
export function attemptOutcomeText(t: (key: string) => string, outcome: string): string {
  return textOf(t, 'shell.inspector.attemptOutcomes.', attemptOutcomes, outcome)
}

/** The types of a step, see docs/reference/process-inspection.md#instance-detail. */
export const stepTypes = ['decision', 'task', 'operation', 'subProcess', 'timer', 'waitEvent', 'end'] as const

/** The platform text of a step's type. A type this build does not know shows as sent. */
export function stepTypeText(t: (key: string) => string, type: string): string {
  return textOf(t, 'shell.inspector.stepTypes.', stepTypes, type)
}

/** The kinds of a start that no person made, see docs/reference/process-inspection.md#instance-detail. */
export const startKinds = ['trigger', 'endpoint', 'schedule', 'parent'] as const

/** The platform text of a start kind that no person made. A kind this build does not know shows as sent. */
export function startKindText(t: (key: string) => string, kind: string): string {
  return textOf(t, 'shell.inspector.startKinds.', startKinds, kind)
}

/**
 * The text of a process's title in the reader's language, or its name where the release gives none. The release
 * catalog answers the key, and a catalog without it answers the key itself, see docs/reference/shared-widgets.md#which-widget.
 */
export function processTitle(t: (key: string) => string, titleKey: string, name: string): string {
  const text = t(titleKey)
  return text === titleKey ? name : text
}

/** The states of a process call, see docs/reference/process-inspection.md#instance-detail. */
export const callStates = ['pending', 'sending', 'unknown', 'succeeded', 'failed', 'cancelled'] as const

const callTones: Record<(typeof callStates)[number], StatusTone> = {
  pending: 'neutral',
  sending: 'neutral',
  unknown: 'attention',
  succeeded: 'success',
  failed: 'danger',
  cancelled: 'muted',
}

/** The tone of a call state. An unknown state is `neutral`. */
export function callStateTone(state: string): StatusTone {
  return toneOf(callTones, state)
}

/** The platform text of a call state. A state this build does not know shows as the API sends it. */
export function callStateText(t: (key: string) => string, state: string): string {
  return textOf(t, 'shell.inspector.callStates.', callStates, state)
}

/** The code of a failed step or attempt with its values, see docs/reference/process-inspection.md#step-errors-in-inspection. */
export interface StepProblem {
  code: string
  values: Record<string, number>
}

/** The codes of a step problem, see docs/reference/process-inspection.md#step-errors-in-inspection. */
export const stepProblems = [
  'unavailable',
  'serverError',
  'refused',
  'timeout',
  'notSent',
  'exception',
  'expressionFailed',
  'writeRefused',
] as const

/**
 * The words of a row's error in the reader's language: the platform text of its `problem`, with `timeout`'s `seconds` as a length of time,
 * or the stored `error` for a row with no `problem` and for a code this build does not know.
 */
export function problemText(
  t: (key: string) => string,
  locale: string,
  problem: StepProblem | null | undefined,
  error: string | null,
): string | null {
  if (!problem || !(stepProblems as readonly string[]).includes(problem.code)) {
    return error
  }
  return fillText(t(`shell.stepProblem.${problem.code}`), {
    seconds: formatDuration((problem.values.seconds ?? 0) * 1000, locale, t),
  })
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
  /** The code of `error`, or `null` when the entry has none. */
  problem: StepProblem | null
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
  /** The code of `error`, or `null` when the row has none. */
  problem: StepProblem | null
  /** The trace id of the run that wrote the row, or `null` when it stored none. */
  traceId: string | null
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

/** The instance that started this one, with the sub-process step of the parent that did so. */
export interface InstanceParent {
  id: string
  process: string
  processTitleKey: string
  step: string
}

/** An instance that a sub-process step of this instance started. `endedAt` is `null` until it ends. */
export interface SubProcess {
  id: string
  process: string
  processTitleKey: string
  /** The sub-process step of this instance that started the child. */
  step: string
  state: string
  createdAt: string
  endedAt: string | null
}

/**
 * Who or what started an instance. `kind` is `person`, `trigger`, `endpoint`, `schedule` or `parent`. `name` and `id` are
 * `null` where the detail gives none, and `via` is the text key of the action's label for a person.
 */
export interface InstanceStartedBy {
  kind: string
  name: string | null
  id: string | null
  via: string | null
}

/**
 * One instance of a process, the graph of the release it is pinned to, its path, its tasks and its calls.
 * `parent` and `subProcesses` name only the instances whose process the user may inspect. `studio` is the
 * application and the resource that open the process in Studio, for a person who may author the application.
 */
export interface InstanceDetail {
  id: string
  process: string
  /** The text key of the process's title in the release the instance is pinned to. */
  processTitleKey: string
  /** The subject the instance runs for, or `null` when its process has no subject. */
  subjectId: string | null
  /** The entity of the subject record, or `null` without a subject. */
  subjectEntity: string | null
  /** The subject's display field value, or `null` without a subject or when the user may not read it. */
  subjectLabel: string | null
  releaseId: string
  /** The number of the instance's release in the release history, or `null` when the release has no history row. */
  releaseNumber: number | null
  state: string
  step: string
  createdAt: string
  endedAt: string | null
  /** Who started the instance, or `null` for an instance started before Axis recorded its starter. */
  startedBy: InstanceStartedBy | null
  studio: { app: string; resource: string } | null
  graph: { start: string; steps: GraphStep[] }
  path: PathEntry[]
  tasks: InstanceTask[]
  calls: InstanceCall[]
  parent: InstanceParent | null
  subProcesses: SubProcess[]
  /** The operator commands the signed-in user may take on the instance now, such as `cancel` and `retry`. */
  commands: string[]
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
    releaseNumber: detail.releaseNumber === null ? null : Number(detail.releaseNumber),
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
