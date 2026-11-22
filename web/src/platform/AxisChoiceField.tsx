import { Flex, Radio, Typography, type FormItemProps } from 'antd'
import { useId, type ReactNode } from 'react'
import { AxisField, type AxisFieldStatus } from './AxisField'
import { StatusTag } from './StatusTag'
import { useDesignTokens } from './themeMode'

/** An option of a choice: its value, its label, its help and an optional mark such as "Built in". */
export interface AxisChoiceOption {
  value: string
  label: string
  /** The line under the option's label. The radio's description. An empty help draws nothing. */
  help: string
  mark?: string
  /**
   * Proposed, #3264: the settings that belong to this option, shown under it while it is chosen, set in to where the
   * options' text starts, so they read as the chosen option's and not as more options, as Polaris's choice list shows
   * a chosen choice's children. Only a choice the screen holds shows it.
   */
  detail?: ReactNode
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
  hideLabel?: never
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
  /**
   * Proposed, #3264: hides the label from the eye, as for {@link AxisField}, for a choice that a group's title already
   * names, such as the Value group's way the value comes from. It still names the choice.
   */
  hideLabel?: boolean
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
  const { label, help, options } = props
  const optionId = useId()
  const groupName = useId()
  const { spacing, type } = useDesignTokens()
  const radios = (chosen?: string) => (
    <Flex vertical>
      {options.map((option, index) => {
        const helpId = `${optionId}-${index}-help`
        return (
          <div key={option.value}>
            <Radio value={option.value} aria-describedby={option.help === '' ? undefined : helpId}>
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
            {option.detail !== undefined && option.value === chosen && (
              // The settings start where the option's text does, after the 16px radio and Ant Design's 8px gap.
              <div
                style={{
                  paddingInlineStart: 16 + spacing.xs,
                  marginBlock: spacing.xs,
                  // Ant Design's radio group sets its font size to 0, so the settings take the body size back.
                  fontSize: type.fontSize,
                }}
              >
                {option.detail}
              </div>
            )}
          </div>
        )
      })}
    </Flex>
  )
  if (isOwnMode(props)) {
    const { value, onChange, required } = props
    return (
      <AxisField label={label} help={help} required={required} hideLabel={props.hideLabel}>
        <Radio.Group
          aria-label={label}
          name={groupName}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        >
          {radios(value)}
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
      <Radio.Group aria-label={label}>{radios()}</Radio.Group>
    </AxisField>
  )
}
