import { Alert, Button, Form, Input, Typography } from 'antd'
import { useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { UpdatePasswordContext } from './signInContext'

/**
 * The page that sets a password, which an invitation and a reset link open. It is a real `<form method="post">` to the
 * login address, with `password-new` and `password-confirm`. The hidden `logout-sessions=on` keeps Keycloak's default
 * of signing the person out on other devices. The browser checks only that both fields are filled and that they match.
 * Keycloak checks the password rules and shows its error above the form.
 */
export function UpdatePasswordPage({ context }: { context: UpdatePasswordContext }) {
  const { texts, error, loginAction } = context
  const { colour } = useDesignTokens()
  const formId = useId()
  const labelStyle = { fontWeight: 500, color: colour.textMuted }
  const submitRef = useRef<HTMLInputElement>(null)
  return (
    <IdentityFrame title={texts.title} organization={context.realmName} preferences={context.preferences}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
      <Form
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <Form.Item
          name="password-new"
          label={<span style={labelStyle}>{texts.newPassword}</span>}
          rules={[{ required: true, message: texts.newPasswordRequired }]}
          validateStatus={error ? 'error' : undefined}
        >
          <Input.Password name="password-new" autoComplete="new-password" />
        </Form.Item>
        <Form.Item
          name="password-confirm"
          label={<span style={labelStyle}>{texts.confirmPassword}</span>}
          dependencies={['password-new']}
          rules={[
            { required: true, message: texts.passwordsDiffer },
            ({ getFieldValue }) => ({
              validator: (_, value) =>
                !value || getFieldValue('password-new') === value
                  ? Promise.resolve()
                  : Promise.reject(new Error(texts.passwordsDiffer)),
            }),
          ]}
        >
          <Input.Password name="password-confirm" autoComplete="new-password" />
        </Form.Item>
        <input ref={submitRef} type="hidden" name="logout-sessions" value="on" />
        <Button type="primary" htmlType="submit" block>
          {texts.setPassword}
        </Button>
      </Form>
    </IdentityFrame>
  )
}
