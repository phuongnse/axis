import { Checkbox, Switch, type FormItemProps } from 'antd'
import { AxisField, type AxisFieldStatus } from './AxisField'

/**
 * A yes-or-no field of a platform screen, a checkbox or a switch: its label above, its help under the label, then
 * the input, see docs/reference/actions-and-controls.md#fields. The field label names the input, so the input has no
 * text of its own. A `requiredMessage` marks the field required and refuses an unticked box. It belongs inside a vertical
 * form, with a `name`.
 */
export function AxisCheckboxField({
  label,
  help,
  name,
  requiredMessage,
  initialValue,
  status,
  kind = 'checkbox',
  disabled,
}: {
  label: string
  /** Required, as for {@link AxisField}. */
  help: string
  name: NonNullable<FormItemProps['name']>
  /** The message under the field when the box is not ticked. It marks the field required and checks it. */
  requiredMessage?: string
  initialValue?: boolean
  status?: AxisFieldStatus
  /** A checkbox, or a switch for a setting that takes effect as it is on. */
  kind?: 'checkbox' | 'switch'
  disabled?: boolean
}) {
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
