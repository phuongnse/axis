import { Checkbox, Form, Radio, Switch, Upload, type FormItemProps } from 'antd'
import { cloneElement, forwardRef, useId, type ReactElement, type ReactNode, type Ref } from 'react'
import { Announcement } from './Announcement'
import { fieldClass, fieldInputClass } from './antTheme'
import { requiredMark } from './requiredMark'
import { useDesignTokens } from './themeMode'
import { visuallyHidden } from './visuallyHidden'

type FieldInput = ReactElement<{
  id?: string
  'aria-describedby'?: string
  'aria-invalid'?: boolean
  status?: 'error'
  onChange?: (...args: never[]) => void
}>

/** What the screen found about the field beyond its rules, see docs/reference/actions-and-controls.md#fields. */
export type AxisFieldStatus =
  { state: 'idle' } | { state: 'running' } | { state: 'passed' | 'refused' | 'failed'; message: string }

type FormFieldProps = {
  label: string
  /** Required: every platform field has help, see docs/reference/frontend.md#platform-field-help. An empty help draws no help line. */
  help: string
  name: NonNullable<FormItemProps['name']>
  /** The message under the field when it is empty, such as "Enter a name.". It marks the field required and checks it. A multiple select is empty when nothing is chosen, and an unticked box is empty. */
  requiredMessage?: string
  /** More rules of Ant Design's form, checked after the required one. */
  rules?: FormItemProps['rules']
  /** For an input whose value is not called `value`, such as an `Upload`'s `fileList`. Ant Design's form setting of that name. */
  valuePropName?: FormItemProps['valuePropName']
  /** For an input whose change event is not the value, such as an `Upload`'s. Ant Design's form setting of that name. */
  getValueFromEvent?: FormItemProps['getValueFromEvent']
  /** The value the field starts at and returns to on reset. Ant Design's form setting of that name. */
  initialValue?: FormItemProps['initialValue']
  /** What the screen found about the field, such as whether a name is free. It takes the place of the field's own errors while shown. */
  status?: AxisFieldStatus
  /** A field in a form takes its refusal as a `status`, so it takes no `error`. */
  error?: never
  /** A field in a form is marked required by its `requiredMessage`, the one way to mark it. */
  required?: never
  hideLabel?: never
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
  initialValue?: never
  status?: never
  /**
   * The message under the input, once the screen refuses its value, such as "Enter a name.". The input is marked
   * `aria-invalid` and drawn in the error look. The message goes first in the input's description. The caller drops it
   * once the value is right.
   */
  error?: string
  /** Marks the field required: the shared `requiredMark` after the label and `aria-required` on the input. The screen checks the value. */
  required?: boolean
  /** Hides the label from the eye while it still names the input, for a control in a table cell whose row and column name it. */
  hideLabel?: boolean
  /** The input, such as an `Input` or a `Select`. The caller gives it its `value` and its change handler. */
  children: FieldInput
}

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
 *   gives the input its value and change handler. The form stacks its fields with a `spacing-lg` gap, as the field
 *   keeps no space below itself. A `requiredMessage` marks the field required and adds the rule that
 *   checks it, so the mark and the check never part. The check holds for text, numbers, lists and boxes, and blank text and an unticked box are
 *   empty.
 * - Without a `name`, the field needs no form: the caller gives the input its `value` and change handler, and the
 *   label still names the input. `hideLabel` hides the label from the eye and keeps it naming the input, for a control in a
 *   table cell that its row and column name. `required` draws the shared `requiredMark` after the label and sets `aria-required`
 *   on the input, and the screen checks the value.
 *
 * A `status` on a field with a `name` shows a check's result under the input: busy while it runs, a message a screen
 * reader hears when it passed, and an error or a warning when it refused or failed.
 *
 * An `error` on a field without a `name` shows the screen's refusal the same way: the message under the input in
 * `dangerText`, and the input marked `aria-invalid` in its error look. A group of fields takes the same `error` in
 * `AxisFieldGroup`, and the caller marks the inputs inside it.
 *
 * See docs/reference/actions-and-controls.md#fields and docs/reference/frontend.md#platform-field-help.
 */
export function AxisField(props: AxisFieldProps) {
  return isFormField(props) ? <FormField {...props} /> : <OwnField {...props} />
}

// Ant Design remounts the input when hasFeedback switches on or off, so a field with a status keeps it on and draws
// only the spinner.
const statusFeedback: FormItemProps['hasFeedback'] = {
  icons: () => ({ success: false, error: false, warning: false }),
}

