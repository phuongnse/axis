import { Flex, Radio, Tag, Typography, type FormItemProps } from 'antd'
import { useId } from 'react'
import { AxisField, type AxisFieldStatus } from './AxisField'
import { useDesignTokens } from './themeMode'

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
  /**
   * Proposed: the options in two columns, read down the first column and then the second, for a choice of many options
   * such as a field's type. One column by default.
   */
  columns?: 1 | 2
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

/**
 * A choice among a few options of a platform screen, drawn as radios: the field's label, its help, then each option
 * with its label, its optional mark and its own help, see docs/reference/actions-and-controls.md#fields. The option's
 * help is part of the option's text and also its description. Like {@link AxisField}, it has two modes. With a `name`
 * it belongs inside a vertical form, and a `requiredMessage` marks the field required and checks it. Without one, the
 * screen holds the value and gives `value` and `onChange`, and `required` marks the field with the shared
 * `requiredMark`. Each choice gives its radio group a name of its own, so two choices on one panel never join.
 */
export function AxisChoiceField(props: FormMode | OwnMode) {
  const { label, help, options, columns = 1 } = props
  const optionId = useId()
  const groupName = useId()
  const { spacing } = useDesignTokens()
  const radios = (
    <Flex
      vertical
      style={
        columns === 2
          ? {
              display: 'grid',
              gridAutoFlow: 'column',
              gridTemplateColumns: '1fr 1fr',
              gridTemplateRows: `repeat(${Math.ceil(options.length / 2)}, auto)`,
              // The rows keep the one-column choice's spacing, so the control has one look.
              columnGap: spacing.lg,
            }
          : undefined
      }
    >
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
    const { value, onChange, required } = props
    return (
      <AxisField label={label} help={help} required={required}>
        <Radio.Group
          aria-label={label}
          name={groupName}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        >
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
