import { Button, Flex, Form, Input, Typography } from 'antd'
import { useId } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { SignInLocale, TwoStepTexts } from './signInContext'

/** The two-step verification page: the 6-digit code in six boxes, one digit each. Only the design system's preview draws it for now. */
export function TwoStepPage({
  texts,
  organization,
  locales,
}: {
  texts: TwoStepTexts
  organization: string
  locales: SignInLocale[]
}) {
  const { colour } = useDesignTokens()
  const formId = useId()
  return (
    <IdentityFrame title={texts.title} organization={organization} locales={locales}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      <Form name={formId} layout="vertical" requiredMark={false}>
        <Form.Item name="code" label={<span style={{ fontWeight: 500, color: colour.textMuted }}>{texts.code}</span>}>
          <Input.OTP length={6} formatter={(value) => value.replace(/\D/g, '')} />
        </Form.Item>
        <Button type="primary" htmlType="submit" block>
          {texts.verify}
        </Button>
      </Form>
      <Flex justify="space-between">
        <Button type="link">{texts.tryAnotherWay}</Button>
        <Button type="link">{texts.backToSignIn}</Button>
      </Flex>
    </IdentityFrame>
  )
}
