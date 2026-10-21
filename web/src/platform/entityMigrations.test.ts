import { describe, expect, it } from 'vitest'
import {
  applyAnswers,
  asked,
  fieldQuestions,
  fieldWarnings,
  loadedOrigins,
  matchOrigins,
  migrationFile,
  migrationName,
  migrationResource,
} from './entityMigrations'

const loaded = {
  name: 'Order',
  fields: [
    { name: 'number', type: 'text' },
    { name: 'note', type: 'text' },
    { name: 'lines', type: 'child-collection', target: 'Line' },
    { name: 'status', type: 'text' },
  ],
}

function withFields(...fields: unknown[]) {
  return { ...loaded, fields }
}

describe('fieldQuestions', () => {
  it('asks nothing for an unchanged entity', () => {
    expect(fieldQuestions(loaded, loaded, loadedOrigins(loaded), [])).toEqual([])
  })

  it('asks nothing for a renamed or removed child-collection field', () => {
    const renamed = withFields(
      loaded.fields[0],
      loaded.fields[1],
      { ...loaded.fields[2], name: 'rows' },
      loaded.fields[3],
    )
    const removed = withFields(loaded.fields[0], loaded.fields[1], loaded.fields[3])

    expect(fieldQuestions(loaded, renamed, loadedOrigins(loaded), [])).toEqual([])
    expect(fieldQuestions(loaded, removed, [loaded.fields[0].name, 'note', 'status'], [])).toEqual([])
  })

  it('follows a row through a move and then a rename', () => {
    // `note` moved to the front, then renamed.
    const draft = withFields(
      { ...loaded.fields[1], name: 'remark' },
      loaded.fields[0],
      loaded.fields[2],
      loaded.fields[3],
    )

    expect(fieldQuestions(loaded, draft, ['note', 'number', 'lines', 'status'], ['note'])).toEqual([
      { type: 'rename', from: 'note', to: 'remark' },
    ])
  })

  it('asks nothing for a field added in this session, even when it is renamed', () => {
    const draft = withFields(...loaded.fields, { name: 'memo', type: 'text' })

    expect(fieldQuestions(loaded, draft, [...loadedOrigins(loaded), null], [])).toEqual([])
  })

  it('asks a removal for a loaded field that no row claims', () => {
    const draft = withFields(loaded.fields[0], loaded.fields[2], loaded.fields[3])

    expect(fieldQuestions(loaded, draft, ['number', 'lines', 'status'], ['note'])).toEqual([
      { type: 'remove', field: 'note', loadedIndex: 1 },
    ])
  })

  it('counts a new row with the name of a removed loaded field as that field', () => {
    const draft = withFields(loaded.fields[0], loaded.fields[2], loaded.fields[3], { name: 'note', type: 'text' })

    expect(fieldQuestions(loaded, draft, ['number', 'lines', 'status', null], ['note'])).toEqual([])
  })

  it('follows the order of the touches, then the loaded order', () => {
    const draft = withFields({ name: 'n2', type: 'text' }, { name: 'note', type: 'text' }, loaded.fields[2], {
      name: 's2',
      type: 'text',
    })

    expect(fieldQuestions(loaded, draft, loadedOrigins(loaded), ['status', 'number'])).toEqual([
      { type: 'rename', from: 'status', to: 's2' },
      { type: 'rename', from: 'number', to: 'n2' },
    ])
    expect(fieldQuestions(loaded, draft, loadedOrigins(loaded), [])).toEqual([
      { type: 'rename', from: 'number', to: 'n2' },
      { type: 'rename', from: 'status', to: 's2' },
    ])
  })
})

