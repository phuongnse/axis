import { describe, expect, it } from 'vitest'
import {
  applyAnswers,
  fieldQuestions,
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
