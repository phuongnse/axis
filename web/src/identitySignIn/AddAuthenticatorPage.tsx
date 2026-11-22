import { Flex, Form, Input, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { CodeField } from './CodeField'
import { IdentityFrame } from './IdentityFrame'
import type { AddAuthenticatorContext } from './signInContext'

/**
 * The page that adds an authenticator: the QR code, the key to type by hand, the 6-digit code in six boxes and the
 * device name. The form is a real `<form method="post">` to the login address, with the code in `totp`, the secret in
 * `totpSecret`, the device name in `userLabel` and Keycloak's `mode` when it gave one. When an application started the
 * step, a second form below posts `cancel-aia=true`, which skips the checks and returns to the application.
 */
export function AddAuthenticatorPage({ context }: { context: AddAuthenticatorContext }) {
  const { texts, error, loginAction, qrCode, manualKey, secret, mode, deviceNameRequired, appInitiated } = context
  const { colour, spacing } = useDesignTokens()
  const formId = useId()
  const [form] = Form.useForm<{ code?: string; userLabel?: string }>()
  const submitRef = useRef<HTMLInputElement>(null)
  return (
    <IdentityFrame title={texts.title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <AxisAlert kind="error" message={error} />}
      <Flex vertical align="center" gap={8}>
        <img src={`data:image/png;base64,${qrCode}`} alt={texts.qrCode} width={160} height={160} />
        <Typography.Text type="secondary">{texts.manualKey}</Typography.Text>
        <Typography.Text code copyable>
          {manualKey}
        </Typography.Text>
      </Flex>
      <Form
        form={form}
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        scrollToFirstError={{ focus: true }}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <Flex vertical gap={spacing.lg}>
          <CodeField
            form={form}
            postAs="totp"
            label={texts.code}
            requiredMessage={texts.codeRequired}
            invalid={Boolean(error)}
          />
          <input ref={submitRef} type="hidden" name="totpSecret" value={secret} />
          {mode && <input type="hidden" name="mode" value={mode} />}
          <Form.Item
            name="userLabel"
            label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{texts.deviceName}</span>}
            rules={[
              { required: deviceNameRequired, whitespace: deviceNameRequired, message: texts.deviceNameRequired },
            ]}
          >
            <Input name="userLabel" autoComplete="off" />
          </Form.Item>
          <AxisButton role="finish" htmlType="submit" block>
            {texts.submit}
          </AxisButton>
        </Flex>
      </Form>
      {appInitiated && (
        <form method="post" action={loginAction}>
          <input type="hidden" name="cancel-aia" value="true" />
          <AxisButton role="secondary" htmlType="submit" name="cancel-aia" value="true" block>
            {texts.cancel}
          </AxisButton>
        </form>
      )}
    </IdentityFrame>
  )
}
