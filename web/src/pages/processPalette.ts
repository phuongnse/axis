import { getAt, setAt } from '../platform/jsonPatch'
import { builtInOperations, packageChoice } from './operationKeys'

type Content = Record<string, unknown>

/** The keys a step type requires, with the empty value that a new key starts with. */
export const requiredStepKeys: Record<string, Content> = {
  decision: { branches: [], otherwise: '' },
  task: { label: { textKey: '' }, assignee: {}, form: '', outcomes: [] },
  operation: { operation: '', next: '' },
  subProcess: { process: '', next: '' },
  end: {},
}

/** One type of the palette. An entry with an `operation` adds an operation step, and the package entry has it empty. */
export interface PaletteEntry {
  key: string
  type: string
  operation?: string
}

/** The step types and built-in operations that the process schema defines and that the palette offers, in order. */
export const paletteEntries: PaletteEntry[] = [
  { key: 'decision', type: 'decision' },
  { key: 'task', type: 'task' },
  ...builtInOperations.map((operation) => ({ key: operation, type: 'operation', operation })),
  { key: packageChoice, type: 'operation', operation: '' },
  { key: 'subProcess', type: 'subProcess' },
  { key: 'end', type: 'end' },
]

/** The keys besides `operation` and `next` that an operation requires, empty. */
const operationKeys: Record<string, Content> = {
  updateRecord: { set: {} },
  callConnector: { connector: { name: '', operation: '' }, input: {} },
  raiseEvent: { event: '', fields: {} },
  createRecord: { entity: '', set: {} },
  notify: {},
  [packageChoice]: { input: {} },
}

/** The text of an entry: its operation or type, and `packageText` for the package entry, which has no operation yet. */
export function entryLabel(entry: PaletteEntry, packageText: string): string {
  return entry.operation === '' ? packageText : (entry.operation ?? entry.type)
}

export interface Added {
  steps: unknown[]
  index: number
  name: string
}

/** A new step of the entry with its required keys empty and no transition. */
export function newStep(entry: PaletteEntry, name: string): Content {
  const step: Content = { name, type: entry.type, ...structuredClone(requiredStepKeys[entry.type]) }
  if (entry.operation === undefined) {
    return step
  }
  step.operation = entry.operation
  const extra = operationKeys[entry.operation === '' ? packageChoice : entry.operation]
  return { ...step, ...structuredClone(extra) }
}

/** The first `base1`, `base2` and so on that no step uses, ignoring letter case as the process graph does. */
export function uniqueStepName(steps: readonly unknown[], base: string): string {
  const used = new Set(
    steps.flatMap((step) => {
      const name = (step as Content | null)?.name
      return typeof name === 'string' ? name.toUpperCase() : []
    }),
  )
  let n = 1
  while (used.has(`${base}${n}`.toUpperCase())) {
    n++
  }
  return `${base}${n}`
}

function nameFor(steps: readonly unknown[], entry: PaletteEntry): string {
  return uniqueStepName(steps, entry.operation === undefined ? entry.type : entry.operation || 'operation')
}

function stepsOf(content: Content): unknown[] {
  return Array.isArray(content.steps) ? content.steps : []
}

/** The steps with a new, unconnected step of the entry at the end. */
export function addStep(content: Content, entry: PaletteEntry): Added {
  const steps = stepsOf(content)
  const name = nameFor(steps, entry)
  return { steps: [...steps, newStep(entry, name)], index: steps.length, name }
}

/**
 * The steps with a new step of the entry inserted into the transition whose target field is at `pointer`: the
 * transition names the new step, and the new step keeps the old target as its `next`, as its `otherwise` for a
 * decision, or as the target of one empty outcome for a task. An old target that is empty is not kept. An `end` step
 * has nothing to keep it in, so it is refused when the transition has a target.
 */
export function insertInto(content: Content, pointer: string, entry: PaletteEntry): Added | 'endRefused' {
  const steps = stepsOf(content)
  const old = getAt({ steps }, pointer)
  const target = typeof old === 'string' ? old : ''
  if (entry.type === 'end' && target !== '') {
    return 'endRefused'
  }
  const name = nameFor(steps, entry)
  const step = newStep(entry, name)
  if (target !== '') {
    if (entry.type === 'decision') {
      step.otherwise = target
    } else if (entry.type === 'task') {
      step.outcomes = [{ name: '', label: { textKey: '' }, next: target }]
    } else {
      step.next = target
    }
  }
  const rewired = (setAt({ steps }, pointer, name) as { steps: unknown[] }).steps
  return { steps: [...rewired, step], index: rewired.length, name }
}
