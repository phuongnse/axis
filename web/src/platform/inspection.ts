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

/**
 * A transition to the step `next`. `on` is a decision branch's index, `otherwise` for the fallback, a task outcome or
 * `timeout`, and `null` for an operation. `branch` is a decision branch's name. `labelKey` is the key of the label of a
 * branch or an outcome. `after` is a timeout's duration as the process file writes it, such as `PT1H`. Each is `null`
 * where it does not apply, see docs/reference/process-inspection.md#instance-detail.
 */
export interface GraphTransition {
  next: string
  on: string | null
  branch: string | null
  labelKey: string | null
  after: string | null
}

/**
 * A step of the graph. `labelKey` is the key of the step's label, or `null` when it has none. `event` is a `waitEvent`
 * step's event name, and `eventLabelKey` the key of the event's label, which is always `null` today.
 */
export interface GraphStep {
  name: string
  type: string
  labelKey: string | null
  event: string | null
  eventLabelKey: string | null
  transitions: GraphTransition[]
}

/**
 * What a run made, with each part `null` where it made none, see docs/reference/process-inspection.md#instance-detail.
 * The panel reads which part is set. What each part holds is for the Output tab.
 */
export interface RunMade {
  changes: unknown[] | null
  event: { name: string } | null
  notified: unknown | null
  created: { entity: string; id: string } | null
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
  /** What the run made. A detail from before the API sent it has none. */
  made?: RunMade
}

/**
 * A task of the instance. `outcome`, `completedBy`, `completedById` and `completedAt` are `null` while it is open.
 * `assigneeId` is a user assignee's id, or the id of the person who claimed a role task, and `null` while no one holds it.
 */
