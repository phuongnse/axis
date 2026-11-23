import type { StepMark } from '../platform/StepsBar'
import type { StatusTone } from '../platform/StatusTag'
import type {
  AuthoringRequest,
  AuthoringRequestChecks,
  AuthoringRequestReviewChecks,
  AuthoringRequestState,
  AuthoringRequestStep,
  AuthoringRequestStepResource,
  StudioScenarioResult,
} from '../platform/studio'

/** The columns the AI requests table's search, filter and sort can name, see docs/reference/authoring-requests.md#routes. */
export const requestFields = [
  { name: 'title', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'author', type: 'text' },
  { name: 'sentAt', type: 'date' },
] as const

/** The word and the tone of each state, from the states in docs/reference/ai-authoring-in-studio.md#the-states. */
export const authoringRequestStates: Record<AuthoringRequestState, { word: string; tone: StatusTone }> = {
  planning: { word: 'shell.studio.aiRequest.state.planning', tone: 'neutral' },
  awaitingApproval: { word: 'shell.studio.aiRequest.state.awaitingApproval', tone: 'attention' },
  working: { word: 'shell.studio.aiRequest.state.working', tone: 'neutral' },
  fixing: { word: 'shell.studio.aiRequest.state.working', tone: 'neutral' },
  checking: { word: 'shell.studio.aiRequest.state.checking', tone: 'neutral' },
  reviewing: { word: 'shell.studio.aiRequest.state.reviewing', tone: 'neutral' },
  ready: { word: 'shell.studio.aiRequest.state.ready', tone: 'attention' },
  accepted: { word: 'shell.studio.aiRequest.state.accepted', tone: 'success' },
  dropped: { word: 'shell.studio.aiRequest.state.dropped', tone: 'muted' },
  failed: { word: 'shell.studio.aiRequest.state.failed', tone: 'danger' },
}

/** The states in which a request waits for its author or ends, so the page stops asking for it. */
export const endedAuthoringRequestStates: ReadonlySet<AuthoringRequestState> = new Set<AuthoringRequestState>([
  'awaitingApproval',
  'ready',
  'accepted',
  'dropped',
  'failed',
])

/** The longest name the heading shows before its ellipsis. */
const maxNameLength = 80

/**
 * The name of a request: its first sentence, or its first line when no sentence ends first, cut at 80 characters with an
 * ellipsis. A sentence ends at a `.`, `!` or `?` that a space or the end follows, and keeps its mark.
 */
export function requestName(text: string): string {
  const trimmed = text.trim()
  const sentenceEnd = /[.!?](?=\s|$)/.exec(trimmed)
  const lineBreak = /\r?\n/.exec(trimmed)
  const end = Math.min(
    sentenceEnd === null ? Infinity : sentenceEnd.index + 1,
    lineBreak === null ? Infinity : lineBreak.index,
  )
  const sentence = trimmed.slice(0, end).trimEnd()
  return sentence.length > maxNameLength ? `${sentence.slice(0, maxNameLength).trimEnd()}…` : sentence
}

/**
 * What a request is called on the screens: the title AI gave it with its plan, or the first sentence of its words until then,
 * see docs/reference/ai-authoring-in-studio.md#the-heading.
 */
export function requestTitle(request: Pick<AuthoringRequest, 'title' | 'text'>): string {
  return request.title ?? requestName(request.text)
}

/** The Checks line of a request: its verdict, and the text key of its summary with the values that fill the key. */
export interface ChecksResult {
  passed: boolean
  summaryKey: string
  values: Record<string, number>
  checks: AuthoringRequestChecks
}

/**
 * The Review line of a request. `summaryKey` names the text of a review that stored no text, `notReviewed` only, and
 * `findings` is the text a review with findings stored.
 */
export interface ReviewResult {
  verdict: 'passed' | 'findings' | 'notReviewed'
  tone: StatusTone
  summaryKey: string | null
  findings: string | null
}

/** What the Result item of a request shows, see docs/reference/ai-authoring-in-studio.md#checks-and-review. */
export interface RequestResult {
  checks: ChecksResult | null
  review: ReviewResult | null
  /** The text key of why a failed request failed, and null on any other state. */
  failureKey: string | null
}

