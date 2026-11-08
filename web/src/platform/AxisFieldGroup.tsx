import { useId, type ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * A titled group of fields, with its help: a `fieldset` that screen readers announce as a group named by its title
 * and described by its help, see docs/reference/actions-and-controls.md#fields. It holds fields that have no one input to
 * label, such as an object's fields, rows or a table. An empty help draws no help line.
 */
export function AxisFieldGroup({ title, help, children }: { title: string; help: string; children: ReactNode }) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  return (
    <fieldset
      aria-describedby={help === '' ? undefined : helpId}
      style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}
    >
      <legend style={{ fontWeight: 500, color: colour.textMuted }}>{title}</legend>
      {help !== '' && (
        <div
          id={helpId}
          style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted, marginBottom: spacing.xs }}
        >
          {help}
        </div>
      )}
      {children}
    </fieldset>
  )
}