describe('fieldQuestions for convert and fill', () => {
  const sized = {
    name: 'Order',
    fields: [
      { name: 'code', type: 'text', maxLength: 20 },
      { name: 'kind', type: 'enum', values: ['a', 'b'] },
      { name: 'note', type: 'text' },
    ],
  }
  const origins = ['code', 'kind', 'note']

  it('asks convert for a new type', () => {
    const draft = { ...sized, fields: [{ name: 'code', type: 'integer' }, sized.fields[1], sized.fields[2]] }

    expect(fieldQuestions(sized, draft, origins, ['code'])).toEqual([
      { type: 'convert', origin: 'code', field: 'code', reason: 'type' },
    ])
  })

  it('asks one convert for a type changed twice', () => {
    const draft = {
      ...sized,
      fields: [{ name: 'code', type: 'text', maxLength: 20 }, sized.fields[1], sized.fields[2]],
    }

    expect(fieldQuestions(sized, draft, origins, ['code'])).toEqual([])
    const changed = { ...sized, fields: [{ name: 'code', type: 'date' }, sized.fields[1], sized.fields[2]] }
    expect(fieldQuestions(sized, changed, origins, ['code', 'code'])).toHaveLength(1)
  })

  it('asks convert for a smaller or a new maxLength, and not for a larger one', () => {
    const smaller = {
      ...sized,
      fields: [{ name: 'code', type: 'text', maxLength: 10 }, sized.fields[1], sized.fields[2]],
    }
    const larger = {
      ...sized,
      fields: [{ name: 'code', type: 'text', maxLength: 30 }, sized.fields[1], sized.fields[2]],
    }
    const added = { ...sized, fields: [sized.fields[0], sized.fields[1], { name: 'note', type: 'text', maxLength: 5 }] }

    expect(fieldQuestions(sized, smaller, origins, [])).toEqual([
      { type: 'convert', origin: 'code', field: 'code', reason: 'maxLength' },
    ])
    expect(fieldQuestions(sized, larger, origins, [])).toEqual([])
    expect(fieldQuestions(sized, added, origins, [])).toEqual([
      { type: 'convert', origin: 'note', field: 'note', reason: 'maxLength' },
    ])
  })

  it('asks convert for a removed enum value and nothing for a type change to or from enum', () => {
    const fewer = {
      ...sized,
      fields: [sized.fields[0], { name: 'kind', type: 'enum', values: ['a'] }, sized.fields[2]],
    }
    const more = {
      ...sized,
      fields: [sized.fields[0], { name: 'kind', type: 'enum', values: ['a', 'b', 'c'] }, sized.fields[2]],
    }
    const toEnum = {
      ...sized,
      fields: [sized.fields[0], sized.fields[1], { name: 'note', type: 'enum', values: ['x'] }],
    }
    const fromEnum = { ...sized, fields: [sized.fields[0], { name: 'kind', type: 'text' }, sized.fields[2]] }

    expect(fieldQuestions(sized, fewer, origins, [])).toEqual([
      { type: 'convert', origin: 'kind', field: 'kind', reason: 'values' },
    ])
    expect(fieldQuestions(sized, more, origins, [])).toEqual([])
    expect(fieldQuestions(sized, toEnum, origins, [])).toEqual([])
    expect(fieldQuestions(sized, fromEnum, origins, [])).toEqual([])
  })

  it('asks fill for a loaded field made required and for a new required field, after the loaded fields', () => {
    const draft = {
      ...sized,
      fields: [
        { name: 'fresh', type: 'text', required: true },
        sized.fields[0],
        sized.fields[1],
        { ...sized.fields[2], required: true },
      ],
    }

    expect(fieldQuestions(sized, draft, [null, 'code', 'kind', 'note'], [])).toEqual([
      { type: 'fill', origin: 'note', field: 'note', whenDropped: false },
      { type: 'fill', origin: null, field: 'fresh', whenDropped: false },
    ])
  })

  it('asks nothing for a child-collection field made required', () => {
    const draft = { ...sized, fields: [...sized.fields, { name: 'lines', type: 'child-collection', required: true }] }

    expect(fieldQuestions(sized, draft, [...origins, null], [])).toEqual([])
  })

  it('groups rename, convert and fill of one field, with the new name after the rename', () => {
    const draft = {
      ...sized,
      fields: [sized.fields[0], sized.fields[1], { name: 'remark', type: 'integer', required: true }],
    }

    expect(fieldQuestions(sized, draft, origins, ['note'])).toEqual([
      { type: 'rename', from: 'note', to: 'remark' },
      { type: 'convert', origin: 'note', field: 'remark', reason: 'type' },
      { type: 'fill', origin: 'note', field: 'remark', whenDropped: false },
    ])
  })

  it('asks a fill that only counts when a rename drops the data for a field that was already required', () => {
    const required = { ...sized, fields: [{ name: 'code', type: 'text', required: true }] }
    const draft = { ...sized, fields: [{ name: 'key', type: 'text', required: true }] }
    const questions = fieldQuestions(required, draft, ['code'], ['code'])

    expect(questions).toEqual([
      { type: 'rename', from: 'code', to: 'key' },
      { type: 'fill', origin: 'code', field: 'key', whenDropped: true },
    ])
    expect(asked(questions, ['keep'], 1)).toBe(false)
    expect(asked(questions, ['drop'], 1)).toBe(true)
    expect(applyAnswers(required, draft, ['code'], questions, ['keep', 'skip']).changes).toEqual([
      { type: 'renameField', from: 'code', to: 'key' },
    ])
    expect(applyAnswers(required, draft, ['code'], questions, ['drop', { value: '0' }]).changes).toEqual([
      { type: 'dropField', field: 'code' },
      { type: 'fill', field: 'key', value: '0' },
    ])
  })

  it('skips the convert of a field whose rename drops the data', () => {
    const draft = { ...sized, fields: [sized.fields[0], sized.fields[1], { name: 'remark', type: 'integer' }] }
    const questions = fieldQuestions(sized, draft, origins, ['note'])

    expect(asked(questions, ['drop'], 1)).toBe(false)
    expect(asked(questions, ['keep'], 1)).toBe(true)
    expect(applyAnswers(sized, draft, origins, questions, ['drop', { value: 'x' }]).changes).toEqual([
      { type: 'dropField', field: 'note' },
    ])
  })

  it('adds a convert or fill only for an answer with an expression', () => {
    const draft = { ...sized, fields: [{ name: 'code', type: 'integer' }, sized.fields[1], sized.fields[2]] }
    const questions = fieldQuestions(sized, draft, origins, ['code'])

    expect(applyAnswers(sized, draft, origins, questions, [{ value: 'code' }]).changes).toEqual([
      { type: 'convert', field: 'code', value: 'code' },
    ])
    expect(applyAnswers(sized, draft, origins, questions, ['skip']).changes).toEqual([])
  })
})

