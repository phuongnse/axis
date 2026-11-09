import { Form, type FormItemProps } from 'antd'
import { cloneElement, forwardRef, useId, type ReactElement, type Ref } from 'react'
import { useDesignTokens } from './themeMode'

type FieldInput = ReactElement<{ id?: string; 'aria-describedby'?: string; onChange?: (...args: never[]) => void }>

type FormFieldProps = {
  label: string
  /** Required: every platform field has help, see docs/reference/frontend.md#platform-field-help. An empty help draws no help line. */
  help: string
  name: NonNullable<FormItemProps['name']>
  /** The message under the field when it is empty, such as "Enter a name.". It marks the field required and checks it. A multiple select is empty when nothing is chosen. */
  requiredMessage?: string
  /** More rules of Ant Design's form, checked after the required one. */
  rules?: FormItemProps['rules']
  /** For an input whose value is not called `value`, such as an `Upload`'s `fileList`. Ant Design's form setting of that name. */
  valuePropName?: FormItemProps['valuePropName']
  /** For an input whose change event is not the value, such as an `Upload`'s. Ant Design's form setting of that name. */
  getValueFromEvent?: FormItemProps['getValueFromEvent']
  /** Proposed: the answer of a check the screen runs while the person types, such as whether a subdomain is free. */
  check?: FieldCheck
  /** The input, such as an `Input` or a `Select`. Ant Design's form gives it its value and its change handler. */
  children: FieldInput
}

type OwnFieldProps = {
  label: string
  /** Required: every platform field has help, see docs/reference/frontend.md#platform-field-help. An empty help draws no help line. */
  help: string
  name?: never
  requiredMessage?: never
  rules?: never
  valuePropName?: never
  getValueFromEvent?: never
  check?: never
  /** The input, such as an `Input` or a `Select`. The caller gives it its `value` and its change handler. */
  children: FieldInput
}

/**
 * Proposed: the answer of a check that runs while the person types, shown under the input where an error shows. Running
 * shows the input busy, `passed` and `refused` say what the check found, and `failed` says the check could not run. While a
 * check is shown it takes the place of the field's own errors, so the screen gives none while the field is empty.
 */
export type FieldCheck = { state: 'running' | 'passed' | 'refused' | 'failed'; message: string }

const checkStatus = { running: 'validating', passed: 'success', refused: 'error', failed: 'warning' } as const

/** With a `name`, a field of an Ant Design form. Without one, a field the caller holds the value of. */
export type AxisFieldProps = FormFieldProps | OwnFieldProps

function isFormField(props: AxisFieldProps): props is FormFieldProps {
  return props.name !== undefined
}

/** Whether a field has no value: none, blank text or an empty list. A number, even 0, is a value. */
function isEmpty(value: unknown): boolean {
  return (
    value === undefined ||
    value === null ||
    (typeof value === 'string' && value.trim() === '') ||
    (Array.isArray(value) && value.length === 0)
  )
}

/**
 * A field of a platform screen: its label, its help, its input and its error, in the form widget's field look. The
 * label sits above the help, the help above the input and the error under it. The help describes the input. Its
 * two modes differ in who holds the value:
 *
 * - With a `name`, the field belongs inside a vertical form that takes the shared `requiredMark`, and Ant Design's form
 *   gives the input its value and change handler. A `requiredMessage` marks the field required and adds the rule that
 *   checks it, so the mark and the check never part. The check holds for text, numbers and lists, and blank text is
 *   empty.
 * - Without a `name`, the field needs no form: the caller gives the input its `value` and change handler, and the
 *   label still names the input.
 *
 * See docs/reference/actions-and-controls.md#fields and docs/reference/frontend.md#platform-field-help.
 */
export function AxisField(props: AxisFieldProps) {
  return isFormField(props) ? <FormField {...props} /> : <OwnField {...props} />
}

/** A field the caller holds the value of: a label tied to the input, then the help and the input. */
function OwnField({ label, help, children }: OwnFieldProps) {
  const { colour } = useDesignTokens()
  const ownId = useId()
  const id = children.props.id ?? ownId
  return (
    <div>
      <label htmlFor={id} style={{ display: 'block', paddingBottom: 8, fontWeight: 500, color: colour.textMuted }}>
        {label}
      </label>
      <HelpedInput help={help} input={children} id={id} />
    </div>
  )
}

function FormField({
  label,
  help,
  name,
  requiredMessage,
  rules,
  valuePropName,
  getValueFromEvent,
  check,
  children,
}: FormFieldProps) {
  const { colour } = useDesignTokens()
  return (
    <Form.Item
      name={name}
      label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{label}</span>}
      required={requiredMessage !== undefined}
      valuePropName={valuePropName}
      getValueFromEvent={getValueFromEvent}
      {...(check === undefined ? {} : { validateStatus: checkStatus[check.state], help: check.message })}
      rules={[
        ...(requiredMessage === undefined
          ? []
          : [
              {
                required: true,
                message: requiredMessage,
                validator: (_rule: unknown, value: unknown) =>
                  isEmpty(value) ? Promise.reject(new Error(requiredMessage)) : Promise.resolve(),
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
  const own = input.props['aria-describedby']
  const joined = [help === '' ? undefined : helpId, described, own].filter((id) => id !== undefined)
  const describedBy = joined.length === 0 ? undefined : joined.join(' ')
  return (
    <>
      {help !== '' && (
        // The label has 8px of padding below it, so this pulls the help 4px under it.
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
      )}
      {cloneElement(input, {
        ...control,
        ref,
        'aria-describedby': describedBy,
      } as object)}
    </>
  )
})
