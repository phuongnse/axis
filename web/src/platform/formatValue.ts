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

// The text keeps every digit, so it is formatted as text. The digits are the field's scale or the
// digits the API wrote when they are more, so formatting never rounds.
function moneyText(field: Formatted, text: string, locale: string, row?: Record<string, unknown>): string {
  const currency = moneyCurrency(field, row)
  if (currency === null || !isJsonNumber(text)) {
    return text
  }
  const written = /\.(\d+)/.exec(text)?.[1].length ?? 0
  const digits = Math.max(field.scale ?? 0, written)
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency,
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(text as Intl.StringNumericLiteral)
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
 * Formats a record value for display. Formatting never changes the value: numbers show exactly as
 * the API wrote them, a money amount adds its currency, and a reference shows the label of the record
 * it points to. `row` is the values of the record or row, where a money amount reads its currency.
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
    case 'date': {
      // A date has no time zone. It is built and formatted in UTC, so it never shifts a day.
      const [year, month, day] = String(value).split('-').map(Number)
      return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeZone: 'UTC' }).format(
        new Date(Date.UTC(year, month - 1, day)),
      )
    }
    case 'date-time': {
      // Shown to the second, so the microseconds are dropped before parsing.
      const date = new Date(String(value).replace(/\.\d+(?=Z$)/, ''))
      return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium', timeZone }).format(date)
    }
    default:
      return String(value)
  }
}
