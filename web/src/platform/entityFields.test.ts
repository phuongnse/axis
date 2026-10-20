import { describe, expect, it } from 'vitest'
import { changeFieldType, fieldSettings, fieldTypes, type FieldType } from './entityFields'

const table: Record<FieldType, string[]> = {
  text: ['required', 'unique', 'maxLength', 'expression', 'sequence', 'creator', 'sensitive'],
  integer: ['required', 'unique', 'expression', 'sensitive'],
  decimal: ['required', 'unique', 'precision', 'scale', 'expression', 'sensitive'],
  boolean: ['required', 'unique', 'expression', 'sensitive'],
  date: ['required', 'unique', 'expression', 'sensitive'],
  'date-time': ['required', 'unique', 'expression', 'sensitive'],
  enum: ['required', 'unique', 'values', 'expression', 'sensitive'],
  reference: ['required', 'unique', 'target', 'sensitive'],
  'child-collection': ['target'],
}

describe('fieldSettings', () => {
  it.each(fieldTypes)('lists the settings of %s in the order of the field type table', (type) => {
    expect(fieldSettings(type)).toEqual(table[type])
  })

  it('takes no settings for an unknown type', () => {
    expect(fieldSettings('money')).toEqual([])
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
    expect(changeFieldType(field, 'money')).toEqual({ name: 'Amount', type: 'money', note: 'keep' })
  })

  it('sets the type of a field that has none', () => {
    expect(changeFieldType({ name: 'Code' }, 'text')).toEqual({ name: 'Code', type: 'text' })
  })
})
