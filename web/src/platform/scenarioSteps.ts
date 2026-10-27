import type { StudioNameStep } from './studio'

/** The step types of a scenario, as `scenario.schema.json` lists them. */
export const stepTypes = [
  'createRecord',
  'updateRecord',
  'deleteRecord',
  'startProcess',
  'completeTask',
  'expectRecord',
  'expectInstance',
  'expectTask',
  'expectNotification',
  'advanceTime',
] as const

export type StepType = (typeof stepTypes)[number]

/** Every property a step may hold. A type takes only some of them, and the others are refused. */
export const stepPropertyNames = [
  'entity',
  'record',
  'save',
  'as',
  'process',
  'step',
  'outcome',
  'state',
  'assignee',
  'values',
  'expect',
  'by',
  'recipient',
  'channel',
] as const

export type StepProperty = (typeof stepPropertyNames)[number]

/** The properties of each step type, in panel order. An `expectTask` takes exactly one of `assignee` and `state`. */
export const stepProperties: Record<StepType, { name: StepProperty; required: boolean }[]> = {
  createRecord: [
    { name: 'entity', required: true },
    { name: 'as', required: true },
    { name: 'values', required: true },
    { name: 'save', required: false },
    { name: 'expect', required: false },
  ],
  updateRecord: [
    { name: 'record', required: true },
    { name: 'as', required: true },
    { name: 'values', required: true },
    { name: 'expect', required: false },
  ],
  deleteRecord: [
    { name: 'record', required: true },
    { name: 'as', required: true },
    { name: 'expect', required: false },
  ],
  startProcess: [
    { name: 'process', required: true },
    { name: 'record', required: true },
    { name: 'as', required: true },
    { name: 'expect', required: false },
  ],
  completeTask: [
    { name: 'record', required: true },
    { name: 'step', required: true },
    { name: 'outcome', required: true },
    { name: 'as', required: true },
    { name: 'values', required: false },
    { name: 'expect', required: false },
  ],
  expectRecord: [
    { name: 'record', required: true },
    { name: 'values', required: true },
  ],
  expectInstance: [
    { name: 'process', required: true },
    { name: 'record', required: true },
    { name: 'state', required: true },
    { name: 'step', required: false },
  ],
  expectTask: [
    { name: 'record', required: true },
    { name: 'step', required: true },
    { name: 'assignee', required: false },
    { name: 'state', required: false },
  ],
  expectNotification: [
    { name: 'recipient', required: true },
    { name: 'channel', required: false },
    { name: 'values', required: false },
  ],
  advanceTime: [{ name: 'by', required: true }],
}

/** The states a step of each type may expect. A type not listed here takes no `state`. */
export const stateOptions: Partial<Record<StepType, string[]>> = {
  expectInstance: ['running', 'waiting', 'completed', 'failed'],
  expectTask: ['open', 'completed'],
}

export type Step = Record<string, unknown>

export function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isStepProperty(key: string): key is StepProperty {
  return (stepPropertyNames as readonly string[]).includes(key)
}

/** The pointer segment of a key, escaped for a JSON Pointer. */
export function pointerSegment(key: string): string {
  return key.replaceAll('~', '~0').replaceAll('/', '~1')
}

/**
 * The step with a new type. It drops the known properties the new type does not take, because the schema refuses
 * them, and keeps the shared ones and every key it does not know.
 */
export function changeStepType(step: Step, type: StepType): Step {
  const allowed = new Set(stepProperties[type].map((property) => property.name as string))
  const next: Step = {}
  for (const [key, value] of Object.entries(step)) {
    if (key === 'type') {
      next.type = type
    } else if (!isStepProperty(key) || allowed.has(key)) {
      next[key] = value
    }
  }
  if (!Object.hasOwn(next, 'type')) {
    next.type = type
  }
  return next
}

/** The aliases that earlier `createRecord` steps save, which later steps may name as their `record`. */
export function recordAliases(steps: unknown[], index: number): string[] {
  return steps.slice(0, index).flatMap((step) => {
    const save = isObject(step) && step.type === 'createRecord' ? step.save : undefined
    return typeof save === 'string' ? [save] : []
  })
}

/**
 * The entity of a step's record: its own `entity` for a `createRecord`, otherwise the entity of the earlier
 * `createRecord` that saved the alias it names. `undefined` when neither is known.
 */
export function recordEntity(steps: unknown[], index: number): string | undefined {
  const step = steps[index]
  if (!isObject(step)) {
    return undefined
  }
  if (step.type === 'createRecord') {
    return typeof step.entity === 'string' ? step.entity : undefined
  }
  if (typeof step.record !== 'string') {
    return undefined
  }
  const earlier = steps
    .slice(0, index)
    .find((candidate) => isObject(candidate) && candidate.type === 'createRecord' && candidate.save === step.record)
  return isObject(earlier) && typeof earlier.entity === 'string' ? earlier.entity : undefined
}