/** The words that say why a request failed, keyed by a step's kind and outcome, or by its outcome alone. */
const failureKeys: Record<string, string> = {
  'check:failed': 'shell.studio.aiRequest.failure.checks',
  'review:findings': 'shell.studio.aiRequest.failure.review',
  overBudget: 'shell.studio.aiRequest.failure.overBudget',
  timedOut: 'shell.studio.aiRequest.failure.timedOut',
  tokenEnded: 'shell.studio.aiRequest.failure.tokenEnded',
  noPlan: 'shell.studio.aiRequest.failure.noPlan',
  notAccepted: 'shell.studio.aiRequest.failure.notAccepted',
}

/** The Checks line of one `check` step: passed when the step passed, and a summary from the first failing check. */
function checksResult(step: AuthoringRequestStep): ChecksResult {
  const checks = step.checks as AuthoringRequestChecks
  const passed = step.outcome === 'passed'
  const { compile, packages, scenarios } = checks
  if (compile.outcome === 'failed') {
    const count = compile.diagnostics.filter((diagnostic) => diagnostic.severity === 'error').length
    return count === 1
      ? { passed, summaryKey: 'shell.studio.aiRequest.checks.compileOne', values: {}, checks }
      : { passed, summaryKey: 'shell.studio.aiRequest.checks.compile', values: { count }, checks }
  }
  if (packages.outcome === 'failed') {
    return { passed, summaryKey: 'shell.studio.aiRequest.checks.packages', values: {}, checks }
  }
  // A skipped contract scenario is neither passed nor failed, so it counts in neither.
  const run = scenarios.items.filter((item): item is StudioScenarioResult => item.outcome !== 'skipped')
  const failed = run.filter((item) => item.outcome === 'failed').length
  if (failed > 0 && run.length === 1) {
    return { passed, summaryKey: 'shell.studio.aiRequest.checks.scenarioFails', values: {}, checks }
  }
  if (failed > 0) {
    return {
      passed,
      summaryKey: 'shell.studio.aiRequest.checks.somePass',
      values: { passed: run.length - failed, total: run.length },
      checks,
    }
  }
  if (scenarios.outcome === 'failed') {
    return { passed, summaryKey: 'shell.studio.aiRequest.checks.runFailed', values: {}, checks }
  }
  return { passed, summaryKey: 'shell.studio.aiRequest.checks.allPass', values: {}, checks }
}

/**
 * The Review line of a request after its checks: the newest review that gave a verdict. A ready or accepted request with
 * no such review was not reviewed. A failed one shows no Review line.
 */
function reviewResult(
  request: AuthoringRequest,
  isAuthor: boolean,
  afterChecks: AuthoringRequestStep[],
): ReviewResult | null {
  const verdict = afterChecks.findLast(
    (step) => step.kind === 'review' && (step.outcome === 'passed' || step.outcome === 'findings'),
  )
  if (verdict !== undefined) {
    return verdict.outcome === 'passed'
      ? { verdict: 'passed', tone: 'success', summaryKey: null, findings: null }
      : {
          verdict: 'findings',
          tone: 'attention',
          summaryKey: null,
          findings: (verdict.checks as AuthoringRequestReviewChecks | null)?.review.findings ?? null,
        }
  }
  if (request.state !== 'ready' && request.state !== 'accepted') {
    return null
  }
  return {
    verdict: 'notReviewed',
    tone: 'muted',
    summaryKey: isAuthor
      ? 'shell.studio.aiRequest.review.notReviewedAuthor'
      : 'shell.studio.aiRequest.review.notReviewedOther',
    findings: null,
  }
}

/**
 * Why a failed request failed: the newest step with an outcome names the reason. A failed request with an outcome that
 * has no words of its own shows the general reason. Any other state has no reason.
 */
