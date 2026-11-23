import { Form, Input, Typography } from 'antd'
import { AxisForm } from '../platform/AxisForm'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { ResetPasswordContext } from './signInContext'

/**
 * The "Forgot password?" page: the email or username in a real `<form method="post">` to the login address, with the
 * value in `username`. Keycloak sends the link and shows its own page after that. "Back to sign in" goes to `loginUrl`.
 */
export function ResetPasswordPage({ context }: { context: ResetPasswordContext }) {
  const { texts, error, username, loginAction, loginUrl } = context
  const { colour } = useDesignTokens()
  const formId = useId()
  const submitRef = useRef<HTMLInputElement>(null)
  return (
    <IdentityFrame title={texts.title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <AxisAlert kind="error" message={error} />}
      <AxisForm
        name={formId}
        method="post"
        action={loginAction}
        unmarked
        initialValues={{ username }}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <Form.Item
          name="username"
          label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{texts.username}</span>}
          rules={[{ required: true, message: texts.usernameRequired }]}
          validateStatus={error ? 'error' : undefined}
        >
          <Input name="username" autoComplete="username" />
        </Form.Item>
        {/* No name, so the field posts nothing. It only gives onFinish the form. */}
        <input ref={submitRef} type="hidden" />
        <AxisButton role="finish" htmlType="submit" block>
          {texts.sendLink}
        </AxisButton>
      </AxisForm>
      <Typography.Link href={loginUrl}>{texts.backToSignIn}</Typography.Link>
    </IdentityFrame>
  )
}
