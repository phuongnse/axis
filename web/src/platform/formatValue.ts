import type { RecordValue } from './records'
import type { FieldMetadata } from './site'

export interface FormatOptions {
  /** The UI locale, such as `en`. */
  locale: string
  /** The time zone for date-times. Left out, it is the browser's. Tests set it so results are stable. */
  timeZone?: string
  /** Resolves a text key, for the localized yes and no. */
  text: (key: string) => string
}

/**
 * Formats a record value for display. Formatting never changes the value: numbers show exactly as
 * the API wrote them, and a reference shows the label of the record it points to.
 */
export function formatValue(
  field: Pick<FieldMetadata, 'name' | 'type'>,
  value: RecordValue,
  labels: Record<string, string>,
  options: FormatOptions,
): string {
  if (value === null) {
    return ''
  }
  const { locale, timeZone, text } = options
  switch (field.type) {
    case 'boolean':
      return text(value === true ? 'shell.table.yes' : 'shell.table.no')
    case 'reference':
      return labels[field.name] ?? String(value)
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
