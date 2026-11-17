import { Typography } from 'antd'
import { formatJsonText } from './jsonText'
import { useDesignTokens } from './themeMode'
import { useText } from './texts'

/**
 * Proposed, see the review on #3650: a value to read as code, such as a call's request or response body or a step's
 * definition, never to edit. JSON, as text or as a value, is laid out with two spaces, and JSON text keeps every number
 * as it was written; other text shows as it is. It is drawn in the code font on
 * `canvas`, scrolls past `maxHeight` and copies whole with its copy button. An editor of JSON is `JsonView`.
 */
export function CodeBlock({ value, label, maxHeight = 320 }: { value: unknown; label: string; maxHeight?: number }) {
  const t = useText()
  const { colour, radius, spacing, type } = useDesignTokens()
  const text = typeof value === 'string' ? formatJsonText(value) : JSON.stringify(value, null, 2)
  return (
    <div style={{ position: 'relative' }}>
      <pre
        aria-label={label}
        tabIndex={0}
        style={{
          margin: 0,
          maxHeight,
          overflow: 'auto',
          padding: `${spacing.xs}px ${spacing.sm}px`,
          paddingInlineEnd: spacing.xl,
          background: colour.canvas,
          border: `1px solid ${colour.borderSubtle}`,
          borderRadius: radius.base,
          fontFamily: type.codeFontFamily,
          fontSize: 13,
          lineHeight: '20px',
          color: colour.text,
          whiteSpace: 'pre',
        }}
      >
        {text}
      </pre>
      <Typography.Text
        copyable={{ text, tooltips: [t('shell.code.copy'), t('shell.code.copied')] }}
        style={{ position: 'absolute', top: spacing.xs / 2, insetInlineEnd: spacing.xs }}
      />
    </div>
  )
}
