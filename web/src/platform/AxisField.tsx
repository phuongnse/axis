import { Checkbox, Flex } from 'antd'
import { useId, type ReactNode } from 'react'
import { RequiredAsterisk } from './RequiredAsterisk'
import { useDesignTokens } from './themeMode'
import { useNarrowWidth } from './useNarrowWidth'

/**
 * Proposed: a field's label and help text, as the form widget draws them: the label in medium weight and textMuted, the
 * help in small textMuted text between the label and the input. `AxisField` draws it; a schema-driven form whose help
 * comes from the text catalogue draws it alone, where a test checks the catalogue has every help.
 */
export function AxisFieldHeading({
  label,
  help,
  helpId,
  required = false,
}: {
  label: string
  help: string | null
  helpId: string
  required?: boolean
}) {
  const { colour, type } = useDesignTokens()
  return (
    <div style={{ marginBottom: 6 }}>
      <div style={{ fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }}>
        {label}
        {required && <RequiredAsterisk />}
      </div>
      {help && (
        <div id={helpId} style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted }}>
          {help}
        </div>
      )}
    </div>
  )
}

/**
 * Proposed: the one field of the platform's own screens, Studio's and the console's: its label, its help text, then
 * its input, one field to a row. Every field has help text, so `help` is required: a screen cannot leave it out. The
 * field is a group described by its help, so a screen reader reads the help with the input. `testId` is the id a
 * diagnostic opens at, such as `studio-field-/name`. A `required` field has the form widget's mark (D96): a `*` in
 * `dangerText` after its label, hidden from screen readers, and its input carries `aria-required`, which the screen sets.
 */
export function AxisField({
  label,
  help,
  required = false,
  testId,
  children,
}: {
  label: string
  help: string
  required?: boolean
  testId?: string
  children: ReactNode
}) {
  const helpId = useId()
  return (
    <div role="group" aria-label={label} aria-describedby={helpId} data-testid={testId}>
      <AxisFieldHeading label={label} help={help} helpId={helpId} required={required} />
      {children}
    </div>
  )
}

/**
 * Proposed: a yes-or-no field: the checkbox, its label beside it and its help under the label, as the help of
 * `AxisField` reads. It is one field to a row, as every field is.
 */
export function AxisCheckboxField({
  label,
  help,
  checked,
  testId,
  onChange,
}: {
  label: string
  help: string
  checked: boolean
  testId?: string
  onChange: (checked: boolean) => void
}) {
  const { colour, spacing, type } = useDesignTokens()
  const helpId = useId()
  return (
    <Flex gap={spacing.xs} align="flex-start" data-testid={testId}>
      <Checkbox
        aria-label={label}
        aria-describedby={helpId}
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
        style={{ marginTop: 2 }}
      />
      <div>
        <div style={{ fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }}>
          {label}
        </div>
        <div id={helpId} style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted }}>
          {help}
        </div>
      </div>
    </Flex>
  )
}

/**
 * Proposed: two or three fields that belong together on one row, such as a range's minimum and maximum, in equal
 * columns, spacing-md apart. Below 992 px they stack, one to a row. Fields that only sit near each other each keep a row.
 */
export function AxisFieldRow({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const narrow = useNarrowWidth()
  return (
    <div
      style={{
        display: 'grid',
        gridAutoFlow: narrow ? 'row' : 'column',
        gridAutoColumns: 'minmax(0, 1fr)',
        columnGap: spacing.md,
        rowGap: spacing.md,
        alignItems: 'start',
      }}
    >
      {children}
    </div>
  )
}