function failureKeyOf(request: AuthoringRequest, steps: AuthoringRequestStep[]): string | null {
  if (request.state !== 'failed') {
    return null
  }
  const ended = steps.findLast((step) => step.outcome !== null)
  if (ended === undefined) {
    return 'shell.studio.aiRequest.failure.unfinished'
  }
  return (
    failureKeys[`${ended.kind}:${ended.outcome}`] ??
    failureKeys[ended.outcome!] ??
    'shell.studio.aiRequest.failure.unfinished'
  )
}

/**
 * What the Result item of a request shows, from its steps. The Checks line is the newest `check` step that has checks, and
 * the Review line is the newest review after it. `isAuthor` picks the text of a review that was not reviewed. See
 * docs/reference/ai-authoring-in-studio.md#checks-and-review.
 */
export function requestResult(request: AuthoringRequest, isAuthor: boolean): RequestResult {
  const steps = request.steps ?? []
  const checkIndex = steps.findLastIndex((step) => step.kind === 'check' && step.checks !== null)
  const check = checkIndex < 0 ? null : checksResult(steps[checkIndex])
  return {
    checks: check,
    review: reviewResult(request, isAuthor, steps.slice(checkIndex + 1)),
    failureKey: failureKeyOf(request, steps),
  }
}

/** An item of a request's feed, see docs/reference/ai-authoring-in-studio.md#the-feed. */
export interface FeedEntry {
  key: 'plan' | 'work' | 'result' | 'dropped'
  mark: StepMark
}

const runningStates: ReadonlySet<AuthoringRequestState> = new Set<AuthoringRequestState>([
  'working',
  'fixing',
  'checking',
  'reviewing',
])

/** The kinds of the steps that make up a request's work. The plan, a changed plan and the accept are not part of it. */
const workKinds: ReadonlySet<string> = new Set(['work', 'write', 'fix', 'check', 'review'])

/** The mark of the Plan item, from the request's state and whether its plan was approved. */
function planMark(request: AuthoringRequest): StepMark {
  if (request.state === 'planning') return 'now'
  if (request.state === 'awaitingApproval') return 'waiting'
  if (request.approvedAt !== null) return 'done'
  return request.state === 'failed' ? 'stopped' : 'idle'
}

/** The mark of the Work item, which shows once the plan is approved. */
function workMark(request: AuthoringRequest): StepMark {
  if (runningStates.has(request.state)) return 'now'
  if (request.state === 'ready' || request.state === 'accepted') return 'done'
  return request.state === 'failed' ? 'stopped' : 'idle'
}

/**
 * The items of a request's feed in order, each with its mark, see
 * docs/reference/ai-authoring-in-studio.md#the-feed. Plan shows while AI writes it, once it exists and when the request
 * failed before it was approved. Work shows once the plan is approved. Result shows on an accepted request and on a ready
 * or failed one that has checks. Dropped shows on a dropped request.
 */
export function requestFeed(request: AuthoringRequest): FeedEntry[] {
  const feed: FeedEntry[] = []
  const approved = request.approvedAt !== null
  if (request.state === 'planning' || request.plan !== null || (request.state === 'failed' && !approved)) {
    feed.push({ key: 'plan', mark: planMark(request) })
  }
  if (approved) {
    feed.push({ key: 'work', mark: workMark(request) })
  }
  const hasChecks =
    (request.state === 'ready' || request.state === 'failed') && requestResult(request, false).checks !== null
  if (request.state === 'accepted' || hasChecks) {
    feed.push({
      key: 'result',
      mark: request.state === 'ready' ? 'waiting' : request.state === 'accepted' ? 'done' : 'idle',
    })
  }
  if (request.state === 'dropped') {
    feed.push({ key: 'dropped', mark: 'idle' })
  }
  return feed
}

/**
 * The line of the Work item, see docs/reference/ai-authoring-in-studio.md#the-work-item. While a step runs it is the key
 * of what runs now, in present words. Otherwise it is the count of the work's steps and their duration in milliseconds,
 * from the first start to the last finish, and null when the request has no such step.
 */
export type WorkLine = { key: string } | { count: number; ms: number } | null

