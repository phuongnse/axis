import { Checkbox, Switch, type FormItemProps } from 'antd'
import { AxisField, type AxisFieldStatus } from './AxisField'

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
    const { checked, onChange, loading, required } = props
    return (
      <AxisField label={label} help={help} required={required}>
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
