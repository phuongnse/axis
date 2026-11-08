import { Form, type FormItemProps } from 'antd'
import { cloneElement, forwardRef, useId, type ReactElement, type Ref } from 'react'
import { useDesignTokens } from './themeMode'

type FieldInput = ReactElement<{ id?: string; 'aria-describedby'?: string; onChange?: (...args: never[]) => void }>

export type AxisFieldProps = {
  label: string
  /** Required: every platform field has help, see docs/reference/frontend.md#platform-field-help. */
  help: string
  name: NonNullable<FormItemProps['name']>
  /** The message under the field when it is empty, such as "Enter a name.". It marks the field required and checks it. A multiple select is empty when nothing is chosen. */
  requiredMessage?: string
  /** More rules of Ant Design's form, checked after the required one. */
  rules?: FormItemProps['rules']
  /** The input, such as an `Input` or a `Select`. Ant Design's form gives it its value and its change handler. */
  children: FieldInput
}

/**
 * A field of a platform screen: its label, its help, its input and its error, in the form widget's field look. The
 * label sits above the help, the help above the input and the error under it. The help describes the input. A
 * `requiredMessage` marks the field required and adds the rule that checks it, so the mark and the check never part. It
 * belongs inside a vertical form that takes the shared `requiredMark`, see
 * docs/reference/actions-and-controls.md#fields and docs/reference/frontend.md#platform-field-help.
 */
export function AxisField({ label, help, name, requiredMessage, rules, children }: AxisFieldProps) {
  const { colour } = useDesignTokens()
  return (
    <Form.Item
      name={name}
      label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{label}</span>}
      required={requiredMessage !== undefined}
      rules={[
        ...(requiredMessage === undefined
          ? []
          : [
              {
                required: true,
                whitespace: true,
                message: requiredMessage,
                // A multiple select holds an array, which is empty when nothing is chosen. The rule reads text, so it reads the joined values.
                transform: (value: unknown) => (Array.isArray(value) ? value.join('') : value),
              },
            ]),
        ...(rules ?? []),
      ]}
    >
      {/* Ant Design gives the one child of a named item its value, its change handler and its accessibility props. */}
      <HelpedInput help={help} input={children} onChange={children.props.onChange} />
    </Form.Item>
  )
}

type HelpedInputProps = {
  help: string
  input: FieldInput
  id?: string
  'aria-describedby'?: string
  onChange?: (...args: never[]) => void
}

/** The help and the input under the label. The help joins the input's description, after the error Ant Design adds. */
const HelpedInput = forwardRef(function HelpedInput(
  { help, input, 'aria-describedby': described, ...control }: HelpedInputProps,
  ref: Ref<unknown>,
) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  return (
    <>
      {/* The label has Ant Design's 8px padding below it, so this pulls the help 4px under it. */}
      <div
        id={helpId}
        style={{
          fontSize: 12,
          lineHeight: '20px',
          color: colour.textMuted,
          marginTop: -4,
          marginBottom: spacing.xs,
        }}
      >
        {help}
      </div>
      {cloneElement(input, {
        ...control,
        ref,
        'aria-describedby': described === undefined ? helpId : `${helpId} ${described}`,
      } as object)}
    </>
  )
})
