import { useId, type ReactNode } from 'react'
import { TitleRowActions } from './AxisButton'
import { useDesignTokens } from './themeMode'

/**
 * A titled group of fields, with its help: a `fieldset` that screen readers announce as a group named by its title
 * and described by its help, see docs/reference/actions-and-controls.md#fields. It holds fields that have no one input to
 * label, such as an object's fields, rows or a table. An empty help draws no help line. An `action` sits on the right of
 * the title row, drawn through `TitleRowActions` in its own role, so a secondary button keeps its border, and the group is
 * still named by its title alone.
 * An `error` draws the screen's refusal under the fields, in `dangerText`, and the group's description starts with it.
 * The group has no one input to mark, so the caller marks the inputs inside it, such as through `LabelTexts`' `invalid`.
 */
export function AxisFieldGroup({
  title,
  help,
  action,
  error,
  children,
}: {
  title: string
  help: string
  action?: ReactNode
  /** The message under the fields, once the screen refuses their value. */
  error?: string
  children: ReactNode
}) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  const errorId = `${useId()}-error`
  const titleId = useId()
  const hasAction = action !== null && action !== undefined
  const legendStyle = { fontWeight: 500, color: colour.textMuted }
  const described = [error === undefined ? undefined : errorId, help === '' ? undefined : helpId].filter(
    (id) => id !== undefined,
  )
  return (
    <fieldset
      aria-labelledby={hasAction ? titleId : undefined}
      aria-describedby={described.length === 0 ? undefined : described.join(' ')}
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
      {error !== undefined && (
        <div id={errorId} style={{ color: colour.dangerText }}>
          {error}
        </div>
      )}
    </fieldset>
  )
}
