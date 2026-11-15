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

/**
 * A choice among a few options of a platform screen, drawn as radios: the field's label, its help, then each option
 * with its label, its optional mark and its own help, see docs/reference/actions-and-controls.md#fields. The option's
 * help is part of the option's text and also its description. It belongs inside a vertical form, with a `name`.
 */
export function AxisChoiceField({
  label,
  help,
  name,
  options,
  requiredMessage,
  initialValue,
  status,
}: {
  label: string
  /** Required, as for {@link AxisField}. */
  help: string
  name: NonNullable<FormItemProps['name']>
  options: readonly AxisChoiceOption[]
  /** The message under the field when nothing is chosen. It marks the field required and checks it. */
  requiredMessage?: string
  initialValue?: string
  status?: AxisFieldStatus
}) {
  const optionId = useId()
  return (
    <AxisField
      label={label}
      help={help}
      name={name}
      requiredMessage={requiredMessage}
      initialValue={initialValue}
      status={status}
    >
      <Radio.Group aria-label={label}>
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
      </Radio.Group>
    </AxisField>
  )
}
