import { isFileValue, type FieldLabel, type FieldValue, type RecordValue } from './records'
import { isJsonNumber } from './recordBody'
import type { FieldMetadata } from './site'

export interface FormatOptions {
  /** The UI locale, such as `en`. */
  locale: string
  /** The time zone for date-times. Left out, it is the browser's. Tests set it so results are stable. */
  timeZone?: string
  /** Resolves a text key, for the localized yes and no. */
  text: (key: string) => string
}

/** An enum value's label in the current locale, or the value itself when it has no label key. */
export function enumLabel(
  field: Pick<FieldMetadata, 'valueLabelKeys'>,
  value: string,
  text: (key: string) => string,
): string {
  const key = field.valueLabelKeys?.[value]
  return key ? text(key) : value
}

/** Whether values of the type are numbers, which sit on the right in tabular figures. */
export function isNumeric(type: FieldMetadata['type']): boolean {
  return type === 'integer' || type === 'decimal' || type === 'money'
}

/** What formatting reads of a field or a data source column. */
type Formatted = Pick<FieldMetadata, 'name' | 'type' | 'valueLabelKeys'> & {
  scale?: number | null
  currency?: string | null
  currencyField?: string | null
  currencyColumn?: string | null
}

const currencyCode = /^[A-Za-z]{3}$/

/**
 * The currency of a money field or column: its fixed code, or the row's value of the field or column
 * that holds the code. It is null without a code of three letters, because a text currency field can
 * hold anything and a wrong code would mislead.
 */
export function moneyCurrency(field: Formatted, row?: Record<string, unknown>): string | null {
  const named = field.currencyField ?? field.currencyColumn
  const code = field.currency ?? (named && row ? row[named] : null)
  return typeof code === 'string' && currencyCode.test(code) ? code : null
}

// The digits are the field's scale or the digits the API wrote when they are more, so formatting
// never rounds.
function fractionDigits(text: string, scale: number | null | undefined): number {
  const written = /\.(\d+)/.exec(text)?.[1].length ?? 0
  return Math.max(scale ?? 0, written)
}

// The text keeps every digit, so it is formatted as text.
function moneyText(field: Formatted, text: string, locale: string, row?: Record<string, unknown>): string {
  const currency = moneyCurrency(field, row)
  if (currency === null) {
    return groupedNumber(text, locale, field.scale)
  }
  if (!isJsonNumber(text)) {
    return text
  }
  const digits = fractionDigits(text, field.scale)
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency,
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(text as Intl.StringNumericLiteral)
}

/**
 * A number in the locale's digit grouping, formatted from its source text so that no digit is lost.
 * Text that is not a JSON number is returned unchanged.
 */
function groupedNumber(text: string, locale: string, scale: number | null | undefined): string {
  if (!isJsonNumber(text)) {
    return text
  }
  const digits = fractionDigits(text, scale)
  return new Intl.NumberFormat(locale, { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(
    text as Intl.StringNumericLiteral,
  )
}

// `Intl` writes no-break spaces, which a person cannot tell from a space and a typed text never has.
const plain = (text: string) => text.replace(/[\u00a0\u202f]/g, ' ')

const dateOptions: Intl.DateTimeFormatOptions = { day: '2-digit', month: '2-digit', year: 'numeric' }

function twelveHours(locale: string): boolean {
  const cycle = new Intl.DateTimeFormat(locale, { hour: 'numeric' }).resolvedOptions().hourCycle
  return cycle === 'h11' || cycle === 'h12'
}

// A 12-hour clock has no leading zero (4:30 PM) and a 24-hour clock has two digits (16:30, 09:05).
function timeOptions(locale: string): Intl.DateTimeFormatOptions {
  return { hour: twelveHours(locale) ? 'numeric' : '2-digit', minute: '2-digit' }
}

// The parts of a formatter as a dayjs pattern. A literal with letters is escaped.
function patternOf(format: Intl.DateTimeFormat, hour: string): string {
  const symbols: Record<string, string> = {
    year: 'YYYY',
    month: 'MM',
    day: 'DD',
    minute: 'mm',
    dayPeriod: 'A',
  }
  return format
    .formatToParts(new Date(Date.UTC(2026, 2, 4, 16, 30)))
    .map((part) => {
      if (part.type === 'hour') {
        return hour
      }
      if (part.type === 'literal') {
        const literal = plain(part.value)
        return /\p{L}/u.test(literal) ? `[${literal}]` : literal
      }
      return symbols[part.type] ?? ''
    })
    .join('')
}

/** The dayjs pattern of the shown time to the minute, which the date-time picker's time part uses. */
export function timePattern(locale: string): string {
  const time = new Intl.DateTimeFormat(locale, { ...timeOptions(locale), timeZone: 'UTC' })
  return patternOf(time, twelveHours(locale) ? 'h' : 'HH')
}

/** The dayjs pattern of the shown date or date-time, so a picker writes what `formatValue` shows. */
export function datePattern(locale: string, withTime: boolean): string {
  const date = patternOf(new Intl.DateTimeFormat(locale, { ...dateOptions, timeZone: 'UTC' }), '')
  return withTime ? `${date} ${timePattern(locale)}` : date
}

/**
 * The labels of a references field's ids, in order. An id shows as itself where its label is missing
 * or null, because the user may not read the target's display field.
 */
export function referencesLabels(value: FieldValue | undefined, label: FieldLabel | undefined): string[] {
  const ids = Array.isArray(value) ? (value as string[]) : []
  const labels: unknown[] = Array.isArray(label) ? label : []
  return ids.map((id, index) => (typeof labels[index] === 'string' ? labels[index] : id))
}

/**
 * Formats a record value for display. Formatting never changes the value: numbers keep every digit
 * the API wrote and gain the locale's grouping, a money amount adds its currency, and a reference shows
 * the label of the record it points to. `row` is the values of the record or row, where a money amount reads its currency.
 */
export function formatValue(
  field: Formatted,
  value: RecordValue,
  labels: Record<string, FieldLabel>,
  options: FormatOptions,
  row?: Record<string, unknown>,
): string {
  if (value === null) {
    return ''
  }
  const { locale, timeZone, text } = options
  switch (field.type) {
    case 'boolean':
      return text(value === true ? 'shell.table.yes' : 'shell.table.no')
    case 'money':
      return moneyText(field, String(value), locale, row)
    case 'enum':
      return enumLabel(field, String(value), text)
    case 'file':
      return isFileValue(value) ? value.name : String(value)
    case 'reference': {
      const label = labels[field.name]
      return typeof label === 'string' ? label : String(value)
    }
    case 'integer':
      return groupedNumber(String(value), locale, 0)
    case 'decimal':
      return groupedNumber(String(value), locale, field.scale)
    case 'date': {
      // A date has no time zone. It is built and formatted in UTC, so it never shifts a day.
      const [year, month, day] = String(value).split('-').map(Number)
      return new Intl.DateTimeFormat(locale, { ...dateOptions, timeZone: 'UTC' }).format(
        new Date(Date.UTC(year, month - 1, day)),
      )
    }
    case 'date-time': {
      // Shown to the minute, so the fraction is dropped before parsing and the seconds are not shown.
      const date = new Date(String(value).replace(/\.\d+(?=Z$)/, ''))
      return `${plain(new Intl.DateTimeFormat(locale, { ...dateOptions, timeZone }).format(date))} ${plain(
        new Intl.DateTimeFormat(locale, { ...timeOptions(locale), timeZone }).format(date),
      )}`
    }
    default:
      return String(value)
  }
}
