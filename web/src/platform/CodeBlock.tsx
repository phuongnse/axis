import { EmptyDash } from './EmptyValue'
import { useDesignTokens } from './themeMode'

/**
 * Text to read as it is, in the code font, such as read-only JSON or a body that is not JSON. A `null` text shows the em
 * dash. The block scrolls sideways only, unless `wrap` wraps its long lines, as a log's are, see
 * docs/reference/actions-and-controls.md#scroll-areas. Shared widgets are in
 * docs/reference/shared-widgets.md#which-widget.
 */
export function CodeBlock({ text, testId, wrap }: { text: string | null; testId?: string; wrap?: boolean }) {
  const { colour, radius, spacing, type } = useDesignTokens()
  return (
    <pre
      data-testid={testId}
      style={{
        margin: 0,
        padding: spacing.sm,
        ...(wrap ? { whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' } : { overflowX: 'auto' }),
        fontFamily: type.codeFontFamily,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      {text === null ? <EmptyDash /> : text}
    </pre>
  )
}
