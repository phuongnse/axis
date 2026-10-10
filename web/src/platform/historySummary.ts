import type { HistoryItem } from './records'
import type { FieldMetadata } from './site'

/**
 * The short summary of a history entry, built from its details: the labels of the fields it
 * names, its outcome and its step, in that order. A field the entity no longer has shows its name.
 * Returns `null` when the details hold none of them.
 */
export function historySummary(
  item: HistoryItem,
  fields: readonly FieldMetadata[],
  t: (key: string) => string,
): string | null {
  const { details } = item
  const parts: string[] = []
  if (Array.isArray(details.fields) && details.fields.length > 0) {
    const labels = details.fields.map((name) => {
      const field = fields.find((candidate) => candidate.name === name)
      return field?.labelKey ? t(field.labelKey) : String(name)
    })
    parts.push(`${t('shell.history.fields')}: ${labels.join(', ')}`)
  }
  if (typeof details.outcome === 'string') {
    parts.push(`${t('shell.history.outcome')}: ${details.outcome}`)
  }
  if (typeof details.step === 'string') {
    parts.push(`${t('shell.history.step')}: ${details.step}`)
  }
  return parts.length > 0 ? parts.join(' · ') : null
}
