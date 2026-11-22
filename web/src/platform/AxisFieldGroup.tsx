import { useId, type ReactNode } from 'react'
import { AxisActionRow, AxisButton } from './AxisButton'
import { FieldError } from './AxisField'
import { Chevron } from './Chevron'
import { requiredMark } from './requiredMark'
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
  fold,
  heading = false,
  required = false,
  error,
  children,
}: {
  title: string
  help: string
  action?: ReactNode
  /**
   * Proposed, #3264: a group of settings that are seldom changed folds, closed at first, behind the fold `SectionPart`
   * has, before its title. While closed it shows `state` after its title in `textMuted`, such as which of its settings
   * are on, so a setting is never out of sight unseen. Its help and its fields show only while it is open.
   */
  fold?: { open: boolean; onToggle: () => void; state: string }
  /**
   * Proposed, #3264: a group that heads a part of a long form, such as a field panel's General and Value, takes the
   * look of a heading, in the text colour and heavier than a field's label, so a part is told from the fields in it.
   */
  heading?: boolean
  /** Proposed, #3264: marks the group required with the shared `requiredMark` after its title. The screen checks it. */
  required?: boolean
  /** Proposed, #3264: what the screen found wrong with the group's value, under its fields, as `AxisField`'s error. */
  error?: string
  children: ReactNode
}) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  const titleId = useId()
  const errorId = `${titleId}-error`
  const hasAction = action !== null && action !== undefined
  // Proposed, #3264: a legend sits where a field's label sits and as far above the help, so a group's title and a
  // field's label line up, see docs/reference/actions-and-controls.md#fields. A browser pads a legend by 2px on each side.
  const legendStyle = {
    fontWeight: heading ? 600 : 500,
    color: heading ? colour.text : colour.textMuted,
    padding: 0,
    paddingBottom: heading ? spacing.xs : 4,
  }
  return (
    <fieldset
      aria-labelledby={hasAction ? titleId : undefined}
      aria-describedby={
        [help === '' ? undefined : helpId, error === undefined ? undefined : errorId].filter(Boolean).join(' ') ||
        undefined
      }
      aria-invalid={error === undefined ? undefined : true}
      style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}
    >
      {hasAction ? (
        // The button is not in the legend's text, so the group is named by its title alone.
        // Proposed, #3264: the title and its help stand together on the left and the action on the right, as a page
        // part's heading has them, so the action's height does not push the help away from the title. The group is
        // named by its title alone, so the help in the legend stays its description.
        <legend
          style={{
            ...legendStyle,
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            gap: spacing.sm,
            width: '100%',
          }}
        >
          <span style={{ minWidth: 0 }}>
            <span id={titleId} style={{ display: 'block' }}>
              {title}
            </span>
            {help !== '' && (
              <span
                id={helpId}
                // As far under the title as a group without an action sets its help or its first field.
                style={{
                  display: 'block',
                  marginTop: legendStyle.paddingBottom,
                  fontSize: 12,
                  lineHeight: '20px',
                  fontWeight: 400,
                  color: colour.textMuted,
                }}
              >
                {help}
              </span>
            )}
          </span>
          <AxisActionRow>{action}</AxisActionRow>
        </legend>
      ) : fold ? (
        <legend style={{ ...legendStyle, display: 'flex', alignItems: 'center', gap: spacing.xs, width: '100%' }}>
          <AxisButton
            role="icon"
            size="small"
            icon={<Chevron point={fold.open ? 'down' : 'right'} />}
            nameKey={fold.open ? 'shell.fieldGroup.hide' : 'shell.fieldGroup.show'}
            nameValues={{ title }}
            aria-expanded={fold.open}
            onClick={fold.onToggle}
          />
          <span>{title}</span>
          {!fold.open && <span style={{ fontWeight: 400 }}>· {fold.state}</span>}
        </legend>
      ) : (
        <legend style={legendStyle}>{requiredMark(title, { required })}</legend>
      )}
      {(fold && !fold.open) || hasAction
        ? null
        : help !== '' && (
            <div
              id={helpId}
              style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted, marginBottom: spacing.xs }}
            >
              {help}
            </div>
          )}
      {fold && !fold.open ? null : children}
      {error !== undefined && <FieldError id={errorId}>{error}</FieldError>}
    </fieldset>
  )
}