/** The processes that earlier `startProcess` steps started on the step's record, or all of them when there are none. */
function candidateProcesses<P extends { name: string; steps: StudioNameStep[] }>(
  processes: P[],
  steps: unknown[],
  index: number,
): P[] {
  const step = steps[index]
  const record = isObject(step) ? step.record : undefined
  const started = steps.slice(0, index).flatMap((candidate) => {
    if (!isObject(candidate) || candidate.type !== 'startProcess' || candidate.record !== record) {
      return []
    }
    const name = typeof candidate.process === 'string' ? candidate.process.toLowerCase() : undefined
    return processes.filter((process) => process.name.toLowerCase() === name)
  })
  return started.length > 0 ? started : processes
}

/** Adds each name once, ignoring letter case. The first spelling wins. */
function unique(names: string[]): string[] {
  const seen = new Map<string, string>()
  for (const name of names) {
    if (!seen.has(name.toLowerCase())) {
      seen.set(name.toLowerCase(), name)
    }
  }
  return [...seen.values()]
}

/** The task steps a `completeTask` or `expectTask` step may name, from its candidate processes. */
export function taskStepOptions(
  processes: { name: string; steps: StudioNameStep[] }[],
  steps: unknown[],
  index: number,
): string[] {
  return unique(
    candidateProcesses(processes, steps, index).flatMap((process) =>
      process.steps.filter((step) => step.type === 'task').map((step) => step.name),
    ),
  )
}

/** The outcomes of the task step that the step names, or of every candidate task when it names none. */
export function outcomeOptions(
  processes: { name: string; steps: StudioNameStep[] }[],
  steps: unknown[],
  index: number,
): string[] {
  const step = steps[index]
  const named = isObject(step) && typeof step.step === 'string' ? step.step.toLowerCase() : undefined
  return unique(
    candidateProcesses(processes, steps, index).flatMap((process) =>
      process.steps
        .filter(
          (candidate) => candidate.type === 'task' && (named === undefined || candidate.name.toLowerCase() === named),
        )
        .flatMap((candidate) => candidate.outcomes),
    ),
  )
}

/** The steps of the process of that name, ignoring letter case. */
export function processStepOptions(
  processes: { name: string; steps: StudioNameStep[] }[],
  processName: unknown,
): string[] {
  if (typeof processName !== 'string') {
    return []
  }
  const wanted = processName.toLowerCase()
  return processes.find((process) => process.name.toLowerCase() === wanted)?.steps.map((step) => step.name) ?? []
}

/** The item of that name, ignoring letter case. */
export function findNamed<T extends { name: string }>(items: T[], name: string | undefined): T | undefined {
  if (name === undefined) {
    return undefined
  }
  const wanted = name.toLowerCase()
  return items.find((item) => item.name.toLowerCase() === wanted)
}

/** A copy of `record` with `from` renamed to `to`, keeping the key order. `undefined` when `to` is taken or empty. */
export function renameKey(
  record: Record<string, unknown>,
  from: string,
  to: string,
): Record<string, unknown> | undefined {
  if (to === '' || (to !== from && Object.hasOwn(record, to))) {
    return undefined
  }
  return Object.fromEntries(Object.entries(record).map(([key, value]) => (key === from ? [to, value] : [key, value])))
}

/** The first `prefix` plus a number that no key of `record` uses. */
export function freshKey(record: Record<string, unknown>, prefix: string): string {
  let number = 1
  while (Object.hasOwn(record, `${prefix}${number}`)) {
    number++
  }
  return `${prefix}${number}`
}

/**
 * The text of a value in the JSON literal box. A string that is itself JSON shows quoted, so that reading the text
 * back gives the same string.
 */
export function literalText(value: unknown): string {
  if (value === undefined) {
    return ''
  }
  if (typeof value === 'string') {
    return isJson(value) ? JSON.stringify(value) : value
  }
  return JSON.stringify(value)
}

function isJson(text: string): boolean {
  try {
    JSON.parse(text)
    return true
  } catch {
    return false
  }
}

/** The value a literal box text stands for: its JSON value, or the text itself when it is not JSON. */
export function parseLiteral(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

/** The JSON value of a text, or `ok: false` when the text is not JSON. */
export function parseJsonText(text: string): { ok: true; value: unknown } | { ok: false } {
  try {
    return { ok: true, value: JSON.parse(text) }
  } catch {
    return { ok: false }
  }
}
