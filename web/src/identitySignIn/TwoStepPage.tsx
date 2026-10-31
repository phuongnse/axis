import { Alert, Button, Flex, Form, Typography } from 'antd'
import { useId, useRef } from 'react'
import { CodeField } from './CodeField'
import { IdentityFrame } from './IdentityFrame'
import type { TwoStepContext } from './signInContext'

/**
 * The two-step verification page: the 6-digit code in six boxes, one digit each. The code form is a real
 * `<form method="post">` to the login address, with the code in `otp`. "Try another way" is a second form that posts
 * `tryAnotherWay=on`, and it shows only when Keycloak offers another way. "Back to sign in" restarts the sign-in.
 */
export function TwoStepPage({ context }: { context: TwoStepContext }) {
  const { texts, error, loginAction, restartUrl, tryAnotherWay } = context
  const formId = useId()
  const [form] = Form.useForm<{ code?: string }>()
  const submitRef = useRef<HTMLInputElement>(null)
  return (
    <IdentityFrame title={texts.title} organization={context.realmName} preferences={context.preferences}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
      <Form
        form={form}
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <CodeField
          form={form}
          postAs="otp"
          label={texts.code}
          requiredMessage={texts.message}
          invalid={Boolean(error)}
        />
        <input ref={submitRef} type="hidden" name="login" value="Sign In" />
        <Button type="primary" htmlType="submit" block>
          {texts.verify}
        </Button>
      </Form>
      <Flex justify={tryAnotherWay ? 'space-between' : 'flex-start'}>
        {tryAnotherWay && (
          <form method="post" action={loginAction}>
            <input type="hidden" name="tryAnotherWay" value="on" />
            <Button type="link" htmlType="submit">
              {texts.tryAnotherWay}
            </Button>
          </form>
        )}
        <Typography.Link href={restartUrl}>{texts.backToSignIn}</Typography.Link>
      </Flex>
    </IdentityFrame>
  )
}
