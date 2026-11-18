import type { CSSProperties, HTMLAttributes, ReactNode } from 'react'
import { Link, type LinkProps } from 'react-router'
import { useDesignTokens } from './themeMode'

/**
 * Proposed, see the review on #3650: a tile, as docs/reference/shell.md#platform-home-page says one is: a panel on
 * `surface` with a `borderSubtle` border, `radius-base` corners and `spacing-md` padding, which keeps its border on a
 * flush page, where a page's parts sit a rule apart with no panel. The home page's tools and an application's tiles in
 * Studio, a dashboard's KPI and its chart draw through it. A tile with `to` is a link: hover changes nothing, and
 * keyboard focus draws the `focusRing` outline that `TileFocusStyle` gives, which the page renders once.
 */
export function Tile({
  to,
  children,
  style,
  ...rest
}: {
  to?: LinkProps['to']
  children: ReactNode
  style?: CSSProperties
} & Omit<HTMLAttributes<HTMLElement>, 'style' | 'children'> &
  Pick<LinkProps, 'target' | 'rel'> &
  Record<`data-${string}`, string | boolean | undefined>) {
  const { colour, spacing, radius } = useDesignTokens()
  const look: CSSProperties = {
    display: 'block',
    minWidth: 0,
    padding: spacing.md,
    background: colour.surface,
    border: `1px solid ${colour.borderSubtle}`,
    borderRadius: radius.base,
    // A link tile reads as the rest of the page: its link is its title in `primaryText`, never the whole tile's colour.
    color: 'inherit',
    ...style,
  }
  return to === undefined ? (
    <div {...rest} style={look}>
      {children}
    </div>
  ) : (
    <Link {...rest} to={to} data-tile style={look}>
      {children}
    </Link>
  )
}

/** The keyboard focus ring of a link tile, rendered once by a page that has one. Inline styles cannot target focus. */
export function TileFocusStyle() {
  const { colour } = useDesignTokens()
  return <style>{`[data-tile]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: 2px; }`}</style>
}