export function workLine(request: AuthoringRequest): WorkLine {
  if (runningStates.has(request.state)) {
    return { key: `shell.studio.aiRequest.work.${request.state}` }
  }
  const steps = (request.steps ?? []).filter((step) => workKinds.has(step.kind))
  if (steps.length === 0) {
    return null
  }
  const starts = steps.map((step) => Date.parse(step.startedAt))
  const finishes = steps.flatMap((step) => (step.finishedAt === null ? [] : [Date.parse(step.finishedAt)]))
  const ms = finishes.length === 0 ? 0 : Math.max(0, Math.max(...finishes) - Math.min(...starts))
  return { count: steps.length, ms }
}

/** The comments that asked for another plan, oldest first: the summary of each `planChange` step. */
export function planChanges(request: AuthoringRequest): string[] {
  return (request.steps ?? []).flatMap((step) =>
    step.kind === 'planChange' && step.summary !== null && step.summary !== '' ? [step.summary] : [],
  )
}

/**
 * A row of a request's History, one step or a group of writes, see docs/reference/ai-authoring-in-studio.md#the-history. `step`
 * and `result` are the text keys with the values that fill them, and each is null where the step has no words of its own. A
 * write row holds its `resource`, and a group row holds its writes, newest first, in `rows`.
 */
export interface HistoryRow {
  key: string
  startedAt: string
  /** How long the step took in milliseconds, or null while it runs. */
  ms: number | null
  step: { key: string; values: Record<string, string | number> } | null
  /** Null while the step runs, and for a step whose outcome has no words. */
  result: { key: string; values: Record<string, number> } | null
  /** The resource of a write, and null on any other row. */
  resource: AuthoringRequestStepResource | null
  /** The writes of a group row, newest first, and null on any other row. */
  rows: HistoryRow[] | null
}

/** The words of each step by its kind, see docs/reference/ai-authoring-in-studio.md#the-history. */
const historyStepKeys: Record<string, string> = {
  plan: 'shell.studio.aiRequest.history.step.plan',
  planChange: 'shell.studio.aiRequest.history.step.planChange',
  work: 'shell.studio.aiRequest.history.step.work',
  check: 'shell.studio.aiRequest.history.step.check',
  review: 'shell.studio.aiRequest.history.step.review',
  fix: 'shell.studio.aiRequest.history.step.fix',
  changeRequest: 'shell.studio.aiRequest.history.step.changeRequest',
  retry: 'shell.studio.aiRequest.history.step.retry',
  accept: 'shell.studio.aiRequest.history.step.accept',
  write: 'shell.studio.aiRequest.history.step.write',
}

/** The words of a `write` step made by the tool `delete_resource`, which remove a resource and do not write it. */
const historyDeleteKey = 'shell.studio.aiRequest.history.step.delete'

/** The words of a group of writes, with a delete among them or not. */
const historyGroupKeys = {
  wrote: 'shell.studio.aiRequest.history.step.wroteGroup',
  changed: 'shell.studio.aiRequest.history.step.changedGroup',
}

/** The result of a group whose writes ended differently. */
const historyDifferKey = 'shell.studio.aiRequest.history.result.differ'

/**
 * The result of a step, keyed by its kind and outcome, as `kind:outcome`, or by its outcome alone. A step with no outcome
 * is keyed as `kind:null`. A check with checks reads its words from `checksResult` first, see `historyResultOf`.
 */
const historyResultKeys: Record<string, string> = {
  'plan:null': 'shell.studio.aiRequest.history.result.planWritten',
  'work:null': 'shell.studio.aiRequest.history.result.saved',
  'write:saved': 'shell.studio.aiRequest.history.result.saved',
  'fix:null': 'shell.studio.aiRequest.history.result.saved',
  'review:passed': 'shell.studio.aiRequest.review.passed',
  'review:findings': 'shell.studio.aiRequest.review.findings',
  'review:notReviewed': 'shell.studio.aiRequest.review.notReviewed',
  'accept:accepted': 'shell.studio.aiRequest.history.result.accepted',
  'accept:conflict': 'shell.studio.aiRequest.history.result.conflict',
  refused: 'shell.studio.aiRequest.history.result.refused',
  notAccepted: 'shell.studio.aiRequest.history.result.notAccepted',
  timedOut: 'shell.studio.aiRequest.history.result.timedOut',
  overBudget: 'shell.studio.aiRequest.history.result.overBudget',
  tokenEnded: 'shell.studio.aiRequest.history.result.tokenEnded',
  noPlan: 'shell.studio.aiRequest.history.result.noPlan',
  keyRefused: 'shell.studio.aiRequest.history.result.keyRefused',
  noKey: 'shell.studio.aiRequest.history.result.noKey',
  authorDisabled: 'shell.studio.aiRequest.history.result.authorDisabled',
  unfinished: 'shell.studio.aiRequest.history.result.unfinished',
  interrupted: 'shell.studio.aiRequest.history.result.unfinished',
}

