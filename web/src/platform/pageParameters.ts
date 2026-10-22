import type { PageParameter } from './site'

const int64Min = -(2n ** 63n)
const int64Max = 2n ** 63n - 1n

// [0-9] rather than \d keeps non-ASCII digits out, as the server does.
const integerPattern = /^-?[0-9]+$/
const decimalPattern = /^-?[0-9]+(\.[0-9]+)?$/
const datePattern = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/
const dateTimePattern =
  /^([0-9]{4}-[0-9]{2}-[0-9]{2})[Tt]([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.[0-9]{1,6})?(?:[Zz]|[+-]([0-9]{2}):([0-9]{2}))$/
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

// The most an offset may be from UTC, in minutes, as the server accepts it.
const maxOffsetMinutes = 14 * 60

function isRealDate(text: string): boolean {
  const match = datePattern.exec(text)
  if (!match) {
    return false
  }
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1) {
    return false
  }
  const leap = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return day <= days[month - 1]
}

function isDateTime(text: string): boolean {
  const match = dateTimePattern.exec(text)
  if (!match || !isRealDate(match[1])) {
    return false
  }
  const [hour, minute, second] = [Number(match[2]), Number(match[3]), Number(match[4])]
  if (hour > 23 || minute > 59 || second > 59) {
    return false
  }
  if (match[5] === undefined) {
    return true
  }
  const offsetMinute = Number(match[6])
  return offsetMinute <= 59 && Number(match[5]) * 60 + offsetMinute <= maxOffsetMinutes
}

/**
 * Whether `value` has the plain text form of the parameter's type, which is the form the data sources
 * query values use (see data-sources.md). The server checks the same forms on the rows request.
 */
function isWellFormed(parameter: PageParameter, value: string): boolean {
  switch (parameter.type) {
    case 'text':
      return !value.includes('\0')
    case 'integer': {
      if (!integerPattern.test(value)) {
        return false
      }
      const number = BigInt(value)
      return number >= int64Min && number <= int64Max
    }
    case 'decimal':
      return decimalPattern.test(value)
    case 'boolean':
      return value === 'true' || value === 'false'
    case 'date':
      return isRealDate(value)
    case 'date-time':
      return isDateTime(value)
    case 'enum':
      return parameter.values?.includes(value) ?? false
    case 'reference':
      return uuidPattern.test(value)
    default:
      return false
  }
}

/**
 * Reads the values of a page's parameters from its URL query, by parameter name. A value is missing
 * when it is absent or empty, and malformed when it is repeated or not in the form of its type. A
 * missing or malformed optional value is left out, as if the URL did not give it. Returns `null` when a
 * required value is missing or malformed, which means the address names no page.
 */
export function readPageParameters(
  params: URLSearchParams,
  parameters: readonly PageParameter[],
): Record<string, string> | null {
  const values: Record<string, string> = {}
  for (const parameter of parameters) {
    const given = params.getAll(parameter.name)
    const [value] = given
    const usable = given.length === 1 && value !== '' && isWellFormed(parameter, value)
    if (usable) {
      values[parameter.name] = value
    } else if (parameter.required) {
      return null
    }
  }
  return values
}
