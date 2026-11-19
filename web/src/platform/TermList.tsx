import type { ReactNode } from 'react'
import { ColumnGrid, ColumnGridCell } from './ColumnGrid'
import { useDesignTokens } from './themeMode'

export interface Term {
  key: string
  term: ReactNode
  value: ReactNode
  /** The columns the fact spans on the grid of twelve. Unset it is 12, one fact to a row. Not used with `row`. */
  width?: number
}

interface TermListProps {
  terms: readonly Term[]
  /** A strip of facts in one wrapping line, each as wide as its content, instead of one fact to a row. */
  row?: boolean
}

/**
 * A list of facts, each a term over its value, see docs/reference/actions-and-controls.md#fields. It gives each fact a
 * row of its own on the shared {@link ColumnGrid}, or with `row` draws a strip of facts in one line.
 */
export function TermList({ terms, row = false }: TermListProps) {
  const { spacing, colour, type } = useDesignTokens()
  const termStyle = { fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }
  const valueStyle = { margin: '6px 0 0', fontSize: type.fontSize, lineHeight: '22px', color: colour.text }

  if (row) {
    return (
      <dl
        style={{
          margin: 0,
          display: 'flex',
          flexWrap: 'wrap',
          columnGap: spacing.xl,
          rowGap: spacing.md,
        }}
      >
        {terms.map((fact) => (
          <div key={fact.key}>
            <dt style={termStyle}>{fact.term}</dt>
            <dd style={{ ...valueStyle, whiteSpace: 'nowrap' }}>{fact.value}</dd>
          </div>
        ))}
      </dl>
    )
  }

  return (
    <ColumnGrid as="dl">
      {terms.map((fact) => (
        <ColumnGridCell key={fact.key} width={fact.width ?? 12}>
          <dt style={termStyle}>{fact.term}</dt>
          <dd style={valueStyle}>{fact.value}</dd>
        </ColumnGridCell>
      ))}
    </ColumnGrid>
  )
}
