import { describe, expect, it } from 'vitest'
import { recordName } from './recordName'
import type { RecordItem } from './records'
import type { EntityMetadata, FieldMetadata } from './site'

function field(name: string, type: FieldMetadata['type']): FieldMetadata {
  return {
    name,
    type,
    labelKey: null,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields: null,
  }
}

const entity: EntityMetadata = {
  name: 'Note',
  labelKey: null,
  displayField: 'title',
  recordsPath: '/api/apps/E2eApp/entities/Note/records',
  fields: [field('title', 'text'), field('done', 'boolean')],
}

function recordWith(values: RecordItem['values']): RecordItem {
  return { id: '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7', version: 1, values, labels: {} }
}

const options = { locale: 'en', text: (key: string) => key }

describe('recordName', () => {
  it("gives the display field's value as the record's name", () => {
    expect(recordName(entity, recordWith({ title: 'Buy paper' }), options)).toBe('Buy paper')
  })

  it('formats the value as a table cell does, so a boolean shows its word', () => {
    const done = { ...entity, displayField: 'done' }

    expect(recordName(done, recordWith({ done: true }), options)).toBe('shell.table.yes')
  })

  it('gives no name when the entity has no display field', () => {
    expect(recordName({ ...entity, displayField: null }, recordWith({ title: 'Buy paper' }), options)).toBeNull()
  })

  it('gives no name when the display field is not one of the entity fields', () => {
    expect(recordName({ ...entity, displayField: 'code' }, recordWith({ code: 'A1' }), options)).toBeNull()
  })

  it('gives no name when the record leaves the display field out, as for a field the person may not read', () => {
    expect(recordName(entity, recordWith({}), options)).toBeNull()
  })

  it('gives no name for a null value', () => {
    expect(recordName(entity, recordWith({ title: null }), options)).toBeNull()
  })

  it('gives no name for an empty or blank value, so the page keeps its title', () => {
    expect(recordName(entity, recordWith({ title: '' }), options)).toBeNull()
    expect(recordName(entity, recordWith({ title: '   ' }), options)).toBeNull()
  })
})
