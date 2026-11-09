import { Alert, Button, Form, Input } from 'antd'
import { useEffect, useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { UpdateProfileContext } from './signInContext'

/**
 * The pages that update a profile: Keycloak's update profile step and the review after a first brokered sign-in. Both
 * draw one single-line input per user profile field, filled with its current value, and only the title differs. It is
 * a real `<form method="post">` to the login address, with each field posted under its attribute name. A field's error
 * shows under it, and an error that belongs to no field shows above the form, next to Keycloak's reminder.
 */
export function UpdateProfilePage({ context }: { context: UpdateProfileContext }) {
  const { texts, notice, error, loginAction, fields } = context
  const { colour } = useDesignTokens()
  const formId = useId()
  const labelStyle = { fontWeight: 500, color: colour.textMuted }
  const submitRef = useRef<HTMLButtonElement>(null)
  const [form] = Form.useForm()
  const initialValues = Object.fromEntries(fields.map((field) => [field.name, field.value ?? '']))
  useEffect(() => {
    // Keycloak's errors show under their fields, and the next validation of a field clears its error.
    form.setFields(fields.filter((field) => field.error).map((field) => ({ name: field.name, errors: [field.error!] })))
    // Only the page's first draw shows the errors of the post that Keycloak answered.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  return (
    <IdentityFrame title={context.title} frame={context}>
      {/* oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert. */}
      {notice && <Alert type="info" showIcon title={notice} />}
      {/* oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert. */}
      {error && <Alert type="error" showIcon title={error} />}
      <Form
        form={form}
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        initialValues={initialValues}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        {fields.map((field) => (
          <Form.Item
            key={field.name}
            name={field.name}
            label={<span style={labelStyle}>{field.label}</span>}
            rules={
              field.required && !field.readOnly
                ? [{ required: true, message: texts.fieldRequired.replace('{field}', field.label) }]
                : []
            }
          >
            <Input
              name={field.name}
              readOnly={field.readOnly}
              type={field.name === 'email' ? 'email' : 'text'}
              autoComplete={field.autoComplete}
            />
          </Form.Item>
        ))}
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
        <Button ref={submitRef} type="primary" htmlType="submit" block>
          {texts.submit}
        </Button>
      </Form>
    </IdentityFrame>
  )
}
