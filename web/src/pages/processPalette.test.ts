import { describe, expect, it } from 'vitest'
import schema from '../../../src/Axis.Configuration/Schemas/process.schema.json'
import {
  addStep,
  entryLabel,
  insertInto,
  newStep,
  paletteEntries,
  uniqueStepName,
  type PaletteEntry,
} from './processPalette'

const entry = (key: string): PaletteEntry => paletteEntries.find((candidate) => candidate.key === key)!

const content = {
  steps: [
    { name: 'submit', type: 'operation', operation: 'updateRecord', set: {}, next: 'approve' },
    {
      name: 'approve',
      type: 'task',
      outcomes: [
        { name: 'yes', next: 'check' },
        { name: 'later', next: '' },
      ],
    },
    { name: 'check', type: 'decision', branches: [{ when: 'a', next: 'done' }], otherwise: 'done' },
    { name: 'done', type: 'end' },
  ],
}

describe('the palette', () => {
  it('lists every step type and built-in operation of the process schema', () => {
    type Conditional = { if: { properties?: { operation?: { const?: string } } } }
    const step = schema.$defs.step as unknown as { properties: { type: { enum: string[] } }; allOf: Conditional[] }
    const types = step.properties.type.enum.filter((type) => type !== 'operation')
    const operations = step.allOf.flatMap((conditional) => conditional.if.properties?.operation?.const ?? [])

    expect(types.length).toBeGreaterThan(0)
    expect(operations.length).toBeGreaterThan(0)
    const entries = paletteEntries.map((candidate) => candidate.key)
    for (const type of types) {
      expect(entries, type).toContain(type)
    }
    for (const operation of operations) {
      expect(entries, operation).toContain(operation)
    }
  })

  it('has one package entry with no operation, and a text for it', () => {
    expect(entry('package')).toEqual({ key: 'package', type: 'operation', operation: '' })
    expect(entryLabel(entry('package'), 'Package operation')).toBe('Package operation')
    expect(entryLabel(entry('raiseEvent'), 'Package operation')).toBe('raiseEvent')
    expect(entryLabel(entry('end'), 'Package operation')).toBe('end')
  })

  it('makes each entry a step of its type with the required keys empty and no transition', () => {
    expect(newStep(entry('decision'), 'decision1')).toEqual({
      name: 'decision1',
      type: 'decision',
      branches: [],
      otherwise: '',
    })
    expect(newStep(entry('updateRecord'), 'u')).toEqual({
      name: 'u',
      type: 'operation',
      operation: 'updateRecord',
      next: '',
      set: {},
    })
    expect(newStep(entry('callConnector'), 'c')).toMatchObject({
      connector: { name: '', operation: '' },
      input: {},
    })
    expect(newStep(entry('raiseEvent'), 'r')).toMatchObject({ event: '', fields: {} })
    expect(newStep(entry('createRecord'), 'c')).toMatchObject({
      operation: 'createRecord',
      entity: '',
      set: {},
      next: '',
    })
    expect(newStep(entry('package'), 'p')).toMatchObject({ operation: '', input: {}, next: '' })
    expect(newStep(entry('subProcess'), 's')).toEqual({ name: 's', type: 'subProcess', process: '', next: '' })
    expect(newStep(entry('timer'), 't')).toEqual({ name: 't', type: 'timer', wait: '', next: '' })
    expect(newStep(entry('waitEvent'), 'w')).toEqual({ name: 'w', type: 'waitEvent', event: '', next: '' })
    expect(newStep(entry('end'), 'e')).toEqual({ name: 'e', type: 'end' })
  })

  it('does not share the empty values of one step with the next', () => {
    const first = newStep(entry('task'), 'a')
    const second = newStep(entry('task'), 'b')
    ;(first.outcomes as unknown[]).push({})
    expect(second.outcomes).toEqual([])
  })
})

describe('step names', () => {
  it('are the first free number after the type or operation, ignoring letter case', () => {
    expect(uniqueStepName([], 'task')).toBe('task1')
    expect(uniqueStepName([{ name: 'task1' }, { name: 'TASK2' }, { name: 'other' }], 'task')).toBe('task3')
    expect(uniqueStepName([{ name: 'Task1' }], 'task')).toBe('task2')
    expect(uniqueStepName([{ name: 'task2' }], 'task')).toBe('task1')
    expect(uniqueStepName([null, 3, { type: 'end' }], 'end')).toBe('end1')
  })

  it('are named by the operation, and by operation for the package entry', () => {
    expect(addStep(content, entry('raiseEvent')).name).toBe('raiseEvent1')
    expect(addStep(content, entry('package')).name).toBe('operation1')
    expect(addStep(content, entry('decision')).name).toBe('decision1')
  })

  it('stay unique when the same entry is added again', () => {
    const first = addStep(content, entry('end'))
    const second = addStep({ steps: first.steps }, entry('end'))
    expect([first.name, second.name]).toEqual(['end1', 'end2'])
  })
})

