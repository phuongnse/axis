import { fillText } from './fillText'
import { formatValue } from './formatValue'
import type { RecordValue } from './records'
import type { FieldMetadata } from './site'
import type { ExpressionReading as Reading } from './studio'

/**
 * What a reading needs to name its fields and format its values: the label of a text key, from Studio's names of the
 * application or from the release an instance is pinned to, and the reader's language.
 */
export interface Labels {
  label?: (labelKey: string | null) => string | undefined
  locale: string
}

/** Whether a node is a run of `and` or `or`, or a `not` before one. */
export function isGroup(node: Reading): boolean {
  return node.kind === 'all' || node.kind === 'any' || node.kind === 'notAll' || node.kind === 'none'
}

/** A literal in the author's language: yes and no, a label, a number or a date as tables show them. */
export function valueText(
  node: Extract<Reading, { kind: 'value' }>,
  labels: Labels,
  t: (key: string) => string,
): string {
  switch (node.type) {
    case 'boolean':
      return t(`shell.studio.expression.reads.value.${node.value === true ? 'true' : 'false'}`)
    case 'null':
      return t('shell.studio.expression.reads.value.null')
    case 'enum':
      return labels.label?.(node.labelKey) ?? String(node.value)
    case 'text':
      return `'${String(node.value)}'`
    case 'integer':
    case 'decimal':
    case 'date':
    case 'date-time':
      return formatValue(
        { name: '', type: node.type as FieldMetadata['type'] },
        node.value as RecordValue,
        {},
        { locale: labels.locale, text: t },
      )
    default:
      return String(node.value)
  }
}

/**
 * Proposed, see the review on #3650: the reading of an expression as one line of text, for a place that holds no more,
 * such as a transition's label on a process graph: the same words as `ExpressionReading`, a group read as its heading
 * then its items joined with a comma.
 */
export function readingText(reading: Reading, labels: Labels, t: (key: string) => string): string {
  switch (reading.kind) {
    case 'phrase': {
      const separator = reading.words === 'in' ? ` ${t('shell.studio.expression.reads.join.or')} ` : ', '
      const values = Object.fromEntries(
        reading.slots.map((slot, index) => [
          String(index),
          slot.map((node) => readingText(node, labels, t)).join(separator),
        ]),
      )
      return fillText(t(`shell.studio.expression.reads.${reading.words}`), values)
    }
    case 'name':
      return labels.label?.(reading.labelKey) ?? reading.name
    case 'value':
      return valueText(reading, labels, t)
    default: {
      const items = reading.items.map((item) => readingText(item, labels, t))
      if (reading.kind === 'notAll' || reading.kind === 'none' || reading.items.some(isGroup)) {
        return `${t(`shell.studio.expression.reads.group.${reading.kind}`)} ${items.join(', ')}`
      }
      return items.join(` ${t(`shell.studio.expression.reads.join.${reading.kind === 'all' ? 'and' : 'or'}`)} `)
    }
  }
}
