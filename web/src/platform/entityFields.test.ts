import { describe, expect, it } from 'vitest'
import entitySchema from '../../../src/Axis.Configuration/Schemas/entity.schema.json'
import {
  changeFieldType,
  editableSettings,
  fieldKeysEditedElsewhere,
  fieldSettingNames,
  fieldSettings,
  fieldTypes,
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
    'searchable',
    'default',
    'sensitive',
  ],
  integer: ['required', 'unique', 'min', 'max', 'expression', 'default', 'sensitive'],
  decimal: ['required', 'unique', 'min', 'max', 'precision', 'scale', 'expression', 'default', 'sensitive'],
  boolean: ['required', 'unique', 'expression', 'default', 'sensitive'],
  date: ['required', 'unique', 'min', 'max', 'expression', 'default', 'sensitive'],
  'date-time': ['required', 'unique', 'min', 'max', 'expression', 'default', 'sensitive'],
  enum: ['required', 'unique', 'values', 'valueLabels', 'expression', 'default', 'sensitive'],
  reference: ['required', 'unique', 'target', 'sensitive'],
  references: ['required', 'target', 'sensitive'],
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
  ],
  file: ['required', 'maxSize', 'accept', 'sensitive'],
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
  it('holds only the name, the type and the label', () => {
    expect(Object.keys(fieldKeysEditedElsewhere)).toEqual(['name', 'type', 'label'])
  })
})

describe('editableSettings', () => {
  const hidden = ['default', 'min', 'max', 'minLength']

  it('offers every setting of the type for a field that computes nothing', () => {
    expect(editableSettings({ name: 'Code', type: 'text', maxLength: 5 })).toEqual(table.text)
    expect(editableSettings({ name: 'Code', type: 'text', creator: false })).toEqual(table.text)
  })

  it.each([
    ['an expression', { expression: 'a' }],
    ['a sequence', { sequence: 'Numbers' }],
    ['creator', { creator: true }],
  ])('hides default, min, max and minLength next to %s', (_, extra) => {
    const settings = editableSettings({ name: 'Code', type: 'text', ...extra })
    expect(settings).toEqual(table.text.filter((setting) => !hidden.includes(setting)))
    expect(editableSettings({ name: 'N', type: 'integer', ...extra })).toEqual(
      table.integer.filter((setting) => !hidden.includes(setting)),
    )
  })

  it('takes no settings for an unknown type or a field without a type', () => {
    expect(editableSettings({ name: 'Code', type: 'blob' })).toEqual([])
    expect(editableSettings({ name: 'Code' })).toEqual([])
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

  it('sets the type of a field that has none', () => {
    expect(changeFieldType({ name: 'Code' }, 'text')).toEqual({ name: 'Code', type: 'text' })
  })
})
