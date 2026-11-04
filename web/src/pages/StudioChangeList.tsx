import { Typography } from 'antd'
import { Link } from 'react-router'
import { useDesignTokens } from '../platform/themeMode'
import { nameCollator } from './studioViews'

/** Proposed: one change in a change list: what changed, its kind and how, and where it opens, if anywhere. */
export interface StudioChangeRow {
  key: string
  name: string
  kind: string
  change: string
  to?: string
  /** Proposed: where its kind sorts, from `kindOrder`; rows sort by it, then by name. */
  order?: number
}

/**
 * Proposed: Studio's one change list, the Overview's changes since the live release and a release's changes from the
 * one before it alike: three aligned columns, as the table rules have them, the name, which is free text and wraps at
 * 320 px, then its kind and what changed, each as wide as its content, with no separator between them. Each row is a
 * list item whose cells sit in the list's grid. A name with somewhere to open is a link; one with nothing left to open,
 * such as a removed file, is text.
 */
export function StudioChangeList({ rows, testId }: { rows: readonly StudioChangeRow[]; testId: string }) {
  const { colour, spacing } = useDesignTokens()
  // Proposed: a change list is a set with no order of its own, so it sorts by kind, in the sidebar's order, then by
  // name as a person reads it.
  const collator = nameCollator()
  const sorted = [...rows].sort((a, b) => (a.order ?? 999) - (b.order ?? 999) || collator.compare(a.name, b.name))
  return (
    <ul
      data-testid={testId}
      style={{
        margin: 0,
        padding: 0,
        listStyle: 'none',
        display: 'grid',
        gridTemplateColumns: 'fit-content(320px) max-content max-content',
        columnGap: spacing.lg,
        rowGap: spacing.xs,
        alignItems: 'baseline',
      }}
    >
      {sorted.map((row) => (
        <li key={row.key} style={{ display: 'contents' }} data-testid={`${testId}-row`}>
          {row.to ? (
            <Link to={row.to} style={{ color: colour.primaryText }}>
              {row.name}
            </Link>
          ) : (
            <Typography.Text>{row.name}</Typography.Text>
          )}
          <Typography.Text style={{ color: colour.textMuted, whiteSpace: 'nowrap' }}>{row.kind}</Typography.Text>
          <Typography.Text style={{ color: colour.textMuted, whiteSpace: 'nowrap' }}>{row.change}</Typography.Text>
        </li>
      ))}
    </ul>
  )
}