describe('adding a step', () => {
  it('appends an unconnected step and leaves the others as they are', () => {
    const added = addStep(content, entry('callConnector'))

    expect(added.index).toBe(4)
    expect(added.steps.slice(0, 4)).toEqual(content.steps)
    expect(added.steps[4]).toMatchObject({ name: 'callConnector1', operation: 'callConnector', next: '' })
    expect(content.steps).toHaveLength(4)
  })
})

describe('inserting a step into a transition', () => {
  it('retargets the transition to an operation and keeps the old target as its next', () => {
    const result = insertInto(content, '/steps/0/next', entry('updateRecord'))

    expect(result).not.toBe('endRefused')
    const { steps, index, name } = result as Exclude<typeof result, 'endRefused'>
    expect(name).toBe('updateRecord1')
    expect(index).toBe(4)
    expect(steps[0]).toMatchObject({ name: 'submit', next: 'updateRecord1' })
    expect(steps[4]).toEqual({
      name: 'updateRecord1',
      type: 'operation',
      operation: 'updateRecord',
      next: 'approve',
      set: {},
    })
    expect(content.steps[0].next).toBe('approve')
  })

  it('keeps the old target of a subProcess as its next', () => {
    const { steps } = insertInto(content, '/steps/0/next', entry('subProcess')) as { steps: unknown[] }
    expect(steps[4]).toEqual({ name: 'subProcess1', type: 'subProcess', process: '', next: 'approve' })
  })

  it('gives a decision the old target as otherwise, and leaves it no next', () => {
    const { steps } = insertInto(content, '/steps/0/next', entry('decision')) as { steps: unknown[] }
    expect(steps[4]).toEqual({ name: 'decision1', type: 'decision', branches: [], otherwise: 'approve' })
  })

  it('gives a task one empty outcome that holds the old target', () => {
    const { steps } = insertInto(content, '/steps/0/next', entry('task')) as { steps: Record<string, unknown>[] }
    expect(steps[4].outcomes).toEqual([{ name: '', label: { textKey: '' }, next: 'approve' }])
    expect(steps[4]).not.toHaveProperty('next')
  })

  it('retargets a branch, an otherwise and an outcome by their pointers', () => {
    const branch = insertInto(content, '/steps/2/branches/0/next', entry('raiseEvent')) as { steps: any[] }
    expect(branch.steps[2].branches[0].next).toBe('raiseEvent1')
    expect(branch.steps[2].otherwise).toBe('done')
    expect(branch.steps[4].next).toBe('done')

    const otherwise = insertInto(content, '/steps/2/otherwise', entry('raiseEvent')) as { steps: any[] }
    expect(otherwise.steps[2].otherwise).toBe('raiseEvent1')
    expect(otherwise.steps[2].branches[0].next).toBe('done')

    const outcome = insertInto(content, '/steps/1/outcomes/0/next', entry('raiseEvent')) as { steps: any[] }
    expect(outcome.steps[1].outcomes[0].next).toBe('raiseEvent1')
    expect(outcome.steps[1].outcomes[1].next).toBe('')
    expect(outcome.steps[4].next).toBe('check')
  })

  it('refuses an end step where the transition has a target', () => {
    expect(insertInto(content, '/steps/0/next', entry('end'))).toBe('endRefused')
    expect(insertInto(content, '/steps/2/otherwise', entry('end'))).toBe('endRefused')
  })

  it('keeps no old target when the field is empty, and takes an end step there', () => {
    const empty = insertInto(content, '/steps/1/outcomes/1/next', entry('updateRecord')) as { steps: any[] }
    expect(empty.steps[1].outcomes[1].next).toBe('updateRecord1')
    expect(empty.steps[4].next).toBe('')

    const end = insertInto(content, '/steps/1/outcomes/1/next', entry('end')) as { steps: any[] }
    expect(end.steps[1].outcomes[1].next).toBe('end1')
    expect(end.steps[4]).toEqual({ name: 'end1', type: 'end' })
  })

  it('names the new step apart from the existing ones', () => {
    const taken = { steps: [...content.steps, { name: 'UpdateRecord1', type: 'end' }] }
    expect((insertInto(taken, '/steps/0/next', entry('updateRecord')) as { name: string }).name).toBe('updateRecord2')
  })
})
