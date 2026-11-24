import type { CSSProperties, ReactNode } from 'react'
import { Link } from 'react-router'
import { FocusRingStyle } from './FocusRingStyle'
import { PartBoundary } from './SectionParts'
import { useDesignTokens } from './themeMode'

/**
 * The keyboard focus ring of a link tile, drawn once by the page that shows tiles through `FocusRingStyle`. Inline styles
 * cannot target focus. See docs/reference/shell.md#signed-in.
 */
export function TileFocusStyle() {
  return <FocusRingStyle selector="[data-tile]:focus-visible" />
}

/**
 * The one tile of the platform: the home page's applications and tools, Studio's application tiles, and a dashboard's
 * KPI and chart, see docs/reference/shell.md#signed-in and docs/reference/detail-chart-kpi-widgets.md. It is a panel
 * on `surface` with a `borderSubtle` border, `radius.base` corners and `spacing.md` padding, or none of them with
 * `bordered={false}`, for a tile inside a frame of its own. With `to` the whole tile is one link, which shows
 * only by the words in `primaryText` that its children draw, and which draws the `focusRing` outline on keyboard focus
 * when the page draws `TileFocusStyle`. With `labelledBy`, the id of its heading, the tile is a labelled section.
 * A part inside a tile draws no rule above itself.
 */
export function Tile({
  to,
  target,
  rel,
  'aria-label': ariaLabel,
  labelledBy,
  testId,
  align = 'flex-start',
  bordered = true,
  busy,
  style,
  children,
}: {
  to?: string
  target?: string
  rel?: string
  'aria-label'?: string
  labelledBy?: string
  testId?: string
  /** How the tile's children line up on the cross axis. */
  align?: 'center' | 'flex-start'
  bordered?: boolean
  /** Whether the tile's content is still loading. */
  busy?: boolean
  style?: CSSProperties
  children: ReactNode
}) {
  const { colour, radius, spacing } = useDesignTokens()
  const frame: CSSProperties = {
    display: 'flex',
    alignItems: align,
    gap: spacing.sm,
    minWidth: 0,
    color: colour.text,
    // A tile that is a link stands for a thing, not a sentence, so it is never underlined,
    // see docs/reference/actions-and-controls.md#opening-things.
    textDecoration: 'none',
    ...(bordered && {
      padding: spacing.md,
      background: colour.surface,
      border: `1px solid ${colour.borderSubtle}`,
      borderRadius: radius.base,
    }),
    ...style,
  }
  const body = <PartBoundary>{children}</PartBoundary>
  if (to !== undefined) {
    return (
      <Link
        to={to}
        target={target}
        rel={rel}
        aria-label={ariaLabel}
        aria-busy={busy}
        data-tile
        data-testid={testId}
        style={frame}
      >
        {body}
      </Link>
    )
  }
  if (labelledBy !== undefined) {
    return (
      <section aria-labelledby={labelledBy} aria-busy={busy} data-testid={testId} style={frame}>
        {body}
      </section>
    )
  }
  return (
    <div aria-busy={busy} data-testid={testId} style={frame}>
      {body}
    </div>
  )
}