describe('fieldWarnings', () => {
  const before = {
    name: 'Order',
    fields: [
      { name: 'a', type: 'text' },
      { name: 'b', type: 'enum', values: ['x'] },
      { name: 'c', type: 'text' },
      { name: 'd', type: 'text' },
    ],
  }

  it('warns for a type change to or from enum and for unique added, by draft row', () => {
    const draft = {
      ...before,
      fields: [
        { name: 'a', type: 'enum', values: ['y'] },
        { name: 'b', type: 'text' },
        { name: 'c', type: 'text', unique: true },
        { name: 'd', type: 'integer' },
        { name: 'e', type: 'enum', unique: true },
      ],
    }

    expect(fieldWarnings(before, draft, ['a', 'b', 'c', 'd', null])).toEqual(
      new Map([
        [0, 'enum'],
        [1, 'enum'],
        [2, 'unique'],
      ]),
    )
  })

  it('warns for nothing on an unchanged entity', () => {
    expect(fieldWarnings(before, before, loadedOrigins(before)).size).toBe(0)
  })
})

describe('matchOrigins', () => {
  it('matches rows to loaded fields by name only, and each loaded field once', () => {
    const draft = withFields(loaded.fields[1], { name: 'remark', type: 'text' }, loaded.fields[1])

    expect(matchOrigins(loaded, draft)).toEqual(['note', null, null])
  })
})

describe('applyAnswers', () => {
  it('puts a kept field back at its loaded place and names the migration changes in question order', () => {
    const draft = withFields(loaded.fields[0], { name: 'remark', type: 'text' }, loaded.fields[2])
    const questions = fieldQuestions(loaded, draft, ['number', 'note', 'lines'], ['note', 'status'])

    const outcome = applyAnswers(loaded, draft, ['number', 'note', 'lines'], questions, ['keep', 'keep'])

    expect(outcome.changes).toEqual([{ type: 'renameField', from: 'note', to: 'remark' }])
    expect(outcome.draft.fields).toEqual([
      loaded.fields[0],
      { name: 'remark', type: 'text' },
      loaded.fields[2],
      loaded.fields[3],
    ])
    expect(outcome.origins).toEqual(['number', 'note', 'lines', 'status'])
  })

  it('drops the old name when a rename answers Drop data', () => {
    const draft = withFields(loaded.fields[0], { name: 'remark', type: 'text' }, loaded.fields[2], loaded.fields[3])
    const questions = fieldQuestions(loaded, draft, loadedOrigins(loaded), [])

    expect(applyAnswers(loaded, draft, loadedOrigins(loaded), questions, ['drop']).changes).toEqual([
      { type: 'dropField', field: 'note' },
    ])
  })
})

describe('migrationName', () => {
  it('is the entity, Change and the UTC time with fixed widths', () => {
    expect(migrationName('Order', new Date(Date.UTC(2026, 9, 21, 3, 4, 5)))).toBe('OrderChange20261021030405')
    expect(migrationFile('PurchaseRequestChange20261020093000')).toBe(
      'migrations/purchase-request-change20261020093000.json',
    )
  })

  it('forms the resource with the entity and the changes', () => {
    const now = new Date(Date.UTC(2026, 0, 2, 3, 4, 5))

    expect(migrationResource('Order', [{ type: 'dropField', field: 'note' }], now, 'the-id')).toEqual({
      id: 'the-id',
      kind: 'migration',
      name: 'OrderChange20260102030405',
      formatVersion: 1,
      entity: 'Order',
      changes: [{ type: 'dropField', field: 'note' }],
    })
  })
})
