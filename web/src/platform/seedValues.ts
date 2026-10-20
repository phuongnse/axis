/** A field of the seed's entity, as the entity file declares it. Only what the seed grid reads. */
export interface SeedField {
  name: string
  type: string
  required?: boolean
  maxLength?: number
  precision?: number
  scale?: number
  values?: string[]
  expression?: string
  sequence?: string
  creator?: boolean
}

const uuidForm = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const dateForm = /^(\d{4})-(\d{2})-(\d{2})$/
const dateTimeForm = /^(\d{4}-\d{2}-\d{2})[Tt](\d{2}):(\d{2}):(\d{2})(?:\.\d{1,6})?(?:[Zz]|[+-](\d{2}):(\d{2}))$/
const integerText = /^-?\d+$/
const decimalText = /^-?\d+(\.\d+)?$/
// The signed 64-bit range of `long`. As a double, 2^63 is the first value above it.
const longLimit = 2 ** 63

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * The fields the seed grid shows as columns. A seed may not set a computed, sequence or creator field, and a
 * child collection stays as the file has it.
 */
export function seedColumns(fields: SeedField[]): SeedField[] {
  return fields.filter(
    (field) => field.type !== 'child-collection' && !field.expression && !field.sequence && field.creator !== true,
  )
}

/** The JSON Pointer of a field's value in the seed's `records` array. */
export function seedValuePointer(index: number, name: string): string {
  return `/records/${index}/values/${name.replaceAll('~', '~0').replaceAll('/', '~1')}`
}

function isRealDate(year: number, month: number, day: number): boolean {
  if (year < 1 || month < 1 || month > 12 || day < 1) {
    return false
  }
  const leap = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return day <= days[month - 1]
}

/** The digits of a finite number as the record API counts them: after the exponent, without leading or trailing zeros. */
function decimalDigits(value: number): { integer: number; fraction: number } {
  const match = /^(\d+)(?:\.(\d+))?(?:e([+-]\d+))?$/.exec(String(Math.abs(value)))
  if (!match) {
    return { integer: 0, fraction: 0 }
  }
  const [, whole, part = '', exponent = '0'] = match
  const digits = whole + part
  const point = whole.length + Number(exponent)
  const integerDigits = point <= 0 ? '' : digits.slice(0, point).padEnd(point, '0')
  const fractionDigits =
    point >= digits.length ? '' : '0'.repeat(Math.max(-point, 0)) + digits.slice(Math.max(point, 0))
  return {
    integer: integerDigits.replace(/^0+/, '').length,
    fraction: fractionDigits.replace(/0+$/, '').length,
  }
}

/**
 * Why `value` would fail seeding for `field`, as a `shell.studio.seed.*` text key, or `null` when it is fine. The rules
 * are those of the record API, and a value is never coerced between JSON types. A missing or `null` value is a problem
 * only for a required field.
 */
export function seedValueProblem(field: SeedField, value: unknown): string | null {
  if (value === undefined || value === null) {
    return field.required ? 'shell.studio.seed.required' : null
  }
  const wrongType = 'shell.studio.seed.wrongType'
  switch (field.type) {
    case 'text': {
      if (typeof value !== 'string' || value.includes('\0')) {
        return wrongType
      }
      return field.maxLength !== undefined && [...value].length > field.maxLength ? 'shell.studio.seed.tooLong' : null
    }
    case 'integer': {
      if (typeof value !== 'number' || !Number.isInteger(value)) {
        return wrongType
      }
      return value >= longLimit || value < -longLimit ? 'shell.studio.seed.outOfBounds' : null
    }
    case 'decimal': {
      if (typeof value !== 'number' || !Number.isFinite(value)) {
        return wrongType
      }
      if (field.precision === undefined) {
        return null
      }
      const scale = field.scale ?? 0
      const digits = decimalDigits(value)
      return digits.fraction > scale || digits.integer > field.precision - scale
        ? 'shell.studio.seed.outOfBounds'
        : null
    }
    case 'boolean':
      return typeof value === 'boolean' ? null : wrongType
    case 'date': {
      if (typeof value !== 'string') {
        return wrongType
      }
      const match = dateForm.exec(value)
      return match && isRealDate(Number(match[1]), Number(match[2]), Number(match[3]))
        ? null
        : 'shell.studio.seed.invalidDate'
    }
    case 'date-time': {
      if (typeof value !== 'string') {
        return wrongType
      }
      const match = dateTimeForm.exec(value)
      if (!match) {
        return 'shell.studio.seed.invalidDate'
      }
      const [, date, hour, minute, second, offsetHour, offsetMinute] = match
      const [year, month, day] = date.split('-').map(Number)
      const offsetInRange =
        offsetHour === undefined ||
        (Number(offsetMinute) < 60 && Number(offsetHour) * 60 + Number(offsetMinute) <= 14 * 60)
      return isRealDate(year, month, day) &&
        Number(hour) < 24 &&
        Number(minute) < 60 &&
        Number(second) < 60 &&
        offsetInRange
        ? null
        : 'shell.studio.seed.invalidDate'
    }
    case 'enum':
      if (typeof value !== 'string') {
        return wrongType
      }
      return field.values?.includes(value) ? null : 'shell.studio.seed.notInValues'
    case 'reference':
      return typeof value === 'string' && uuidForm.test(value) ? null : wrongType
    default:
      return null
  }
}

/**
 * The problems of the seed's records, keyed by the JSON Pointer of the value, `/records/{i}/values/{field}`. Only the
 * fields in `columns` are checked, because the grid shows only those.
 */
export function seedProblems(records: unknown, columns: SeedField[]): Map<string, string> {
  const problems = new Map<string, string>()
  if (!Array.isArray(records)) {
    return problems
  }
  records.forEach((record, index) => {
    const values = isObject(record) && isObject(record.values) ? record.values : {}
    for (const field of columns) {
      const problem = seedValueProblem(field, Object.hasOwn(values, field.name) ? values[field.name] : undefined)
      if (problem !== null) {
        problems.set(seedValuePointer(index, field.name), problem)
      }
    }
  })
  return problems
}

/** The number a typed text stands for, or the text itself while it is not one, so that it is flagged as the wrong type. */
export function parseNumberText(text: string, type: 'integer' | 'decimal'): number | string {
  const trimmed = text.trim()
  if (!(type === 'integer' ? integerText : decimalText).test(trimmed)) {
    return text
  }
  const number = Number(trimmed)
  return Number.isFinite(number) ? number : text
}
