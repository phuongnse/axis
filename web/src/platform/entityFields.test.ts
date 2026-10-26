import { describe, expect, it } from 'vitest'
import entitySchema from '../../../src/Axis.Configuration/Schemas/entity.schema.json'
import {
  changeFieldType,
  fieldKeysEditedElsewhere,
  fieldSettingNames,
  fieldSettings,
  fieldTypes,
  type FieldType,
} from './entityFields'

const table: Record<FieldType, string[]> = {
  text: ['required', 'unique', 'maxLength', 'expression', 'sequence', 'creator', 'sensitive'],
  integer: ['required', 'unique', 'expression', 'sensitive'],
  decimal: ['required', 'unique', 'precision', 'scale', 'expression', 'sensitive'],
  boolean: ['required', 'unique', 'expression', 'sensitive'],
  date: ['required', 'unique', 'expression', 'sensitive'],
  'date-time': ['required', 'unique', 'expression', 'sensitive'],
  enum: ['required', 'unique', 'values', 'valueLabels', 'expression', 'sensitive'],
  reference: ['required', 'unique', 'target', 'sensitive'],
  references: ['required', 'target', 'sensitive'],
  'child-collection': ['target'],
  money: ['required', 'unique', 'precision', 'scale', 'currency', 'currencyField', 'expression', 'sensitive'],
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

  it('sets the type of a field that has none', () => {
    expect(changeFieldType({ name: 'Code' }, 'text')).toEqual({ name: 'Code', type: 'text' })
  })
})