/** The form item's state for a status. Idle and passed show no message, so the field keeps its rule errors. */
function statusItemProps(
  status: AxisFieldStatus | undefined,
): Pick<FormItemProps, 'validateStatus' | 'help' | 'hasFeedback'> {
  if (status === undefined) {
    return {}
  }
  if (status.state === 'idle' || status.state === 'passed') {
    return { hasFeedback: statusFeedback }
  }
  if (status.state === 'running') {
    return { validateStatus: 'validating', hasFeedback: statusFeedback }
  }
  return {
    validateStatus: status.state === 'refused' ? 'error' : 'warning',
    help: status.message,
    hasFeedback: statusFeedback,
  }
}

/** A field the caller holds the value of: a label tied to the input, then the help and the input. */
function OwnField({ label, help, error, required, hideLabel, children }: OwnFieldProps) {
  const { colour } = useDesignTokens()
  const ownId = useId()
  const id = children.props.id ?? ownId
  return (
    <div className={fieldClass}>
      <label
        htmlFor={id}
        style={
          hideLabel === true
            ? visuallyHidden
            : { display: 'block', paddingBottom: 8, fontWeight: 500, color: colour.textMuted }
        }
      >
        {requiredMark(label, { required: required === true })}
      </label>
      <HelpedInput
        help={help}
        input={children}
        id={id}
        error={error}
        {...(required === true ? { 'aria-required': true } : {})}
      />
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
  initialValue,
  status,
  children,
}: FormFieldProps) {
  const { colour } = useDesignTokens()
  return (
    // The theme keeps no margin below the field. The form gives the gap, see docs/reference/shared-widgets.md#the-rule.
    <Form.Item
      className={fieldClass}
      name={name}
      label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{label}</span>}
      required={requiredMessage !== undefined}
      valuePropName={valuePropName}
      getValueFromEvent={getValueFromEvent}
      initialValue={initialValue}
      rules={[
        ...(requiredMessage === undefined
          ? []
          : [
              {
                required: true,
                message: requiredMessage,
                validator: (_rule: unknown, value: unknown) =>
                  isEmpty(value) || (valuePropName === 'checked' && value !== true)
                    ? Promise.reject(new Error(requiredMessage))
                    : Promise.resolve(),
              },
            ]),
        ...(rules ?? []),
      ]}
      {...statusItemProps(status)}
      extra={
        status === undefined ? undefined : (
          <Announcement>{status.state === 'passed' ? status.message : undefined}</Announcement>
        )
      }
    >
      {/* Ant Design gives the one child of a named item its value, its change handler and its accessibility props. */}
      <HelpedInput help={help} input={children} onChange={children.props.onChange} status={status} />
    </Form.Item>
  )
}

type HelpedInputProps = {
  help: string
  input: FieldInput
  status?: AxisFieldStatus
  /** The message under the input, which marks the input as in error. */
  error?: string
  id?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
  onChange?: (...args: never[]) => void
}

// A checkbox, a switch, an upload and a radio group keep their own width, see docs/reference/shell.md#layout.
const ownWidth = [Checkbox, Radio.Group, Switch, Upload]

/** The wrapper `fieldRules` sizes round an input that fills its field. An input that keeps its own width gets none. */
function FieldInputFrame({ own, children }: { own: boolean; children: ReactNode }) {
  return own ? children : <div className={fieldInputClass}>{children}</div>
}

/**
 * The help and the input under the label, and the error under the input. The error goes first in the input's
 * description, then the help, as the form's field does with Ant Design's error. An input that fills the field goes in
 * a wrapper that `fieldRules` sizes.
 */
const HelpedInput = forwardRef(function HelpedInput(
  { help, input, status, error, 'aria-describedby': described, ...control }: HelpedInputProps,
  ref: Ref<unknown>,
) {
  const { spacing, colour } = useDesignTokens()
  const helpId = `${useId()}-help`
  const errorId = `${useId()}-error`
  const own = input.props['aria-describedby']
  const joined = [error === undefined ? undefined : errorId, help === '' ? undefined : helpId, described, own].filter(
    (id) => id !== undefined,
  )
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
      <FieldInputFrame own={ownWidth.includes(input.type as never)}>
        {cloneElement(input, {
          ...control,
          ref,
          'aria-describedby': describedBy,
          'aria-busy': status?.state === 'running' ? true : undefined,
          ...(error === undefined ? {} : { 'aria-invalid': true, status: 'error' }),
        } as object)}
      </FieldInputFrame>
      {error !== undefined && (
        <div id={errorId} style={{ color: colour.dangerText }}>
          {error}
        </div>
      )}
    </>
  )
})
