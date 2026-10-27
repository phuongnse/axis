import type { ReactNode } from 'react'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: what shows where a value is empty. A field reads "Not set" and a table cell "—", both in muted italics,
 * which no value is ever drawn in, so a value that happens to read "Not set" is never taken for an empty one.
 */
export function EmptyValue({ cell = false }: { cell?: boolean }) {
  const t = useText()
  const { colour } = useDesignTokens()
  return <span style={{ color: colour.textMuted, fontStyle: 'italic' }}>{cell ? '—' : t('shell.value.notSet')}</span>
}

/**
 * Proposed: a read-only value is plain text. In a cell it takes an input's height, so it lines up with the inputs
 * beside it. Under a label it sits as far below the label as an input's border would.
 */
export function ReadOnlyValue({
  id,
  cell = false,
  compact = false,
  children,
}: {
  id?: string
  cell?: boolean
  /** Proposed: in a detail widget, which has no inputs to line up with, the value is as tall as its text. */
  compact?: boolean
  children: ReactNode
}) {
  return (
    <div
      id={id}
      style={{
        lineHeight: '22px',
        overflowWrap: 'anywhere',
        // A form item centres its content in an input's 32px. Pulled up by 4px and as tall as that, the value starts
        // 8px below the label, as an input's border does.
        ...(cell
          ? { boxSizing: 'border-box', minHeight: 32, paddingBlock: 5 }
          : compact
            ? {}
            : { marginTop: -4, minHeight: 36 }),
      }}
    >
      {children === '' ? <EmptyValue cell={cell} /> : children}
    </div>
  )
}
