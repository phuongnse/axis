import { describe, expect, it } from 'vitest'
import {
  changeStepType,
  findNamed,
  freshKey,
  literalText,
  outcomeOptions,
  parseJsonText,
  parseLiteral,
  recordAliases,
  recordEntity,
  renameKey,
  stepProperties,
  stepTypes,
  taskStepOptions,
} from './scenarioSteps'

const processes = [
  {
    name: 'Approval',
    steps: [
      { name: 'review', type: 'task', outcomes: ['approve', 'reject'] },
      { name: 'notify', type: 'service', outcomes: [] },
    ],
  },
  { name: 'Archive', steps: [{ name: 'file', type: 'task', outcomes: ['done'] }] },
]

describe('stepProperties', () => {
  it.each(stepTypes)('lists the properties of %s once each', (type) => {
    const names = stepProperties[type].map((property) => property.name)
    expect(new Set(names).size).toBe(names.length)
  })
})

describe('changeStepType', () => {
  it('keeps the shared properties and drops the ones the new type does not take', () => {
    const step = { type: 'createRecord', entity: 'Order', as: 'author', values: { code: 'A' }, save: 'order' }
    expect(changeStepType(step, 'deleteRecord')).toEqual({ type: 'deleteRecord', as: 'author' })
  })

  it('keeps keys it does not know', () => {
    const step = { type: 'advanceTime', by: 'PT1H', note: 'keep me' }
    expect(changeStepType(step, 'deleteRecord')).toEqual({ type: 'deleteRecord', note: 'keep me' })
  })

  it('sets the type of a step that has none', () => {
    expect(changeStepType({}, 'advanceTime')).toEqual({ type: 'advanceTime' })
  })

  it('drops the recipient and the channel when a notification step becomes a task expectation', () => {
    const step = { type: 'expectNotification', recipient: { user: 'anna' }, channel: 'email', note: 'keep me' }
    expect(changeStepType(step, 'expectTask')).toEqual({ type: 'expectTask', note: 'keep me' })
  })
})

describe('record aliases and entities', () => {
  const steps = [
    { type: 'createRecord', entity: 'Order', as: 'author', values: {}, save: 'order' },
    { type: 'updateRecord', record: 'order', as: 'author', values: {} },
    { type: 'expectRecord', record: 'unknown', values: {} },
  ]

  it('lists the aliases that earlier steps save', () => {
    expect(recordAliases(steps, 2)).toEqual(['order'])
  })

  it('finds the entity of an alias from the step that saved it', () => {
    expect(recordEntity(steps, 1)).toBe('Order')
  })

  it('finds no entity for an alias that nothing saved', () => {
    expect(recordEntity(steps, 2)).toBeUndefined()
  })

  it('finds the entity of a create step from its own entity', () => {
    expect(recordEntity(steps, 0)).toBe('Order')
  })
})

describe('step options', () => {
  it('lists the task steps of the processes the record started', () => {
    const steps = [
      { type: 'startProcess', process: 'Archive', record: 'order', as: 'author' },
      { type: 'completeTask', record: 'order', step: 'file', outcome: 'done', as: 'manager' },
    ]
    expect(taskStepOptions(processes, steps, 1)).toEqual(['file'])
  })

  it('lists every task step when no process was started on the record', () => {
    expect(taskStepOptions(processes, [{ type: 'expectTask', record: 'order' }], 0)).toEqual(['review', 'file'])
  })

  it('lists the outcomes of the named task only', () => {
    const steps = [{ type: 'completeTask', record: 'order', step: 'review', outcome: 'approve', as: 'manager' }]
    expect(outcomeOptions(processes, steps, 0)).toEqual(['approve', 'reject'])
  })
})

describe('names and keys', () => {
  it('finds a name ignoring letter case', () => {
    expect(findNamed([{ name: 'Order' }], 'order')).toEqual({ name: 'Order' })
  })

  it('renames a key and keeps the order of the keys', () => {
    expect(renameKey({ a: 1, b: 2, c: 3 }, 'b', 'x')).toEqual({ a: 1, x: 2, c: 3 })
    expect(Object.keys(renameKey({ a: 1, b: 2, c: 3 }, 'b', 'x')!)).toEqual(['a', 'x', 'c'])
  })

  it('refuses a rename to a key that is taken or empty', () => {
    expect(renameKey({ a: 1, b: 2 }, 'a', 'b')).toBeUndefined()
    expect(renameKey({ a: 1 }, 'a', '')).toBeUndefined()
  })

  it('allows a rename to the same key', () => {
    expect(renameKey({ a: 1 }, 'a', 'a')).toEqual({ a: 1 })
  })

  it('picks the first unused key with a prefix', () => {
    expect(freshKey({ user1: [], user3: [] }, 'user')).toBe('user2')
  })
})

describe('literal text', () => {
  it('shows a string that is itself JSON quoted, so reading it back gives the same string', () => {
    expect(parseLiteral(literalText('123'))).toBe('123')
    expect(parseLiteral(literalText('true'))).toBe('true')
  })

  it('shows numbers and objects as JSON', () => {
    expect(literalText(2)).toBe('2')
    expect(parseLiteral(literalText({ a: [1] }))).toEqual({ a: [1] })
  })

  it('keeps a text that is not JSON as it is', () => {
    expect(parseLiteral('Lan')).toBe('Lan')
  })

  it('shows nothing for a missing value', () => {
    expect(literalText(undefined)).toBe('')
  })
})

describe('parseJsonText', () => {
  it('reads JSON', () => {
    expect(parseJsonText('{"a":1}')).toEqual({ ok: true, value: { a: 1 } })
  })

  it('refuses text that is not JSON', () => {
    expect(parseJsonText('{"a":')).toEqual({ ok: false })
  })
})
