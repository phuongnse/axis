import { useId, type ReactNode } from 'react'
import { TitleRowActions } from './AxisButton'
import { useDesignTokens } from './themeMode'

/**
 * A titled group of fields, with its help: a `fieldset` that screen readers announce as a group named by its title
 * and described by its help, see docs/reference/actions-and-controls.md#fields. It holds fields that have no one input to
 * label, such as an object's fields, rows or a table. An empty help draws no help line. An `action` sits on the right of
 * the title row, drawn through `AxisActionRow` as a light text button, and the group is still named by its title alone.
 */
export function AxisFieldGroup({
  title,
  help,
  action,
  children,
}: {
  title: string
  help: string
  action?: ReactNode
  children: ReactNode
}) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  const titleId = useId()
  const hasAction = action !== null && action !== undefined
  const legendStyle = { fontWeight: 500, color: colour.textMuted }
  return (
    <fieldset
      aria-labelledby={hasAction ? titleId : undefined}
      aria-describedby={help === '' ? undefined : helpId}
      style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}
    >
      {hasAction ? (
        // The button is not in the legend's text, so the group is named by its title alone.
        <legend
          style={{
            ...legendStyle,
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            width: '100%',
            padding: 0,
          }}
        >
          <span id={titleId}>{title}</span>
          <TitleRowActions>{action}</TitleRowActions>
        </legend>
      ) : (
        <legend style={legendStyle}>{title}</legend>
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
