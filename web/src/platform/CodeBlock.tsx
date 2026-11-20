import { EmptyDash } from './EmptyValue'
import { useDesignTokens } from './themeMode'

/**
 * Text to read as it is, in the code font, such as read-only JSON or a body that is not JSON. A `null` text shows the em
 * dash. The block scrolls sideways only, see docs/reference/actions-and-controls.md#scroll-areas. Shared widgets are in
 * docs/reference/shared-widgets.md#which-widget.
 */
export function CodeBlock({
  text,
  testId,
  plain = false,
  wrap = false,
}: {
  text: string | null
  testId?: string
  /**
   * Proposed, see #3978: the text fills a region that rules already part, such as a package's Output tab, so it has no
   * border and no padding of its own.
   */
  plain?: boolean
  /** Proposed, see #3978: long lines wrap at the block's width, so a log reads without scrolling sideways. */
  wrap?: boolean
}) {
  const { colour, radius, spacing, type } = useDesignTokens()
  return (
    <pre
      data-testid={testId}
      style={{
        margin: 0,
        padding: plain ? 0 : spacing.sm,
        overflowX: 'auto',
        whiteSpace: wrap ? 'pre-wrap' : undefined,
        overflowWrap: wrap ? 'anywhere' : undefined,
        fontFamily: type.codeFontFamily,
        background: colour.surface,
        border: plain ? 'none' : `1px solid ${colour.borderSubtle}`,
        borderRadius: plain ? 0 : radius.base,
      }}
    >
      {text === null ? <EmptyDash /> : text}
    </pre>
  )
}
