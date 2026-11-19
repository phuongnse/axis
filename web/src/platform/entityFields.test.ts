import { describe, expect, it } from 'vitest'
import entitySchema from '../../../src/Axis.Configuration/Schemas/entity.schema.json'
import {
  changeFieldType,
  editableSettings,
  fieldKeysEditedElsewhere,
  fieldSettingNames,
  fieldSettings,
  fieldTypes,
  valueSourceOf,
  valueSourcesOf,
  withValueSource,
  type FieldType,
} from './entityFields'

const table: Record<FieldType, string[]> = {
  text: [
    'required',
    'unique',
    'maxLength',
    'minLength',
    'expression',
    'sequence',
    'creator',
    'userId',
    'searchable',
    'default',
    'sensitive',
    'exactLookup',
  ],
  integer: ['required', 'unique', 'min', 'max', 'expression', 'default', 'sensitive', 'exactLookup'],
  decimal: [
    'required',
    'unique',
    'min',
    'max',
    'precision',
    'scale',
    'expression',
    'default',
    'sensitive',
    'exactLookup',
  ],
  boolean: ['required', 'unique', 'expression', 'default', 'sensitive', 'exactLookup'],
  date: ['required', 'unique', 'min', 'max', 'expression', 'default', 'sensitive', 'exactLookup'],
  'date-time': ['required', 'unique', 'min', 'max', 'expression', 'default', 'sensitive', 'exactLookup'],
  enum: ['required', 'unique', 'values', 'valueLabels', 'expression', 'default', 'sensitive', 'exactLookup'],
  reference: ['required', 'unique', 'target', 'match'],
  references: ['required', 'target'],
  'child-collection': ['target'],
  money: [
    'required',
    'unique',
    'min',
    'max',
    'precision',
    'scale',
    'currency',
    'currencyField',
    'expression',
    'default',
    'sensitive',
    'exactLookup',
  ],
  file: ['required', 'maxSize', 'accept'],
}

const schemaField = entitySchema.$defs.field.properties

describe('entity.schema.json', () => {
  it('lists the same field types as the designer', () => {
    expect([...fieldTypes].sort()).toEqual([...schemaField.type.enum].sort())
  })

  it('has no field property that the designer neither edits nor lists as edited elsewhere', () => {
    const known = new Set<string>([...fieldSettingNames, ...Object.keys(fieldKeysEditedElsewhere)])
    for (const key of Object.keys(schemaField)) {
      expect(
        known.has(key),
        `The field property "${key}" is not in fieldSettingNames or fieldKeysEditedElsewhere`,
      ).toBe(true)
    }
  })

  it('has every setting and every key edited elsewhere as a field property', () => {
    for (const key of [...fieldSettingNames, ...Object.keys(fieldKeysEditedElsewhere)]) {
      expect(Object.hasOwn(schemaField, key), `"${key}" is not a field property of the schema`).toBe(true)
    }
  })
})

describe('fieldSettings', () => {
  it.each(fieldTypes)('lists the settings of %s in the order of the field type table', (type) => {
    expect(fieldSettings(type)).toEqual(table[type])
  })

  it('takes no settings for an unknown type', () => {
    expect(fieldSettings('blob')).toEqual([])
    expect(fieldSettings('toString')).toEqual([])
  })
})

describe('fieldKeysEditedElsewhere', () => {
  it('holds only the name, the type, the label and the help', () => {
    expect(Object.keys(fieldKeysEditedElsewhere)).toEqual(['name', 'type', 'label', 'help'])
  })
})

