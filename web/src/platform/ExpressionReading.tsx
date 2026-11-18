import { Typography } from 'antd'
import { Fragment } from 'react'
import { FilledText } from './FilledText'
import { isGroup, valueText, type Labels } from './readingText'
import type { ExpressionReading as Reading } from './studio'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

export interface ExpressionReadingProps extends Labels {
  reading: Reading
}

/**
 * The reading of an expression in words, see docs/reference/studio.md#expression-editor. A run of `and` or of `or`
 * reads on one line. A group that holds a group, and every `not` group, reads as a list under its heading. Proposed, see
 * the review on #3650: it is shared, as a process instance's page reads a decision branch's condition with it too.
 */
export function ExpressionReading({ reading, label, locale }: ExpressionReadingProps) {
  return <ReadingNode node={reading} label={label} locale={locale} />
}

function ReadingNode({ node, ...labels }: Labels & { node: Reading }) {
  const t = useText()
  const tokens = useDesignTokens()

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
      return <strong>{labels.label?.(node.labelKey) ?? node.name}</strong>
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
          <ul style={{ margin: 0, paddingInlineStart: tokens.spacing.lg }}>
            {node.items.map((item, index) => (
              <li key={index}>
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
