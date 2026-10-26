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
  'minLength',
  'min',
  'max',
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
  'searchable',
  'default',
  'sensitive',
] as const

export type FieldSetting = (typeof fieldSettingNames)[number]

/** The settings each type takes, in the column order of the field type table in the configuration reference. */
export const fieldTypeSettings: Record<FieldType, FieldSetting[]> = {
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

/** The field keys of `entity.schema.json` that are not settings, and where each is edited. */
export const fieldKeysEditedElsewhere: Record<string, string> = {
  name: 'the name column',
  type: 'the type column',
  label: 'the label column',
}

/** The settings a field type takes. An unknown type takes none. */
export function fieldSettings(type: string): FieldSetting[] {
  return Object.hasOwn(fieldTypeSettings, type) ? fieldTypeSettings[type as FieldType] : []
}

const valueSettings: FieldSetting[] = ['default', 'min', 'max', 'minLength']

/**
 * The settings the designer offers for a field. A field that computes its value with an `expression`, a `sequence` or
 * `creator` takes no `default`, `min`, `max` or `minLength`, because the compiler refuses that combination. A key
 * already in the file stays there, and Validate reports it.
 */
export function editableSettings(field: Record<string, unknown>): FieldSetting[] {
  const settings = fieldSettings(typeof field.type === 'string' ? field.type : '')
  const computed = typeof field.expression === 'string' || typeof field.sequence === 'string' || field.creator === true
  return computed ? settings.filter((setting) => !valueSettings.includes(setting)) : settings
}

/** The format of a `min` or `max`: a number, a date or a date-time. Other types take no limit. */
function limitKind(type: string): string | undefined {
  return type === 'integer' || type === 'decimal' || type === 'money'
    ? 'number'
    : type === 'date' || type === 'date-time'
      ? type
      : undefined
}

function isFieldSetting(key: string): key is FieldSetting {
  return (fieldSettingNames as readonly string[]).includes(key)
}

/**
 * The field with a new type. It drops the settings the new type does not take, because the compiler refuses them,
 * and `min` and `max` when the limit changes format, because a number is not a valid date. It keeps the shared ones,
 * `default`, `name`, `label` and every key it does not know.
 */
export function changeFieldType(field: Record<string, unknown>, type: string): Record<string, unknown> {
  const allowed = fieldSettings(type)
  const sameLimit = limitKind(typeof field.type === 'string' ? field.type : '') === limitKind(type)
  const next: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(field)) {
    if (key === 'type') {
      next.type = type
    } else if ((key === 'min' || key === 'max') && !sameLimit) {
      continue
    } else if (!isFieldSetting(key) || allowed.includes(key)) {
      next[key] = value
    }
  }
  if (!Object.hasOwn(next, 'type')) {
    next.type = type
  }
  return next
}
