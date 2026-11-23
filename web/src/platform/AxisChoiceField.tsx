import { Flex, Form, Radio, Typography, type FormItemProps, type RadioGroupProps } from 'antd'
import { useId, type ReactNode } from 'react'
import { AxisField, type AxisFieldStatus } from './AxisField'
import { useErrorBoxStyles } from './antTheme'
import { keepsOwnWidth } from './ownWidth'
import { StatusTag } from './StatusTag'

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
  required?: never
}

type OwnMode = Shared & {
  name?: never
  requiredMessage?: never
  initialValue?: never
  status?: never
  /** The chosen option's value, or `undefined` when nothing is chosen. The screen holds it. */
  value: string | undefined
  onChange: (value: string) => void
  /** Marks the field required, as for {@link AxisField}. The screen checks the choice. */
  required?: boolean
}

function isOwnMode(props: FormMode | OwnMode): props is OwnMode {
  return props.name === undefined
}

type BoxStyles = ReturnType<ReturnType<typeof useErrorBoxStyles>>

/** The radio group of a choice inside a form item, which draws each unticked radio in `dangerText` while the item shows an error. */
function FormChoiceGroup({
  label,
  radios,
  ...injected
}: RadioGroupProps & {
  label: string
  radios: (value: unknown, styleOf: (ticked: boolean) => BoxStyles) => ReactNode
}) {
  const { status } = Form.Item.useStatus()
  const boxStyles = useErrorBoxStyles()
  return (
    <Radio.Group aria-label={label} {...injected}>
      {radios(injected.value, (ticked) => boxStyles(status === 'error', ticked))}
    </Radio.Group>
  )
}
keepsOwnWidth(FormChoiceGroup)

/**
 * A choice among a few options of a platform screen, drawn as radios: the field's label, its help, then each option
 * with its label, its optional mark and its own help, see docs/reference/actions-and-controls.md#fields. The option's
 * help is part of the option's text and also its description. Like {@link AxisField}, it has two modes. With a `name`
 * it belongs inside a vertical form, and a `requiredMessage` marks the field required and checks it. Without one, the
 * screen holds the value and gives `value` and `onChange`, and `required` marks the field with the shared
 * `requiredMark`. Each choice gives its radio group a name of its own, so two choices on one panel never join.
 */
export function AxisChoiceField(props: FormMode | OwnMode) {
  const { label, help, options } = props
  const optionId = useId()
  const groupName = useId()
  const radios = (value: unknown, styleOf: (ticked: boolean) => BoxStyles) => (
    <Flex vertical>
      {options.map((option, index) => {
        const helpId = `${optionId}-${index}-help`
        return (
          <Radio
            key={option.value}
            value={option.value}
            styles={styleOf(option.value === value)}
            aria-describedby={option.help === '' ? undefined : helpId}
          >
            <Flex gap="small" align="center" wrap>
              <span>{option.label}</span>
              {option.mark && <StatusTag tone="neutral">{option.mark}</StatusTag>}
            </Flex>
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
    const { value, onChange, required } = props
    return (
      <AxisField label={label} help={help} required={required}>
        <Radio.Group
          aria-label={label}
          name={groupName}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        >
          {radios(value, () => undefined)}
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
      <FormChoiceGroup label={label} radios={radios} />
    </AxisField>
  )
}
