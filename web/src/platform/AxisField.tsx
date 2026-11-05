import { Checkbox, Flex, Form, Radio } from 'antd'
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
 * Proposed: a field the screen checks takes `error`, `null` while it has none. Its message is the form widget's: Ant
 * Design's form item draws it 4px under the input and marks the input, and the group is described by it too. A screen
 * checks its fields when the finish button is clicked, never by disabling it, see `design-review.md`.
 */
export function AxisField({
  label,
  help,
  required = false,
  error,
  testId,
  children,
}: {
  label: string
  help: string
  required?: boolean
  /** Proposed: the field's message, or `null` while it has none. A field the screen does not check leaves it out. */
  error?: string | null
  testId?: string
  children: ReactNode
}) {
  const helpId = useId()
  const errorId = useId()
  return (
    <div
      role="group"
      aria-label={label}
      aria-describedby={error ? `${helpId} ${errorId}` : helpId}
      data-testid={testId}
    >
      <AxisFieldHeading label={label} help={help} helpId={helpId} required={required} />
      {error === undefined ? (
        children
      ) : (
        // Always a form item, so the input is not drawn anew, and loses no focus, as the message comes and goes. The
        // message keeps no gap of its own below it: the field's container spaces the fields.
        <Form.Item
          validateStatus={error ? 'error' : undefined}
          help={error ? <span id={errorId}>{error}</span> : undefined}
          style={{ marginBottom: 0, ['--ant-form-item-margin-bottom' as string]: '0px' }}
        >
          {children}
        </Form.Item>
      )}
    </div>
  )
}

/**
 * Proposed: a field that is one choice among a few ways, such as where a value comes from or where a currency comes
 * from: its label and help as `AxisField` draws them, then one radio button to a row, each with its own help under its
 * label, as `AxisCheckboxField` reads. The settings of the chosen way follow it, and only those.
 */
export function AxisChoiceField<T extends string>({
  label,
  help,
  value,
  options,
  disabled = false,
  testId,
  onChange,
}: {
  label: string
  help: string
  value: T
  options: readonly { value: T; label: string; help?: string }[]
  disabled?: boolean
  testId?: string
  onChange: (next: T) => void
}) {
  const { colour, spacing } = useDesignTokens()
  return (
    <AxisField label={label} help={help} testId={testId}>
      <Radio.Group
        aria-label={label}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value as T)}
        style={{ width: '100%' }}
      >
        <Flex vertical gap={spacing.xs}>
          {options.map((option) => (
            <Radio key={option.value} value={option.value}>
              <span style={{ color: colour.text }}>{option.label}</span>
              {option.help && (
                <span style={{ display: 'block', fontSize: 12, lineHeight: '20px', color: colour.textMuted }}>
                  {option.help}
                </span>
              )}
            </Radio>
          ))}
        </Flex>
      </Radio.Group>
    </AxisField>
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

/**
 * Proposed: a group of fields under a heading, as a drawer or a long form splits its fields, such as a field's General,
 * Validation and Value. The heading is a small title in `text`; groups after the first have a line above them, spacing-lg
 * from it, and the fields are spacing-md apart, as in a section.
 */
export function AxisFieldGroup({
  title,
  first = false,
  testId,
  children,
}: {
  title: string
  first?: boolean
  testId?: string
  children: ReactNode
}) {
  const { colour, spacing, type } = useDesignTokens()
  return (
    <section
      aria-label={title}
      data-testid={testId}
      style={first ? undefined : { borderTop: `1px solid ${colour.borderSubtle}`, paddingTop: spacing.lg }}
    >
      <Flex vertical gap={spacing.md}>
        <div style={{ fontWeight: 600, fontSize: type.fontSize, lineHeight: '22px' }}>{title}</div>
        {children}
      </Flex>
    </section>
  )
}
