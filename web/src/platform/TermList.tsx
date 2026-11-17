import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'

/** One term of a `TermList`: its label, its value, and how many of the row's 12 columns it takes, all 12 by default. */
export interface Term {
  key: string
  term: ReactNode
  value: ReactNode
  width?: number
}

/**
 * Proposed, see the review on #3650: the facts of one thing as terms and their values, each label in `textMuted` above
 * its value, on a grid of 12 columns that takes every term to the full row at narrow width. A detail draws its fields
 * with it, and a page that shows one thing draws the facts the platform keeps about it, such as a task's due date.
 * `row` lays the terms in one line at their own widths, `spacing-xl` apart, wrapping when the line is full, as Camunda
 * Operate and the Azure portal's essentials head a page: a strip of short facts under a heading, each value one line.
 */
export function TermList({ terms, row = false }: { terms: readonly Term[]; row?: boolean }) {
  const { colour, spacing, type } = useDesignTokens()
  const narrow = useNarrowWidth()
  return (
    <dl
      style={
        row
          ? { margin: 0, display: 'flex', flexWrap: 'wrap', columnGap: spacing.xl, rowGap: spacing.md }
          : {
              margin: 0,
              display: 'grid',
              gridTemplateColumns: 'repeat(12, minmax(0, 1fr))',
              columnGap: spacing.md,
              rowGap: spacing.md,
              alignItems: 'start',
            }
      }
    >
      {terms.map(({ key, term, value, width = 12 }) => (
        <div
          key={key}
          style={
            row
              ? { minWidth: 0, maxWidth: '100%', whiteSpace: 'nowrap' }
              : { gridColumn: `span ${narrow || width < 1 || width > 12 ? 12 : width}`, minWidth: 0 }
          }
        >
          <dt style={{ fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }}>
            {term}
          </dt>
          <dd style={{ margin: '6px 0 0', fontSize: type.fontSize, lineHeight: '22px', color: colour.text }}>
            {value}
          </dd>
        </div>
      ))}
    </dl>
  )
}
