import { setOwn } from './ownProperty'

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
  'exactLookup',
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
  reference: ['required', 'unique', 'target'],
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

/** The field keys of `entity.schema.json` that are not settings, and where each is edited. */
export const fieldKeysEditedElsewhere: Record<string, string> = {
  name: 'the name column',
  type: 'the type column',
  label: 'the label column',
  help: 'the settings column',
  match: 'the value group',
  userId: 'the value group',
}

/** The settings a field type takes. An unknown type takes none. */
export function fieldSettings(type: string): FieldSetting[] {
  return Object.hasOwn(fieldTypeSettings, type) ? fieldTypeSettings[type as FieldType] : []
}

const valueSettings: FieldSetting[] = ['default', 'min', 'max', 'minLength']

/**
 * Proposed: where a record gets a field's value. People enter it, or the server sets it: computed from an expression,
 * numbered from a sequence, the user who creates the record, or a matched reference found by a unique field. The ways
 * exclude one another, as the compiler refuses two of them on a field (AXC0013).
 */
export type ValueSource = 'entered' | 'computed' | 'sequence' | 'creator' | 'matched'

/** Proposed: the way a field gets its value, as its file says. */
export function valueSourceOf(field: Record<string, unknown>): ValueSource {
  if (typeof field.expression === 'string') {
    return 'computed'
  }
  if (typeof field.sequence === 'string') {
    return 'sequence'
  }
  if (field.creator === true) {
    return 'creator'
  }
  if (typeof field.match === 'object' && field.match !== null) {
    return 'matched'
  }
  return 'entered'
}

/** Proposed: the ways a field of `type` may get its value, entered first. A type with one way offers no choice. */
export function valueSources(type: string): ValueSource[] {
  const settings = fieldSettings(type)
  return [
    'entered',
    ...(settings.includes('expression') ? (['computed'] as const) : []),
    ...(settings.includes('sequence') ? (['sequence'] as const) : []),
    ...(settings.includes('creator') ? (['creator'] as const) : []),
    ...(type === 'reference' ? (['matched'] as const) : []),
  ]
}

/** The keys that say where a value comes from, and those only a value people enter may have. */
const sourceKeys = ['expression', 'sequence', 'creator', 'match']
const enteredKeys = ['default', 'userId', 'required', 'min', 'max', 'minLength']

/**
 * Proposed: the field with its value from `source`. It drops the keys of the other ways, and for a value the server
 * sets, the ones only an entered value may have, `required`, `default`, `userId` and the limits, as the compiler refuses
 * them together. The new way starts empty, for the author to fill: an empty expression, sequence or match.
 */
export function withValueSource(field: Record<string, unknown>, source: ValueSource): Record<string, unknown> {
  const next: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(field)) {
    if (!sourceKeys.includes(key) && (source === 'entered' || !enteredKeys.includes(key))) {
      setOwn(next, key, value)
    }
  }
  switch (source) {
    case 'computed':
      next.expression = typeof field.expression === 'string' ? field.expression : ''
      break
    case 'sequence':
      next.sequence = typeof field.sequence === 'string' ? field.sequence : ''
      break
    case 'creator':
      next.creator = true
      break
    case 'matched':
      next.match = typeof field.match === 'object' && field.match !== null ? field.match : { field: '', from: '' }
      break
  }
  return next
}

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
      setOwn(next, key, value)
    }
  }
  if (!Object.hasOwn(next, 'type')) {
    next.type = type
  }
  return next
}
