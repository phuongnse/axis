import type { CSSProperties, HTMLAttributes, ReactNode } from 'react'
import { ContentInsetContext } from './contentInsetContext'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'

interface ColumnGridProps extends HTMLAttributes<HTMLElement> {
  /** The element: a `dl` for a list of facts, a `div` otherwise. */
  as?: 'div' | 'dl'
  /** The gap between rows, a spacing token. Unset it is `spacing-md`, as the gap across. */
  rowGap?: string | number
  children: ReactNode
}

/**
 * The one grid of twelve columns that a detail's fields, a form's fields and the `columns` widget share, see
 * docs/reference/form-widget.md#fields-in-columns. Each {@link ColumnGridCell} spans as many columns as its width. The
 * columns are `spacing-md` apart.
 */
export function ColumnGrid({ as: Element = 'div', rowGap, children, style, ...rest }: ColumnGridProps) {
  const { spacing } = useDesignTokens()
  return (
    <Element
      {...rest}
      style={{
        margin: 0,
        display: 'grid',
        gridTemplateColumns: 'repeat(12, minmax(0, 1fr))',
        columnGap: spacing.md,
        rowGap: rowGap ?? spacing.md,
        alignItems: 'start',
        ...style,
      }}
    >
      {children}
    </Element>
  )
}

interface ColumnGridCellProps extends HTMLAttributes<HTMLDivElement> {
  /** The columns to span, in twelfths. A width outside 1 to 12 spans all twelve. */
  width?: number
  style?: CSSProperties
  children?: ReactNode
}

/**
 * A cell of a {@link ColumnGrid}. It spans all twelve columns at narrow width, below 992 px, see
 * docs/reference/frontend.md#narrow-width. The hook decides, not Ant Design's breakpoints, so the page designer can
 * force the stack. A cell is a column inside the page, so what it holds reads an inset of 0, see
 * docs/reference/shell.md#layout.
 */
export function ColumnGridCell({ width, style, children, ...rest }: ColumnGridCellProps) {
  const narrow = useNarrowWidth()
  const span = narrow || width === undefined || width < 1 || width > 12 ? 12 : width
  return (
    <div {...rest} style={{ gridColumn: `span ${span}`, minWidth: 0, ...style }}>
      <ContentInsetContext.Provider value={0}>{children}</ContentInsetContext.Provider>
    </div>
  )
}
