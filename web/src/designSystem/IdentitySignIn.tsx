import { Alert, Button, Card, Checkbox, Divider, Flex, Form, Input, Typography } from 'antd'
import { useId, useState } from 'react'
import { SiteMark } from '../platform/SiteMark'
import { useDesignTokens } from '../platform/themeMode'
import { identityTexts } from './fixtures'

export type IdentitySignInPage = 'signIn' | 'wrongPassword' | 'organizationOnly' | 'twoStep'

/** The identity provider's sign-in page in one of its states, drawn from fixtures until M17 builds the theme. */
export function IdentitySignIn({ page }: { page: IdentitySignInPage }) {
  const { spacing } = useDesignTokens()
  const formId = useId()
  const [year] = useState(() => new Date().getFullYear())
  return (
    <Flex vertical align="center" gap={spacing.md} style={{ paddingBlock: spacing.lg }}>
      <SiteMark title="Axis" size={40} />
      <Card style={{ width: '100%', maxWidth: 400 }}>
        <Flex vertical gap={spacing.md}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {page === 'twoStep' ? identityTexts.twoStepTitle : identityTexts.title}
          </Typography.Title>
          {page === 'twoStep' ? <TwoStepForm formId={formId} /> : <SignInForm page={page} formId={formId} />}
        </Flex>
      </Card>
      <Flex gap={spacing.sm}>
        <Button type="link">{identityTexts.english}</Button>
        <Button type="link">{identityTexts.vietnamese}</Button>
      </Flex>
      <Typography.Text type="secondary">{`© ${year} Axis`}</Typography.Text>
    </Flex>
  )
}

function SignInForm({ page, formId }: { page: Exclude<IdentitySignInPage, 'twoStep'>; formId: string }) {
  if (page === 'organizationOnly') {
    return (
      <>
        <Typography.Paragraph style={{ margin: 0 }}>{identityTexts.providerOnly}</Typography.Paragraph>
        <Button type="primary" block>
          {identityTexts.provider}
        </Button>
      </>
    )
  }
  const wrongPassword = page === 'wrongPassword'
  return (
    <>
      <Button block>{identityTexts.provider}</Button>
      <Divider plain>{identityTexts.or}</Divider>
      {wrongPassword && <Alert type="error" showIcon title={identityTexts.invalid} />}
      <Form
        name={formId}
        layout="vertical"
        requiredMark={false}
        initialValues={{ username: identityTexts.usernameValue }}
      >
        <Form.Item name="username" label={identityTexts.username}>
          <Input autoComplete="username" />
        </Form.Item>
        <Form.Item name="password" label={identityTexts.password} validateStatus={wrongPassword ? 'error' : undefined}>
          <Input.Password autoComplete="current-password" />
        </Form.Item>
        <Flex justify="flex-end">
          <Button type="link">{identityTexts.forgotPassword}</Button>
        </Flex>
        <Form.Item name="keep" valuePropName="checked">
          <Checkbox>{identityTexts.keepSignedIn}</Checkbox>
        </Form.Item>
        <Button type="primary" htmlType="submit" block>
          {identityTexts.signIn}
        </Button>
      </Form>
    </>
  )
}

function TwoStepForm({ formId }: { formId: string }) {
  return (
    <>
      <Typography.Paragraph style={{ margin: 0 }}>{identityTexts.twoStepMessage}</Typography.Paragraph>
      <Form name={formId} layout="vertical" requiredMark={false}>
        <Form.Item name="code" label={identityTexts.code}>
          <Input maxLength={6} inputMode="numeric" autoComplete="one-time-code" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block>
          {identityTexts.verify}
        </Button>
      </Form>
      <Flex justify="space-between">
        <Button type="link">{identityTexts.tryAnotherWay}</Button>
        <Button type="link">{identityTexts.backToSignIn}</Button>
      </Flex>
    </>
  )
}