describe('editableSettings', () => {
  const enteredOnly = ['required', 'default', 'userId', 'min', 'max', 'minLength']
  const ways = ['expression', 'sequence', 'creator', 'match']
  const without = (settings: string[], hidden: string[]) => settings.filter((setting) => !hidden.includes(setting))

  it('offers the settings of an entered value, and none of the ways, which are chosen first', () => {
    expect(editableSettings({ name: 'Code', type: 'text', maxLength: 5 })).toEqual(
      without(table.text, ['creator', 'exactLookup', 'expression', 'sequence']),
    )
    expect(editableSettings({ name: 'Code', type: 'text', creator: false })).toEqual(
      without(table.text, ['creator', 'exactLookup', 'expression', 'sequence']),
    )
  })

  it('offers the match of a reference only once it is matched, and the target and Required always', () => {
    expect(editableSettings({ name: 'Owner', type: 'reference' })).toEqual(['required', 'unique', 'target'])
    expect(editableSettings({ name: 'Owner', type: 'reference', match: { field: '', from: '' } })).toEqual([
      'unique',
      'target',
      'match',
    ])
  })

  it.each([
    ['an expression', { expression: 'a' }, 'expression'],
    ['a sequence', { sequence: 'Numbers' }, 'sequence'],
    ['creator', { creator: true }, undefined],
  ])('offers only the settings of %s, and not Required, default, min, max or minLength', (_, extra, way) => {
    const text = editableSettings({ name: 'Code', type: 'text', ...extra })
    expect(text).toEqual(
      table.text.filter(
        (setting) =>
          setting !== 'exactLookup' && !enteredOnly.includes(setting) && (setting === way || !ways.includes(setting)),
      ),
    )
    expect(text).not.toContain('userId')
    expect(editableSettings({ name: 'N', type: 'integer', ...extra })).toEqual(
      table.integer.filter(
        (setting) =>
          setting !== 'exactLookup' && !enteredOnly.includes(setting) && (setting === way || !ways.includes(setting)),
      ),
    )
  })

  it('offers Exact lookup only once the field is sensitive', () => {
    expect(editableSettings({ name: 'Code', type: 'text', sensitive: true })).toEqual(
      without(table.text, ['creator', 'expression', 'sequence']),
    )
    expect(editableSettings({ name: 'Code', type: 'text', sensitive: false })).not.toContain('exactLookup')
  })

  it('takes no settings for an unknown type or a field without a type', () => {
    expect(editableSettings({ name: 'Code', type: 'blob' })).toEqual([])
    expect(editableSettings({ name: 'Code' })).toEqual([])
  })
})

describe('valueSourcesOf', () => {
  it.each([
    ['text', ['entered', 'computed', 'sequence', 'creator']],
    ['integer', ['entered', 'computed']],
    ['decimal', ['entered', 'computed']],
    ['boolean', ['entered', 'computed']],
    ['date', ['entered', 'computed']],
    ['enum', ['entered', 'computed']],
    ['money', ['entered', 'computed']],
    ['reference', ['entered', 'matched']],
    ['references', ['entered']],
    ['file', ['entered']],
  ])('lists the ways a %s field takes', (type, expected) => {
    expect(valueSourcesOf(type)).toEqual(expected)
  })

  it('takes none for an unknown type', () => {
    expect(valueSourcesOf('blob')).toEqual([])
    expect(valueSourcesOf('toString')).toEqual([])
  })
})

describe('valueSourceOf', () => {
  it.each([
    [{ expression: 'a' }, 'computed'],
    [{ expression: '' }, 'computed'],
    [{ sequence: 'Numbers' }, 'sequence'],
    [{ creator: true }, 'creator'],
    [{ match: { field: 'code', from: 'code' } }, 'matched'],
    [{ creator: false }, 'entered'],
    [{ match: 'x' }, 'entered'],
    [{ default: "'a'", userId: true }, 'entered'],
    [{}, 'entered'],
  ])('reads %j as %s', (extra, expected) => {
    expect(valueSourceOf({ name: 'Code', type: 'text', ...extra })).toBe(expected)
  })
})

describe('withValueSource', () => {
  const entered = {
    name: 'Code',
    required: true,
    type: 'text',
    default: "'a'",
    userId: true,
    minLength: 2,
    min: 1,
    max: 5,
    maxLength: 20,
    searchable: true,
    note: 'keep',
  }

  it.each([
    ['computed', { expression: '' }],
    ['sequence', { sequence: '' }],
    ['creator', { creator: true }],
  ] as const)('drops the keys of an entered value and writes the empty key of %s', (source, written) => {
    const next = withValueSource(entered, source)
    expect(next).toEqual({ name: 'Code', type: 'text', maxLength: 20, searchable: true, note: 'keep', ...written })
    expect(Object.keys(next)).toEqual(['name', 'type', 'maxLength', 'searchable', 'note', Object.keys(written)[0]])
  })

  it('writes an empty match for Matched and drops the keys of an entered value', () => {
    const owner = { name: 'Owner', required: true, type: 'reference', target: 'User', unique: true }
    expect(withValueSource(owner, 'matched')).toEqual({
      name: 'Owner',
      type: 'reference',
      target: 'User',
      unique: true,
      match: { field: '', from: '' },
    })
  })

  it('drops the other ways when the way changes, and keeps the keys in order', () => {
    const computed = { name: 'Code', expression: 'a', type: 'text', sequence: 'N', creator: true, note: 'keep' }
    const next = withValueSource(computed, 'entered')
    expect(next).toEqual({ name: 'Code', type: 'text', note: 'keep' })
    expect(Object.keys(next)).toEqual(['name', 'type', 'note'])
    expect(withValueSource({ name: 'Code', type: 'text', expression: 'a' }, 'sequence')).toEqual({
      name: 'Code',
      type: 'text',
      sequence: '',
    })
    expect(withValueSource({ name: 'Owner', type: 'reference', match: { field: 'a', from: 'b' } }, 'entered')).toEqual({
      name: 'Owner',
      type: 'reference',
    })
  })

  it('keeps the keys of an entered value when the way becomes entered again', () => {
    expect(
      withValueSource({ name: 'Code', type: 'text', expression: 'a', default: "'a'", required: true }, 'entered'),
    ).toEqual({ name: 'Code', type: 'text', default: "'a'", required: true })
  })

  it('returns the field as it is when it has the way already', () => {
    const field = { name: 'Code', type: 'text', expression: 'a', default: "'a'" }
    expect(withValueSource(field, 'computed')).toBe(field)
    expect(withValueSource(entered, 'entered')).toBe(entered)
  })
})

