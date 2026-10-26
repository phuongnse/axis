/** The field types of an entity, as `entity.schema.json` lists them. */
export const fieldTypes = [
  'text',
  'integer',
  'decimal',
  'boolean',
  'date',
  'date-time',
  'enum',
  'reference',
  'references',
  'child-collection',
  'money',
  'file',
] as const

export type FieldType = (typeof fieldTypes)[number]

/** Every type-specific setting a field may hold. A type takes only some of them, and the others are refused. */
export const fieldSettingNames = [
  'required',
  'unique',
  'maxLength',
  'precision',
  'scale',
  'maxSize',
  'accept',
  'target',
  'currency',
  'currencyField',
  'values',
  'valueLabels',
  'expression',
  'sequence',
  'creator',
  'sensitive',
] as const

export type FieldSetting = (typeof fieldSettingNames)[number]

/** The settings each type takes, in the column order of the field type table in the configuration reference. */
export const fieldTypeSettings: Record<FieldType, FieldSetting[]> = {
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

/** The field keys of `entity.schema.json` that are not settings, and where each is edited. */
export const fieldKeysEditedElsewhere: Record<string, string> = {
  name: 'the name column',
  type: 'the type column',
  label: 'the label column',
  minLength: 'the JSON tab, until the designer adds it (D77)',
  min: 'the JSON tab, until the designer adds it (D77)',
  max: 'the JSON tab, until the designer adds it (D77)',
  default: 'the JSON tab, until the designer adds it (D77)',
  searchable: 'the JSON tab, until the designer adds it (D80)',
}

/** The settings a field type takes. An unknown type takes none. */
export function fieldSettings(type: string): FieldSetting[] {
  return Object.hasOwn(fieldTypeSettings, type) ? fieldTypeSettings[type as FieldType] : []
}

function isFieldSetting(key: string): key is FieldSetting {
  return (fieldSettingNames as readonly string[]).includes(key)
}

/**
 * The field with a new type. It drops the settings the new type does not take, because the compiler refuses them,
 * and keeps the shared ones, `name`, `label` and every key it does not know.
 */
export function changeFieldType(field: Record<string, unknown>, type: string): Record<string, unknown> {
  const allowed = fieldSettings(type)
  const next: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(field)) {
    if (key === 'type') {
      next.type = type
    } else if (!isFieldSetting(key) || allowed.includes(key)) {
      next[key] = value
    }
  }
  if (!Object.hasOwn(next, 'type')) {
    next.type = type
  }
  return next
}
