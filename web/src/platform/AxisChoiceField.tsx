import { Flex, Radio, Tag, Typography, type FormItemProps } from 'antd'
import { useId } from 'react'
import { AxisField, type AxisFieldStatus } from './AxisField'

/** An option of a choice: its value, its label, its help and an optional mark such as "Built in". */
export interface AxisChoiceOption {
  value: string
  label: string
  /** The line under the option's label. The radio's description. An empty help draws nothing. */
  help: string
  mark?: string
}

type Shared = {
  label: string
  /** Required, as for {@link AxisField}. */
  help: string
  options: readonly AxisChoiceOption[]
}

type FormMode = Shared & {
  name: NonNullable<FormItemProps['name']>
  /** The message under the field when nothing is chosen. It marks the field required and checks it. */
  requiredMessage?: string
  initialValue?: string
  status?: AxisFieldStatus
  value?: never
  onChange?: never
}

type OwnMode = Shared & {
  name?: never
  requiredMessage?: never
  initialValue?: never
  status?: never
  /** The chosen option's value, or none. The screen holds it. */
  value: string | undefined
  onChange: (value: string) => void
}

function isOwnMode(props: FormMode | OwnMode): props is OwnMode {
  return props.name === undefined
}

/**
 * A choice among a few options of a platform screen, drawn as radios: the field's label, its help, then each option
 * with its label, its optional mark and its own help, see docs/reference/actions-and-controls.md#fields. The option's
 * help is part of the option's text and also its description. Like {@link AxisField}, it has two modes. With a `name`
 * it belongs inside a vertical form. Without one, the screen holds the value and gives `value` and `onChange`.
 */
export function AxisChoiceField(props: FormMode | OwnMode) {
  const { label, help, options } = props
  const optionId = useId()
  const radios = (
    <Flex vertical>
      {options.map((option, index) => {
        const helpId = `${optionId}-${index}-help`
        return (
          <Radio key={option.value} value={option.value} aria-describedby={option.help === '' ? undefined : helpId}>
            <span>{option.label}</span>
            {option.mark && <Tag style={{ marginInlineStart: 8 }}>{option.mark}</Tag>}
            {option.help !== '' && (
              <Typography.Text type="secondary" id={helpId} style={{ display: 'block' }}>
                {option.help}
              </Typography.Text>
            )}
          </Radio>
        )
      })}
    </Flex>
  )
  if (isOwnMode(props)) {
    const { value, onChange } = props
    return (
      <AxisField label={label} help={help}>
        <Radio.Group aria-label={label} value={value} onChange={(event) => onChange(event.target.value)}>
          {radios}
        </Radio.Group>
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
    >
      <Radio.Group aria-label={label}>{radios}</Radio.Group>
    </AxisField>
  )
}
