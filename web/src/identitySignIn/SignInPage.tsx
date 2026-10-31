import { Alert, Button, Checkbox, Divider, Flex, Form, Input, Typography } from 'antd'
import { useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { SignInContext } from './signInContext'

/** The identity provider's sign-in page: the providers first, then the account form when accounts are allowed. */
export function SignInPage({ context }: { context: SignInContext }) {
  return (
    <IdentityFrame title={context.title} organization={context.realmName} preferences={context.preferences}>
      <SignInBody context={context} />
    </IdentityFrame>
  )
}

function SignInBody({ context }: { context: SignInContext }) {
  const { site, texts, error } = context
  const providers = site
    ? context.providers.filter((provider) => site.providers.includes(provider.alias))
    : context.providers
  const accounts = context.accounts && (site?.accounts ?? true)
  const only = !accounts && providers.length === 1
  const goTo = (url: string) => window.location.assign(url)
  return (
    <>
      {only && (
        <Typography.Paragraph style={{ margin: 0 }}>
          {texts.providerOnly.replace('{provider}', providers[0].label)}
        </Typography.Paragraph>
      )}
      {providers.map((provider) => (
        <Button key={provider.alias} type="default" block onClick={() => goTo(provider.loginUrl)}>
          {texts.continueWith.replace('{provider}', provider.label)}
        </Button>
      ))}
      {accounts && providers.length > 0 && <Divider plain>{texts.or}</Divider>}
      {accounts && error && <Alert type="error" showIcon title={error} />}
      {accounts && <AccountForm context={context} />}
    </>
  )
}

function AccountForm({ context }: { context: SignInContext }) {
  const { username, error, loginAction, rememberMe, forgotPasswordUrl, texts } = context
  const { colour } = useDesignTokens()
  const formId = useId()
  const labelStyle = { fontWeight: 500, color: colour.textMuted }
  const credentialRef = useRef<HTMLInputElement>(null)
  return (
    <Form
      name={formId}
      method="post"
      action={loginAction}
      layout="vertical"
      requiredMark={false}
      initialValues={{ username }}
      // Ant Design validates and stops the submit event, so the native post happens here.
      onFinish={() => credentialRef.current?.form?.submit()}
    >
      <Form.Item
        name="username"
        label={<span style={labelStyle}>{texts.username}</span>}
        rules={[{ required: true, message: texts.usernameRequired }]}
      >
        <Input name="username" autoComplete="username" />
      </Form.Item>
      {/* The link sits beside the label, not inside it, so the label's name stays "Password". */}
      <Flex justify="space-between" align="baseline" style={{ paddingBottom: 8 }}>
        <label htmlFor={`${formId}_password`} style={labelStyle}>
          {texts.password}
        </label>
        {forgotPasswordUrl && <Typography.Link href={forgotPasswordUrl}>{texts.forgotPassword}</Typography.Link>}
      </Flex>
      <Form.Item
        name="password"
        rules={[{ required: true, message: texts.passwordRequired }]}
        validateStatus={error ? 'error' : undefined}
      >
        <Input.Password name="password" autoComplete="current-password" />
      </Form.Item>
      {rememberMe && (
        <Form.Item name="rememberMe" valuePropName="checked">
          <Checkbox name="rememberMe">{texts.keepSignedIn}</Checkbox>
        </Form.Item>
      )}
      <input ref={credentialRef} type="hidden" name="credentialId" value="" />
      <Button type="primary" htmlType="submit" block>
        {texts.signIn}
      </Button>
    </Form>
  )
}
