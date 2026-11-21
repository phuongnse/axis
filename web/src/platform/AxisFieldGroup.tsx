import { useId, type ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

/**
 * A titled group of fields, with its help: a `fieldset` that screen readers announce as a group named by its title
 * and described by its help, see docs/reference/actions-and-controls.md#fields. It holds fields that have no one input to
 * label, such as an object's fields, rows or a table. An empty help draws no help line.
 */
/**
 * A legend sits where a field's label sits and as far above the help, so a group's title and a field's label line up,
 * see docs/reference/actions-and-controls.md#fields. A browser pads a legend by 2px on each side.
 */
const legendStyle = { padding: 0, paddingBottom: 4 }

export function AxisFieldGroup({
  title,
  help,
  action,
  children,
}: {
  title: string
  help: string
  /**
   * Proposed, #3264: an action of the whole group, such as Add validation, light on the right of its title, as a
   * title row's actions are, see docs/reference/shell.md#layout.
   */
  action?: ReactNode
  children: ReactNode
}) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  return (
    <fieldset
      aria-describedby={help === '' ? undefined : helpId}
      style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}
    >
      {action === undefined ? (
        <legend style={{ ...legendStyle, fontWeight: 500, color: colour.textMuted }}>{title}</legend>
      ) : (
        <legend
          style={{
            ...legendStyle,
            display: 'flex',
            width: '100%',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <span style={{ fontWeight: 500, color: colour.textMuted }}>{title}</span>
          {action}
        </legend>
      )}
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
