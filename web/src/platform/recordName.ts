import { formatValue, type FormatOptions } from './formatValue'
import type { RecordItem, RecordValue } from './records'
import type { EntityMetadata } from './site'

/**
 * A record's name, which heads the record's page: its entity's display field value, formatted as a table cell formats
 * it. It is `null` when the entity has no display field, the record has no value for it, the person may not read it, or
 * the value is empty, so the page keeps its title. See docs/reference/shell.md#page-header.
 */
export function recordName(entity: EntityMetadata, record: RecordItem, options: FormatOptions): string | null {
  const field = entity.fields.find((candidate) => candidate.name === entity.displayField)
  const value = field ? record.values[field.name] : undefined
  if (!field || value === null || value === undefined) {
    return null
  }
  const name = formatValue(field, value as RecordValue, record.labels, options, record.values).trim()
  return name === '' ? null : name
}