export interface InstanceTask {
  id: string
  step: string
  assigneeKind: string
  assignee: string
  assigneeId: string | null
  state: string
  outcome: string | null
  completedBy: string | null
  completedById: string | null
  createdAt: string
  dueAt: string | null
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

/** A named value of an instance's input or result. `value` is source text for a number, and `hidden` marks a value the reader may not read. */
export interface InstanceValue {
  name: string
  value: unknown
  hidden: boolean
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
  /** The time the timer step that the instance waits on goes on, or `null` when the instance does not wait on a timer. */
  timerDueAt: string | null
  createdAt: string
  endedAt: string | null
  /** Who started the instance, or `null` for an instance started before Axis recorded its starter. */
  startedBy: InstanceStartedBy | null
  /** The parameters the instance started with, or `null` for a process with a subject. */
  input: InstanceValue[] | null
  /** The end step and the values the instance ended with once it is completed, else `null`. `values` is `null` for an instance that completed before the engine kept them. */
  result: { step: string; values: InstanceValue[] | null } | null
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

/** One run of a step: the rows of the path that belong together, numbered among the runs of the same step. */
export interface StepRun {
  step: string
  /** The run's number among the runs of its step, from 1. */
  number: number
  state: RunState
  entries: PathEntry[]
  startedAt: string | null
  /** When the run ended, or `null` while it runs or waits. */
  finishedAt: string | null
}

/** The runs of an instance, the state of each step's latest run and the ids of the transitions taken. */
export interface InstanceRuns {
  runs: StepRun[]
  runStates: Map<string, RunState>
  taken: Set<string>
}

/** The id of a step's transition on the graph, from the step's name and the transition's place among its transitions. */
export function transitionId(step: string, index: number): string {
  return `${step}|${index}`
}

/**
 * The runs of an instance, worked out from its path, see docs/reference/process-inspector-widget.md#process-graph. A run
 * starts at a path row whose step differs from the row before, or that follows a failed row of the same step, so a
 * task's created row and its completion row are one run. A run is failed when its last row has an error. The run at the
 * instance's current step takes the instance's state and has no end, and every other run is done. A current step with no
 * row yet has an open run of its own. A transition is taken when a run of its step is followed by a run of its target:
 * the only one to that target, else the one whose `on` is the run's last `decision`, else the first.
 */
export function stepRuns(instance: InstanceDetail): InstanceRuns {
  const grouped: PathEntry[][] = []
  for (const entry of instance.path) {
    const last = grouped.at(-1)
    const before = last?.at(-1)
    if (last && before && before.step === entry.step && !before.error) {
      last.push(entry)
    } else {
      grouped.push([entry])
    }
  }
  const types = new Map(instance.graph.steps.map((step) => [step.name, step.type]))
  const open = (step: string): RunState => {
    switch (instance.state) {
      case 'running':
        return 'running'
      case 'waiting':
        return types.get(step) === 'task' ? 'waitingForPerson' : 'waitingForEvent'
      case 'failed':
        return 'failed'
      case 'cancelled':
        return 'cancelled'
      default:
        return 'done'
    }
  }
  const counts = new Map<string, number>()
  const runs = grouped.map((entries, index): StepRun => {
    const step = entries[0]!.step
    const number = (counts.get(step) ?? 0) + 1
    counts.set(step, number)
    const last = entries.at(-1)!
    const current = index === grouped.length - 1 && step === instance.step
    const state: RunState = current ? open(step) : last.error ? 'failed' : 'done'
    const waiting = state === 'running' || state === 'waitingForEvent' || state === 'waitingForPerson'
    return {
      step,
      number,
      state,
      entries,
      startedAt: entries[0]!.startedAt,
      finishedAt: waiting ? null : last.finishedAt,
    }
  })
  if (runs.at(-1)?.step !== instance.step) {
    runs.push({
      step: instance.step,
      number: (counts.get(instance.step) ?? 0) + 1,
      state: open(instance.step),
      entries: [],
      startedAt: null,
      finishedAt: null,
    })
  }

  const runStates = new Map(runs.map((run) => [run.step, run.state]))
  const taken = new Set<string>()
  runs.slice(1).forEach((run, index) => {
    const from = runs[index]!
    const place = takenPlace(instance, from, run.step)
    if (place !== undefined) {
      taken.add(transitionId(from.step, place))
    }
  })
  return { runs, runStates, taken }
}

/**
 * The place among its step's transitions of the one `run` took to the step `next`: the only one to that target, else the
 * one whose `on` is the run's last `decision`, else the first. `undefined` when the step has none to `next`.
 */
function takenPlace(instance: InstanceDetail, run: StepRun, next: string): number | undefined {
  const transitions = instance.graph.steps.find((step) => step.name === run.step)?.transitions ?? []
  const leading = transitions.flatMap((transition, place) => (transition.next === next ? [place] : []))
  const decision = run.entries.at(-1)?.decision
  return leading.length === 1
    ? leading[0]
    : (leading.find((candidate) => transitions[candidate]!.on === decision) ?? leading[0])
}

/** The transition `run` took to the step `next`, chosen as {@link stepRuns} chooses the transitions taken. */
export function takenTransition(instance: InstanceDetail, run: StepRun, next: string): GraphTransition | undefined {
  const place = takenPlace(instance, run, next)
  return place === undefined
    ? undefined
    : instance.graph.steps.find((step) => step.name === run.step)?.transitions[place]
}

/** The tasks, calls and sub-processes of one run, see {@link runParts}. */
export interface RunParts {
  tasks: InstanceTask[]
  calls: InstanceCall[]
  subProcesses: SubProcess[]
}

/**
 * The tasks, calls and sub-processes that belong to `run`, see docs/reference/process-inspector-widget.md#instance-page.
 * The detail does not link them to a path row, so each is matched by its step and its time: it belongs to the run of its
 * step that began before it and after the step's previous run ended, both ends exclusive. A task or a sub-process uses
 * its `createdAt`, and a call its first attempt's start. A call with no attempts yet belongs to the step's last run.
 */
export function runParts(instance: InstanceDetail, runs: readonly StepRun[], run: StepRun): RunParts {
  const own = runs.filter((candidate) => candidate.step === run.step)
  const place = own.indexOf(run)
  const after = own[place - 1]?.finishedAt ?? null
  const before = own[place + 1]?.startedAt ?? null
  const inside = (time: string | undefined): boolean => {
    if (time === undefined) {
      return place === own.length - 1
    }
    const at = Date.parse(time)
    return (after === null || at > Date.parse(after)) && (before === null || at < Date.parse(before))
  }
  return {
    tasks: instance.tasks.filter((task) => task.step === run.step && inside(task.createdAt)),
    calls: instance.calls.filter((call) => call.step === run.step && inside(call.attempts[0]?.startedAt)),
    subProcesses: instance.subProcesses.filter((child) => child.step === run.step && inside(child.createdAt)),
  }
}

/**
 * The words of why a run failed, in the reader's language, see docs/reference/process-inspector-widget.md#instance-page:
 * the last row of the run with a `problem` or an `error`, else the last attempt with one of the run's last failed call.
 * `null` when the run holds neither.
 */
export function runProblem(
  t: (key: string) => string,
  locale: string,
  instance: InstanceDetail,
  runs: readonly StepRun[],
  run: StepRun,
): string | null {
  const entry = run.entries.findLast((row) => row.problem || row.error)
  if (entry) {
    return problemText(t, locale, entry.problem, entry.error)
  }
  const call = runParts(instance, runs, run).calls.findLast((row) => row.state === 'failed')
  const attempt = call?.attempts.findLast((row) => row.problem || row.error)
  return attempt ? problemText(t, locale, attempt.problem, attempt.error) : null
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
