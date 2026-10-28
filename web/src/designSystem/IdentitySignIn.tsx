import { Button, Flex, Form, Input, Typography } from 'antd'
import { useId } from 'react'
import { IdentityFrame } from '../identitySignIn/IdentityFrame'
import { SignInPage } from '../identitySignIn/SignInPage'
import { identityInvalid, identitySignInContext, identityTwoStepTexts } from './fixtures'

export type IdentitySignInPage = 'signIn' | 'wrongPassword' | 'organizationOnly' | 'twoStep'

/** The identity provider's sign-in page in one of its states, built from fixtures through the shared sign-in components. */
export function IdentitySignIn({ page }: { page: IdentitySignInPage }) {
  switch (page) {
    case 'signIn':
      return <SignInPage context={identitySignInContext} />
    case 'wrongPassword':
      return <SignInPage context={{ ...identitySignInContext, error: identityInvalid }} />
    case 'organizationOnly':
      return <SignInPage context={{ ...identitySignInContext, accounts: false }} />
    case 'twoStep':
      return (
        <IdentityFrame title={identityTwoStepTexts.twoStepTitle} locales={identitySignInContext.locales}>
          <TwoStepForm />
        </IdentityFrame>
      )
  }
}

/** Two-step verification is a preview-only state until the theme draws it. */
function TwoStepForm() {
  const formId = useId()
  return (
    <>
      <Typography.Paragraph style={{ margin: 0 }}>{identityTwoStepTexts.twoStepMessage}</Typography.Paragraph>
      <Form name={formId} layout="vertical" requiredMark={false}>
        <Form.Item name="code" label={identityTwoStepTexts.code}>
          <Input maxLength={6} inputMode="numeric" autoComplete="one-time-code" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block>
          {identityTwoStepTexts.verify}
        </Button>
      </Form>
      <Flex justify="space-between">
        <Button type="link">{identityTwoStepTexts.tryAnotherWay}</Button>
        <Button type="link">{identityTwoStepTexts.backToSignIn}</Button>
      </Flex>
    </>
  )
}
