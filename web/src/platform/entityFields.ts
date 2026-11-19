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
  'userId',
  'match',
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

/** The field keys of `entity.schema.json` that are not settings, and where each is edited. */
export const fieldKeysEditedElsewhere: Record<string, string> = {
  name: 'the field drawer',
  type: 'the field drawer',
  label: 'the field drawer',
  help: 'the field drawer',
}

/** The settings a field type takes. An unknown type takes none. */
export function fieldSettings(type: string): FieldSetting[] {
  return Object.hasOwn(fieldTypeSettings, type) ? fieldTypeSettings[type as FieldType] : []
}

/** Where a field's value comes from, see docs/reference/entity-designer.md#settings. */
export type ValueSource = 'entered' | 'computed' | 'sequence' | 'creator' | 'matched'

/** The key that marks each way a value can come from other than being entered by people. */
const wayKeys = { computed: 'expression', sequence: 'sequence', creator: 'creator', matched: 'match' } as const

/** The keys only an entered value may have, because the compiler refuses them beside another way (`AXC0013`). */
const enteredOnly = ['required', 'default', 'userId', 'min', 'max', 'minLength']

/** The ways a field's type takes: entered by people first, then each way whose key the type takes. An unknown type takes none. */
export function valueSourcesOf(type: string): ValueSource[] {
  const settings = fieldSettings(type)
  if (settings.length === 0) {
    return []
  }
  return [
    'entered',
    ...(Object.entries(wayKeys) as [Exclude<ValueSource, 'entered'>, FieldSetting][])
      .filter(([, key]) => settings.includes(key))
      .map(([source]) => source),
  ]
}

/** The way a field's value comes from: the first of its way keys that holds a value, else entered by people. */
export function valueSourceOf(field: Record<string, unknown>): ValueSource {
  return typeof field.expression === 'string'
    ? 'computed'
    : typeof field.sequence === 'string'
      ? 'sequence'
      : field.creator === true
        ? 'creator'
        : typeof field.match === 'object' && field.match !== null && !Array.isArray(field.match)
          ? 'matched'
          : 'entered'
}

/**
 * The field with a new way for its value. It drops every way's key and, unless the way is entered by people, the keys
 * only an entered value may have, and it writes the empty key of the chosen way, which Validate reports until the author
 * fills it. It keeps every other key in its order. The way the field has already changes nothing.
 */
export function withValueSource(field: Record<string, unknown>, source: ValueSource): Record<string, unknown> {
  if (valueSourceOf(field) === source) {
    return field
  }
  const ways: string[] = Object.values(wayKeys)
  const next: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(field)) {
    if (!ways.includes(key) && (source === 'entered' || !enteredOnly.includes(key))) {
      setOwn(next, key, value)
    }
  }
  if (source !== 'entered') {
    setOwn(
      next,
      wayKeys[source],
      source === 'computed' || source === 'sequence' ? '' : source === 'creator' ? true : { field: '', from: '' },
    )
  }
  return next
}

/**
 * The settings the designer offers for a field, as docs/reference/entity-designer.md#settings says. A way other than the
 * one the field has is not offered, and `creator` only as a way. Required, `default`, `userId`, `min`, `max` and
 * `minLength` show only for an entered value, and `exactLookup` only once the field is sensitive. A key already in the
 * file that a setting hides stays there, and Validate reports it.
 */
export function editableSettings(field: Record<string, unknown>): FieldSetting[] {
  const source = valueSourceOf(field)
  const ways: string[] = Object.values(wayKeys)
  const own = source === 'entered' ? undefined : wayKeys[source]
  return fieldSettings(typeof field.type === 'string' ? field.type : '').filter(
    (setting) =>
      setting !== 'creator' &&
      (setting === own || !ways.includes(setting)) &&
      (source === 'entered' || !enteredOnly.includes(setting)) &&
      (setting !== 'exactLookup' || field.sensitive === true),
  )
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
