import { Typography } from 'antd'
import { Fragment } from 'react'
import { FilledText } from '../platform/FilledText'
import { formatValue } from '../platform/formatValue'
import type { RecordValue } from '../platform/records'
import type { FieldMetadata } from '../platform/site'
import type { ExpressionReading as Reading, StudioNames } from '../platform/studio'
import { labelText } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useBulletList } from '../platform/useBulletList'

/** What a reading needs to name its fields and format its values: the application's names and the author's language. */
interface Labels {
  names?: StudioNames | null
  locale: string
}

export interface ExpressionReadingProps extends Labels {
  reading: Reading
}

/**
 * The reading of an expression in words, see docs/reference/studio.md#expression-editor. A run of `and` or of `or`
 * reads on one line. A group that holds a group, and every `not` group, reads as a list under its heading.
 */
export function ExpressionReading({ reading, names, locale }: ExpressionReadingProps) {
  return <ReadingNode node={reading} names={names} locale={locale} />
}

function ReadingNode({ node, ...labels }: Labels & { node: Reading }) {
  const t = useText()
  const bullets = useBulletList()

  switch (node.kind) {
    case 'phrase': {
      // The `in` phrase lists its values with `or`, and every other slot with a comma.
      const separator = node.words === 'in' ? ` ${t('shell.studio.expression.reads.join.or')} ` : ', '
      const parts = Object.fromEntries(
        node.slots.map((slot, index) => [String(index), joined(slot, separator, labels)]),
      )
      return <FilledText text={t(`shell.studio.expression.reads.${node.words}`)} parts={parts} />
    }
    case 'name':
      return <strong>{(labels.names && labelText(labels.names, node.labelKey, labels.locale)) ?? node.name}</strong>
    case 'value':
      return <>{valueText(node, labels, t)}</>
    default: {
      const nested = node.kind === 'notAll' || node.kind === 'none' || node.items.some(isGroup)
      if (!nested) {
        const join = t(`shell.studio.expression.reads.join.${node.kind === 'all' ? 'and' : 'or'}`)
        return <span>{joined(node.items, ` ${join} `, labels)}</span>
      }
      return (
        <div>
          <Typography.Text>{t(`shell.studio.expression.reads.group.${node.kind}`)}</Typography.Text>
          <ul style={bullets.list}>
            {node.items.map((item, index) => (
              <li key={index} style={bullets.item}>
                <ReadingNode node={item} {...labels} />
              </li>
            ))}
          </ul>
        </div>
      )
    }
  }
}

/** The nodes of a list, each read on its own, with `separator` between them. */
function joined(nodes: Reading[], separator: string, labels: Labels) {
  return nodes.map((node, index) => (
    <Fragment key={index}>
      {index > 0 && separator}
      <ReadingNode node={node} {...labels} />
    </Fragment>
  ))
}

function isGroup(node: Reading): boolean {
  return node.kind === 'all' || node.kind === 'any' || node.kind === 'notAll' || node.kind === 'none'
}

/** A literal in the author's language: yes and no, a label, a number or a date as tables show them. */
function valueText(node: Extract<Reading, { kind: 'value' }>, labels: Labels, t: (key: string) => string): string {
  switch (node.type) {
    case 'boolean':
      return t(`shell.studio.expression.reads.value.${node.value === true ? 'true' : 'false'}`)
    case 'null':
      return t('shell.studio.expression.reads.value.null')
    case 'enum':
      return (labels.names && labelText(labels.names, node.labelKey, labels.locale)) ?? String(node.value)
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
