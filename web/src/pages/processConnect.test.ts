import { describe, expect, it } from 'vitest'
import approval from '../../../samples/apps/purchase-requests/processes/purchase-request-approval.json'
import { choiceLabel, connectChoices, connectEdit } from './processConnect'

const steps = (approval as { steps: unknown[] }).steps
const done = 9

describe('connectChoices', () => {
  it('offers nothing from an end step or a value that is not a step', () => {
    expect(connectChoices(steps[done])).toEqual([])
    expect(connectChoices(null)).toEqual([])
    expect(connectChoices('done')).toEqual([])
  })

  it('offers the branches, otherwise and a new branch from a decision, in that order', () => {
    expect(connectChoices(steps[2])).toEqual([
      { kind: 'branch', index: 0 },
      { kind: 'otherwise' },
      { kind: 'newBranch' },
    ])
  })

  it('offers the outcomes and a new outcome from a task', () => {
    expect(connectChoices(steps[1])).toEqual([
      { kind: 'outcome', index: 0 },
      { kind: 'outcome', index: 1 },
      { kind: 'outcome', index: 2 },
      { kind: 'newOutcome' },
    ])
  })

  it('offers next from an operation and from a step type it does not know', () => {
    expect(connectChoices(steps[0])).toEqual([{ kind: 'next' }])
    expect(connectChoices({ type: 'timer', name: 'wait' })).toEqual([{ kind: 'next' }])
  })
})

describe('connectEdit', () => {
  it('sets the next of an operation to the target name', () => {
    expect(connectEdit(steps, 0, done, { kind: 'next' })).toEqual({ pointer: '/steps/0/next', value: 'done' })
  })

  it('sets the otherwise of a decision', () => {
    expect(connectEdit(steps, 2, done, { kind: 'otherwise' })).toEqual({ pointer: '/steps/2/otherwise', value: 'done' })
  })

  it('sets the next of a branch', () => {
    expect(connectEdit(steps, 2, done, { kind: 'branch', index: 0 })).toEqual({
      pointer: '/steps/2/branches/0/next',
      value: 'done',
    })
  })

  it('appends a branch with an empty when for a new branch', () => {
    const branches = (steps[2] as { branches: unknown[] }).branches
    expect(connectEdit(steps, 2, done, { kind: 'newBranch' })).toEqual({
      pointer: '/steps/2/branches',
      value: [...branches, { when: '', next: 'done' }],
    })
  })

  it('sets the next of an outcome', () => {
    expect(connectEdit(steps, 1, done, { kind: 'outcome', index: 1 })).toEqual({
      pointer: '/steps/1/outcomes/1/next',
      value: 'done',
    })
  })

  it('appends an outcome with the first free name and an empty label key for a new outcome', () => {
    const outcomes = (steps[1] as { outcomes: unknown[] }).outcomes
    expect(connectEdit(steps, 1, done, { kind: 'newOutcome' })).toEqual({
      pointer: '/steps/1/outcomes',
      value: [...outcomes, { name: 'outcome1', label: { textKey: '' }, next: 'done' }],
    })
  })

  it('skips the outcome names that are taken, ignoring letter case', () => {
    const task = { name: 'ask', type: 'task', outcomes: [{ name: 'Outcome1' }, { name: 'outcome3' }] }
    const edit = connectEdit([task, steps[done]], 0, 1, { kind: 'newOutcome' })
    expect(edit).not.toBeNull()
    expect((edit!.value as { name: string }[])[2].name).toBe('outcome2')
  })

  it('makes no edit for a drag onto the same step or onto a step without a name', () => {
    expect(connectEdit(steps, 0, 0, { kind: 'next' })).toBeNull()
    expect(connectEdit([steps[0], { type: 'end' }], 0, 1, { kind: 'next' })).toBeNull()
    expect(connectEdit(steps, 0, 99, { kind: 'next' })).toBeNull()
  })
})

describe('choiceLabel', () => {
  const t = (key: string) => key.split('.').at(-1) ?? key

  it('names a branch by its when and an outcome by its name', () => {
    expect(choiceLabel(steps[2], { kind: 'branch', index: 0 }, t)).toBe('totalAmount >= FinanceThreshold()')
    expect(choiceLabel(steps[1], { kind: 'outcome', index: 0 }, t)).toBe('approve')
  })

  it('numbers a branch without a when and an outcome without a name', () => {
    expect(choiceLabel({ branches: [{ when: '' }] }, { kind: 'branch', index: 0 }, t)).toBe('branchNumber 1')
    expect(choiceLabel({ outcomes: [{}, {}] }, { kind: 'outcome', index: 1 }, t)).toBe('outcomeNumber 2')
  })
})