describe('changeFieldType', () => {
  it('keeps the shared settings and drops the ones the new type does not take', () => {
    const field = { name: 'Amount', type: 'decimal', precision: 10, scale: 2, unique: true }
    expect(changeFieldType(field, 'enum')).toEqual({ name: 'Amount', type: 'enum', unique: true })
  })

  it('keeps name, label and unknown keys in their original order', () => {
    const field = {
      name: 'Lines',
      label: { textKey: 'x' },
      type: 'text',
      maxLength: 50,
      required: true,
      creator: true,
      note: 'keep',
    }
    const next = changeFieldType(field, 'child-collection')
    expect(next).toEqual({ name: 'Lines', label: { textKey: 'x' }, type: 'child-collection', note: 'keep' })
    expect(Object.keys(next)).toEqual(['name', 'label', 'type', 'note'])
  })

  it('drops every known setting when the new type is unknown', () => {
    const field = { name: 'Amount', type: 'decimal', precision: 10, unique: true, note: 'keep' }
    expect(changeFieldType(field, 'blob')).toEqual({ name: 'Amount', type: 'blob', note: 'keep' })
  })

  it('drops the value labels when an enum becomes text', () => {
    const field = { name: 'State', type: 'enum', values: ['open'], valueLabels: { open: { textKey: 'state.open' } } }
    expect(changeFieldType(field, 'text')).toEqual({ name: 'State', type: 'text' })
  })

  it('keeps min and max when the limit keeps its format and drops them when it changes', () => {
    const field = { name: 'Count', type: 'integer', min: 0, max: 10, default: '1' }
    expect(changeFieldType(field, 'decimal')).toEqual({ name: 'Count', type: 'decimal', min: 0, max: 10, default: '1' })
    expect(changeFieldType(field, 'money')).toEqual({ name: 'Count', type: 'money', min: 0, max: 10, default: '1' })
    expect(changeFieldType(field, 'date')).toEqual({ name: 'Count', type: 'date', default: '1' })
    const day = { name: 'Day', type: 'date', min: '2026-01-01' }
    expect(changeFieldType(day, 'date-time')).toEqual({ name: 'Day', type: 'date-time' })
    expect(changeFieldType(day, 'date')).toEqual(day)
  })

  it('keeps the default, the minimum length and searchable that the new type takes', () => {
    const field = { name: 'Code', type: 'text', minLength: 2, searchable: true, default: "'x'" }
    expect(changeFieldType(field, 'text')).toEqual(field)
    expect(changeFieldType(field, 'enum')).toEqual({ name: 'Code', type: 'enum', default: "'x'" })
  })

  it('drops the match when a reference becomes text, and the user id when text becomes a reference', () => {
    const owner = { name: 'Owner', type: 'reference', target: 'User', match: { field: 'a', from: 'b' } }
    expect(changeFieldType(owner, 'text')).toEqual({ name: 'Owner', type: 'text' })
    expect(changeFieldType({ name: 'Who', type: 'text', userId: true }, 'reference')).toEqual({
      name: 'Who',
      type: 'reference',
    })
    expect(changeFieldType({ name: 'Who', type: 'text', userId: true }, 'text')).toEqual({
      name: 'Who',
      type: 'text',
      userId: true,
    })
  })

  it('sets the type of a field that has none', () => {
    expect(changeFieldType({ name: 'Code' }, 'text')).toEqual({ name: 'Code', type: 'text' })
  })
})
