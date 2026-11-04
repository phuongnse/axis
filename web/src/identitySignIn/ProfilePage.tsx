import { Alert, Button, Form, Input, Typography } from 'antd'
import { useId, useRef } from 'react'
import { IdentityFrame } from './IdentityFrame'
import type { ProfileContext } from './signInContext'

/**
 * Proposed: the profile page, which Keycloak shows when an administrator asks a person to update their profile or a
 * brokered provider leaves out a required field: each field of the user profile, with the form widget's labels, a
 * required field checked by the browser before the post and Keycloak's own message under a field it refused. A
 * read-only field shows its value and cannot change. It is a real `<form method="post">` to the login address with
 * each field by its name. Cancel shows only when an application asked for the step, and posts `cancel-aia=true`.
 */
export function ProfilePage({ context }: { context: ProfileContext }) {
  const { texts, fields, hidden, error, notice, loginAction, cancellable } = context
  const formId = useId()
  const [form] = Form.useForm()
  const submitRef = useRef<HTMLInputElement>(null)
  const initialValues = Object.fromEntries(fields.map((field) => [field.name, field.value]))

  return (
    <IdentityFrame
      title={texts.title}
      organization={context.realmName}
      preferences={context.preferences}
      legal={context.legal}
    >
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error ? <Alert type="error" showIcon title={error} /> : notice && <Alert type="info" showIcon title={notice} />}
      <Form
        form={form}
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        initialValues={initialValues}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <input ref={submitRef} type="hidden" name="submitAction" value="Submit" />
        {Object.entries(hidden).map(([name, value]) => (
          <input key={name} type="hidden" name={name} value={value} />
        ))}
        {fields.map((field) => (
          <Form.Item
            key={field.name}
            name={field.name}
            label={field.label}
            required={field.required}
            validateStatus={field.error ? 'error' : undefined}
            help={field.error}
            rules={
              field.required && !field.readOnly ? [{ required: true, whitespace: true, message: texts.required }] : []
            }
          >
            <Input
              name={field.name}
              type={field.type}
              readOnly={field.readOnly}
              autoComplete={field.type === 'email' ? 'email' : undefined}
            />
          </Form.Item>
        ))}
        <Button type="primary" htmlType="submit" block>
          {texts.submit}
        </Button>
      </Form>
      {cancellable && (
        <form method="post" action={loginAction}>
          <input type="hidden" name="cancel-aia" value="true" />
          <Button type="link" htmlType="submit" style={{ paddingInline: 0 }}>
            {texts.cancel}
          </Button>
        </form>
      )}
    </IdentityFrame>
  )
}
