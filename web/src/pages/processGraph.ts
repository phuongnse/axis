export interface ProcessGraphStep {
  index: number
  name: string | null
  type: string | null
  marked: boolean
}

export type ProcessTransitionKind = 'branch' | 'otherwise' | 'next' | 'outcome'

export interface ProcessGraphTransition {
  source: number
  target: number
  kind: ProcessTransitionKind
  label: string | null
  path: string
}

export interface ProcessGraph {
  steps: ProcessGraphStep[]
  transitions: ProcessGraphTransition[]
  start: number | null
}

/** The pointer of the field that names the transition's target step. */
export function transitionPointer(transition: ProcessGraphTransition): string {
  return transition.kind === 'branch' || transition.kind === 'outcome' ? `${transition.path}/next` : transition.path
}

/** The accessible name of a transition, such as "financeCheck to markApproved · otherwise". */
export function transitionName(graph: ProcessGraph, transition: ProcessGraphTransition, to: string): string {
  const nameOf = (index: number) => graph.steps.find((step) => step.index === index)?.name ?? `/steps/${index}`
  return `${nameOf(transition.source)} ${to} ${nameOf(transition.target)} · ${transition.label ?? transition.kind}`
}

function isContent(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/** Resolves transitions as ProcessChecker.CheckGraph does: a name matches the first step with it, ignoring letter case. */
export function buildProcessGraph(content: Record<string, unknown>, diagnosticPaths: readonly string[]): ProcessGraph {
  if (!Array.isArray(content.steps)) {
    return { steps: [], transitions: [], start: null }
  }
  const rawSteps: unknown[] = content.steps

  const firstIndexByName = new Map<string, number>()
  rawSteps.forEach((item, index) => {
    if (isContent(item) && typeof item.name === 'string' && !firstIndexByName.has(item.name.toUpperCase())) {
      firstIndexByName.set(item.name.toUpperCase(), index)
    }
  })

  const resolve = (target: unknown): number | undefined =>
    typeof target === 'string' ? firstIndexByName.get(target.toUpperCase()) : undefined

  const steps: ProcessGraphStep[] = []
  const transitions: ProcessGraphTransition[] = []

  rawSteps.forEach((item, source) => {
    if (!isContent(item)) {
      return
    }

    const prefix = `/steps/${source}`
    steps.push({
      index: source,
      name: typeof item.name === 'string' ? item.name : null,
      type: typeof item.type === 'string' ? item.type : null,
      marked: diagnosticPaths.some((path) => path === prefix || path.startsWith(`${prefix}/`)),
    })

    const addTransition = (kind: ProcessTransitionKind, label: string | null, target: unknown, path: string) => {
      const resolved = resolve(target)
      if (resolved !== undefined) {
        transitions.push({ source, target: resolved, kind, label, path })
      }
    }

    const branches: unknown[] = Array.isArray(item.branches) ? item.branches : []
    branches.forEach((branch, index) => {
      if (isContent(branch)) {
        addTransition(
          'branch',
          typeof branch.when === 'string' ? branch.when : null,
          branch.next,
          `${prefix}/branches/${index}`,
        )
      }
    })

    addTransition('otherwise', null, item.otherwise, `${prefix}/otherwise`)
    addTransition('next', null, item.next, `${prefix}/next`)

    const outcomes: unknown[] = Array.isArray(item.outcomes) ? item.outcomes : []
    outcomes.forEach((outcome, index) => {
      if (isContent(outcome)) {
        addTransition(
          'outcome',
          typeof outcome.name === 'string' ? outcome.name : null,
          outcome.next,
          `${prefix}/outcomes/${index}`,
        )
      }
    })
  })

  return { steps, transitions, start: resolve(content.start) ?? null }
}
