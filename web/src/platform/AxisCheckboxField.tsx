import { Checkbox, Switch, type FormItemProps } from 'antd'
import { useId } from 'react'
import { fieldClass } from './antTheme'
import { AxisField, type AxisFieldStatus } from './AxisField'
import { requiredMark } from './requiredMark'
import { useDesignTokens } from './themeMode'

type Shared = {
  label: string
  /** Required, as for {@link AxisField}. */
  help: string
  /** A checkbox, or a switch for a setting that takes effect as it is on. */
  kind?: 'checkbox' | 'switch'
  disabled?: boolean
}

type FormMode = Shared & {
  name: NonNullable<FormItemProps['name']>
  /** The message under the field when the box is not ticked. It marks the field required and checks it. */
  requiredMessage?: string
  initialValue?: boolean
  status?: AxisFieldStatus
  required?: never
  checked?: never
  onChange?: never
  loading?: never
  hideLabel?: never
}

type OwnMode = Shared & {
  name?: never
  requiredMessage?: never
  initialValue?: never
  status?: never
  /** Whether the box is ticked or the switch is on. The screen holds it. */
  checked: boolean
  onChange: (checked: boolean) => void
  /** Marks the field required, as for {@link AxisField}. The screen checks the box. */
  required?: boolean
  /** Shows a switch as busy, such as while its change is saved. */
  loading?: boolean
  /** Hides the label from the eye, as for {@link AxisField}. It still names the input. */
  hideLabel?: boolean
}

function isOwnMode(props: FormMode | OwnMode): props is OwnMode {
  return props.name === undefined
}

/**
 * A yes-or-no field of a platform screen, a checkbox or a switch: its label above, its help under the label, then
 * the input, see docs/reference/actions-and-controls.md#fields. The field label names the input, so the input has no
 * text of its own. Like {@link AxisField}, it has two modes. With a `name` it belongs inside a vertical form, and a
 * `requiredMessage` marks the field required and refuses an unticked box. Without one, the screen holds the value and
 * gives `checked` and `onChange`, and `required` marks the field with the shared `requiredMark`.
 */
export function AxisCheckboxField(props: FormMode | OwnMode) {
  const { label, help, kind = 'checkbox', disabled } = props
  if (isOwnMode(props)) {
    const { checked, onChange, loading, required, hideLabel } = props
    if (kind === 'checkbox' && hideLabel !== true) {
      return (
        <LabelledCheckbox
          label={label}
          help={help}
          required={required === true}
          checked={checked}
          disabled={disabled}
          onChange={onChange}
        />
      )
    }
    return (
      <AxisField label={label} help={help} required={required} hideLabel={hideLabel}>
        {kind === 'switch' ? (
          <Switch checked={checked} disabled={disabled} loading={loading} onChange={(value) => onChange(value)} />
        ) : (
          <Checkbox checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
        )}
      </AxisField>
    )
  }
  const { name, requiredMessage, initialValue, status } = props
  return (
    <AxisField
      label={label}
      help={help}
      name={name}
      requiredMessage={requiredMessage}
      initialValue={initialValue}
      status={status}
      valuePropName="checked"
    >
      {kind === 'switch' ? <Switch disabled={disabled} /> : <Checkbox disabled={disabled} />}
    </AxisField>
  )
}

/**
 * Proposed, #3264: a checkbox with its label on its right, the way a box is read, and its help under the label, in line
 * with the label's text, see docs/reference/actions-and-controls.md#fields. The label names the box and the help
 * describes it. A field of a form takes the same look in the build.
 */
function LabelledCheckbox({
  label,
  help,
  required,
  checked,
  disabled,
  onChange,
}: {
  label: string
  help: string
  required: boolean
  checked: boolean
  disabled?: boolean
  onChange: (checked: boolean) => void
}) {
  const { colour, spacing } = useDesignTokens()
  const helpId = `${useId()}-help`
  return (
    <div className={fieldClass}>
      <Checkbox
        checked={checked}
        disabled={disabled}
        aria-describedby={help === '' ? undefined : helpId}
        aria-required={required || undefined}
        onChange={(event) => onChange(event.target.checked)}
      >
        <span style={{ fontWeight: 500, color: colour.textMuted }}>{requiredMark(label, { required })}</span>
      </Checkbox>
      {help !== '' && (
        // In line with the label's text: the box is 16px wide and Ant Design sets its text 8px after it.
        <div
          id={helpId}
          style={{
            fontSize: 12,
            lineHeight: '20px',
            color: colour.textMuted,
            paddingInlineStart: 16 + spacing.xs,
          }}
        >
          {help}
        </div>
      )}
    </div>
  )
}
