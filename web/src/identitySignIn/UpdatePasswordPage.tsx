import { Button, Form, Input, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { UpdatePasswordContext } from './signInContext'

/**
 * The page that sets a password, which an invitation and a reset link open. It is a real `<form method="post">` to the
 * login address, with `password-new` and `password-confirm`. The hidden `logout-sessions=on` keeps Keycloak's default
 * of signing the person out on other devices. The browser checks only that both fields are filled and that they match.
 * Keycloak checks the password rules and shows its error above the form. When an application started the step, a second
 * form below posts `cancel-aia=true`, which skips these checks and returns to the application with nothing changed.
 */
export function UpdatePasswordPage({ context }: { context: UpdatePasswordContext }) {
  const { texts, error, loginAction, appInitiated } = context
  const { colour } = useDesignTokens()
  const formId = useId()
  const labelStyle = { fontWeight: 500, color: colour.textMuted }
  const submitRef = useRef<HTMLInputElement>(null)
  return (
    <IdentityFrame title={texts.title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <AxisAlert kind="error" message={error} />}
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
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
        <Button type="primary" htmlType="submit" block>
          {texts.setPassword}
        </Button>
      </Form>
      {appInitiated && (
        <form method="post" action={loginAction}>
          <input type="hidden" name="cancel-aia" value="true" />
          {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
          <Button htmlType="submit" name="cancel-aia" value="true" block>
            {texts.cancel}
          </Button>
        </form>
      )}
    </IdentityFrame>
  )
}