/** The words of a step: its kind's words, with the author's comment for a step that carries one. A delete reads as a delete. */
function historyStepOf(step: AuthoringRequestStep): HistoryRow['step'] {
  const key =
    step.kind === 'write' && step.summary === 'delete_resource' ? historyDeleteKey : historyStepKeys[step.kind]
  return key === undefined ? null : { key, values: { comment: step.summary ?? '' } }
}

/** The result of a step, from its checks when it has some, otherwise from its kind and outcome. */
function historyResultOf(step: AuthoringRequestStep): HistoryRow['result'] {
  if (step.finishedAt === null) {
    return null
  }
  if (step.kind === 'check' && step.checks !== null && (step.outcome === 'passed' || step.outcome === 'failed')) {
    const check = checksResult(step)
    return { key: check.summaryKey, values: check.values }
  }
  const key = historyResultKeys[`${step.kind}:${step.outcome}`] ?? historyResultKeys[step.outcome ?? '']
  return key === undefined ? null : { key, values: {} }
}

/**
 * One group row of a run of writes, see docs/reference/ai-authoring-in-studio.md#the-history. Its result is the one its writes
 * share, or Results differ, and null while one runs. Its duration runs from the first write's start to the last write's end.
 */
function writeGroup(key: string, writes: HistoryRow[]): HistoryRow {
  const results = writes.map((write) => write.result?.key)
  const shared = results.every((result) => result === results[0])
    ? writes[0].result
    : { key: historyDifferKey, values: {} }
  const running = writes.some((write) => write.ms === null)
  const first = Date.parse(writes[0].startedAt)
  const last = Math.max(...writes.map((write) => Date.parse(write.startedAt) + (write.ms ?? 0)))
  const deletes = writes.some((write) => write.step?.key === historyDeleteKey)
  return {
    key,
    startedAt: writes[0].startedAt,
    ms: running ? null : Math.max(0, last - first),
    step: { key: deletes ? historyGroupKeys.changed : historyGroupKeys.wrote, values: { count: writes.length } },
    result: running ? null : shared,
    resource: null,
    rows: writes.toReversed(),
  }
}

/** The row of one step. */
function historyRow(step: AuthoringRequestStep, key: string): HistoryRow {
  return {
    key,
    startedAt: step.startedAt,
    ms: step.finishedAt === null ? null : Math.max(0, Date.parse(step.finishedAt) - Date.parse(step.startedAt)),
    step: historyStepOf(step),
    result: historyResultOf(step),
    resource: step.resource,
    rows: null,
  }
}

/**
 * The rows of a request's History, newest first, see docs/reference/ai-authoring-in-studio.md#the-history. A step has no id,
 * so each row is keyed by the step's position, oldest first, which stays the same because steps are only ever added. Two or
 * more writes in a row fold into one group row that holds them, and a single write stays a row of its own.
 */
export function requestHistory(request: AuthoringRequest): HistoryRow[] {
  const steps = request.steps ?? []
  const rows: HistoryRow[] = []
  for (let start = 0, end = 0; start < steps.length; start = end) {
    end = start + 1
    while (steps[start].kind === 'write' && end < steps.length && steps[end].kind === 'write') {
      end++
    }
    rows.push(
      end - start === 1
        ? historyRow(steps[start], String(start))
        : writeGroup(
            `w${start}`,
            steps.slice(start, end).map((step, offset) => historyRow(step, `w${start}-${start + offset}`)),
          ),
    )
  }
  return rows.reverse()
}
