import { fillText } from './fillText'
import type { HistoryItem } from './records'
import type { FieldMetadata } from './site'

/**
 * The summary lines of a history entry, built from its details: the labels of the fields it
 * names, its outcome and its step, each on a line of its own, in that order. A field the entity
 * no longer has shows its name, and the labels join in the locale's list words, see
 * docs/reference/ui-writing.md#placeholders-and-numbers. Returns no lines when the details hold none of them.
 */
export function historySummary(
  item: HistoryItem,
  fields: readonly FieldMetadata[],
  t: (key: string) => string,
  locale: string,
): string[] {
  const { details } = item
  const lines: string[] = []
  if (Array.isArray(details.fields) && details.fields.length > 0) {
    const labels = details.fields.map((name) => {
      const field = fields.find((candidate) => candidate.name === name)
      return field?.labelKey ? t(field.labelKey) : String(name)
    })
    const list = new Intl.ListFormat(locale, { type: 'conjunction' }).format(labels)
    lines.push(fillText(t('shell.history.fields'), { fields: list }))
  }
  if (typeof details.outcome === 'string') {
    lines.push(fillText(t('shell.history.outcome'), { outcome: details.outcome }))
  }
  if (typeof details.step === 'string') {
    lines.push(fillText(t('shell.history.step'), { step: details.step }))
  }
  return lines
}
