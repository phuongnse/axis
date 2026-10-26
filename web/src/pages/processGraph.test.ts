import { describe, expect, it } from 'vitest'
import approval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import { buildProcessGraph, transitionPointer } from './processGraph'

const approvalContent = approval as Record<string, unknown>

describe('buildProcessGraph', () => {
  it('builds the purchase request process graph in the compiler order', () => {
    const graph = buildProcessGraph(approvalContent, [])

    expect(graph.steps).toHaveLength(10)
    expect(graph.start).toBe(0)
    expect(graph.steps.some((step) => step.marked)).toBe(false)
    expect(graph.transitions.map((t) => [t.source, t.target, t.kind, t.label])).toEqual([
      [0, 1, 'next', null],
      [1, 2, 'outcome', 'approve'],
      [1, 7, 'outcome', 'return'],
      [1, 8, 'outcome', 'reject'],
      [2, 3, 'branch', 'totalAmount >= FinanceThreshold()'],
      [2, 4, 'otherwise', null],
      [3, 4, 'outcome', 'approve'],
      [3, 7, 'outcome', 'return'],
      [3, 8, 'outcome', 'reject'],
      [4, 5, 'next', null],
      [5, 6, 'next', null],
      [6, 9, 'next', null],
      [7, 9, 'next', null],
      [8, 9, 'next', null],
    ])
  })

  it('gives each transition the path of its branch, outcome or next field', () => {
    const graph = buildProcessGraph(approvalContent, [])

    expect(graph.transitions.map((t) => t.path)).toEqual([
      '/steps/0/next',
      '/steps/1/outcomes/0',
      '/steps/1/outcomes/1',
      '/steps/1/outcomes/2',
      '/steps/2/branches/0',
      '/steps/2/otherwise',
      '/steps/3/outcomes/0',
      '/steps/3/outcomes/1',
      '/steps/3/outcomes/2',
      '/steps/4/next',
      '/steps/5/next',
      '/steps/6/next',
      '/steps/7/next',
      '/steps/8/next',
    ])
  })

  it('points a name shared by two steps at the first of them', () => {
    const graph = buildProcessGraph(
      {
        steps: [
          { name: 'a', type: 'operation', next: 'b' },
          { name: 'b', type: 'end' },
          { name: 'B', type: 'end' },
          { name: 'c', type: 'operation', next: 'A' },
        ],
      },
      [],
    )

    expect(graph.steps.map((step) => step.name)).toEqual(['a', 'b', 'B', 'c'])
    expect(graph.transitions.map((t) => [t.source, t.target])).toEqual([
      [0, 1],
      [3, 0],
    ])
  })

  it('leaves out a transition whose target matches no step or is not a string', () => {
    const graph = buildProcessGraph(
      {
        start: 'missing',
        steps: [
          { name: 'a', next: 'missing' },
          { name: 'b', next: 5 },
        ],
      },
      [],
    )

    expect(graph.transitions).toEqual([])
    expect(graph.start).toBeNull()
    expect(graph.steps).toHaveLength(2)
  })

  it('marks a step that a diagnostic path names or starts with', () => {
    const steps = [{ name: 'a' }, { name: 'b' }, { name: 'c' }]

    const graph = buildProcessGraph({ steps }, ['/steps/2', '/steps/2/next', '/steps/20'])

    expect(graph.steps.map((step) => step.marked)).toEqual([false, false, true])
  })

  it('gives no node for a non-object step and keeps the original indexes of the others', () => {
    const graph = buildProcessGraph(
      {
        steps: ['x', null, { name: 'a' }, { name: 'b', next: 'a' }],
      },
      [],
    )

    expect(graph.steps.map((step) => step.index)).toEqual([2, 3])
    expect(graph.transitions).toEqual([{ source: 3, target: 2, kind: 'next', label: null, path: '/steps/3/next' }])
  })
})

describe('transitionPointer', () => {
  it('points at the next field of a branch or an outcome, and at the path of the other kinds', () => {
    const graph = buildProcessGraph(approvalContent, [])
    const pointers = new Map(graph.transitions.map((t) => [t.path, transitionPointer(t)]))

    expect(pointers.get('/steps/0/next')).toBe('/steps/0/next')
    expect(pointers.get('/steps/2/otherwise')).toBe('/steps/2/otherwise')
    expect(pointers.get('/steps/2/branches/0')).toBe('/steps/2/branches/0/next')
    expect(pointers.get('/steps/1/outcomes/0')).toBe('/steps/1/outcomes/0/next')
  })
})
