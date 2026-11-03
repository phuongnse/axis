import { uniqueStepName } from './processPalette'

export type ConnectChoice =
  | { kind: 'next' }
  | { kind: 'otherwise' }
  | { kind: 'newBranch' }
  | { kind: 'newOutcome' }
  | { kind: 'branch'; index: number }
  | { kind: 'outcome'; index: number }

function isContent(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function listOf(value: unknown): unknown[] {
  return Array.isArray(value) ? value : []
}

/**
 * What a drag from `step` can set. An `end` step has no transition. A decision offers its branches, `otherwise` and a
 * new branch, a task its outcomes and a new outcome, and every other step, including types added later, its `next`.
 */
export function connectChoices(step: unknown): ConnectChoice[] {
  if (!isContent(step) || step.type === 'end') {
    return []
  }
  if (step.type === 'decision') {
    return [
      ...listOf(step.branches).map((_, index): ConnectChoice => ({ kind: 'branch', index })),
      { kind: 'otherwise' },
      { kind: 'newBranch' },
    ]
  }
  if (step.type === 'task') {
    return [
      ...listOf(step.outcomes).map((_, index): ConnectChoice => ({ kind: 'outcome', index })),
      { kind: 'newOutcome' },
    ]
  }
  return [{ kind: 'next' }]
}

/** The first `outcomeN` that no outcome of the step uses, ignoring letter case. */
function uniqueOutcomeName(outcomes: unknown[]): string {
  const used = new Set(
    outcomes.map((outcome) =>
      isContent(outcome) && typeof outcome.name === 'string' ? outcome.name.toUpperCase() : '',
    ),
  )
  let number = 1
  while (used.has(`OUTCOME${number}`)) {
    number++
  }
  return `outcome${number}`
}

/**
 * The one edit that a drag from step `source` to step `target` makes for `choice`, or `null` when it makes none: a step
 * cannot lead to itself by a drag, and the target needs a name. A new branch has an empty `when`, and a new outcome
 * the first free name `outcomeN` and an empty label key, which the checker marks until the author fills them in.
 */
export function connectEdit(
  steps: unknown[],
  source: number,
  target: number,
  choice: ConnectChoice,
): { pointer: string; value: unknown } | null {
  const targetStep = steps[target]
  const sourceStep = steps[source]
  if (source === target || !isContent(targetStep) || !isContent(sourceStep) || typeof targetStep.name !== 'string') {
    return null
  }
  const next = targetStep.name
  const prefix = `/steps/${source}`
  switch (choice.kind) {
    case 'next':
      return { pointer: `${prefix}/next`, value: next }
    case 'otherwise':
      return { pointer: `${prefix}/otherwise`, value: next }
    case 'branch':
      return { pointer: `${prefix}/branches/${choice.index}/next`, value: next }
    case 'outcome':
      return { pointer: `${prefix}/outcomes/${choice.index}/next`, value: next }
    case 'newBranch': {
      const branches = listOf(sourceStep.branches)
      return {
        pointer: `${prefix}/branches`,
        value: [...branches, { name: uniqueStepName(branches, 'branch'), when: '', next }],
      }
    }
    case 'newOutcome': {
      const outcomes = listOf(sourceStep.outcomes)
      return {
        pointer: `${prefix}/outcomes`,
        value: [...outcomes, { name: uniqueOutcomeName(outcomes), label: { textKey: '' }, next }],
      }
    }
  }
}

/**
 * The text of a choice in the menu and the announcement. A branch shows its `when`, or "Branch n" when that is empty,
 * and an outcome its name, or "Outcome n". For a new branch or outcome it is what the new one will be called.
 */
export function choiceLabel(step: unknown, choice: ConnectChoice, t: (key: string) => string): string {
  const content = isContent(step) ? step : {}
  switch (choice.kind) {
    case 'next':
      return t('shell.studio.process.next')
    case 'otherwise':
      return t('shell.studio.process.otherwise')
    case 'newBranch':
      return t('shell.studio.process.newBranch')
    case 'newOutcome':
      return t('shell.studio.process.newOutcome')
    case 'branch': {
      const branch = listOf(content.branches)[choice.index]
      const when = isContent(branch) && typeof branch.when === 'string' ? branch.when : ''
      return when || `${t('shell.studio.process.branchNumber')} ${choice.index + 1}`
    }
    case 'outcome': {
      const outcome = listOf(content.outcomes)[choice.index]
      const name = isContent(outcome) && typeof outcome.name === 'string' ? outcome.name : ''
      return name || `${t('shell.studio.process.outcomeNumber')} ${choice.index + 1}`
    }
  }
}
