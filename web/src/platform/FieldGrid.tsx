import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'
import { usePhoneWidth } from './usePhoneWidth'

/** Proposed: a section's fields in rows of twelfths. A row is as tall as its tallest field and its fields line up at
 * the top, so an error under one field never shifts the rows below out of line. */
export function FieldGrid({
  as: Element = 'div',
  rowGap = 0,
  children,
}: {
  /** A detail widget lists its fields as a description list. */
  as?: 'div' | 'dl'
  rowGap?: number
  children: ReactNode
}) {
  const { colour, spacing, type } = useDesignTokens()
  return (
    <Element
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(12, minmax(0, 1fr))',
        columnGap: spacing.md,
        rowGap,
        alignItems: 'start',
        margin: 0,
        // Outside a form item nothing gives the text the theme's colour and font, so the grid does.
        color: colour.text,
        fontFamily: type.fontFamily,
        fontSize: type.fontSize,
      }}
    >
      {children}
    </Element>
  )
}

/** Proposed: one field's place in a {@link FieldGrid}: its width in twelfths, or the whole row at narrow width. */
export function FieldCell({ width, children }: { width: number; children: ReactNode }) {
  const phone = usePhoneWidth()
  return <div style={{ gridColumn: `span ${phone ? 12 : width}` }}>{children}</div>
}

/** Proposed: a field's help text, between its label and its input, so an error under the input never moves it. */
export function FieldHelp({ id, children }: { id: string; children: ReactNode }) {
  const { colour, spacing } = useDesignTokens()
  return (
    <div
      id={id}
      style={{ color: colour.textMuted, fontSize: 12, lineHeight: '20px', marginBlock: `-4px ${spacing.xs}px` }}
    >
      {children}
    </div>
  )
}

/** Proposed: a field's error, 4px under its input at a 20px line height, with a whole field gap before the next field. */
export function FieldError({ name, children }: { name: string; children: string }) {
  const { spacing } = useDesignTokens()
  return (
    <span
      role="alert"
      data-testid={`field-error-${name}`}
      style={{ display: 'block', lineHeight: '20px', paddingTop: 4, paddingBottom: spacing.lg }}
    >
      {children}
    </span>
  )
}

/** Proposed: a field's label, medium and muted so it stands apart from a read-only value, with a `*` after it when required. */
export function FieldLabel({ label, required }: { label: ReactNode; required: boolean }) {
  const { colour } = useDesignTokens()
  return (
    <span style={{ fontWeight: 500, color: colour.textMuted }}>
      {label}
      {required && (
        <span aria-hidden="true" style={{ color: colour.dangerText, marginInlineStart: 4 }}>
          *
        </span>
      )}
    </span>
  )
}
