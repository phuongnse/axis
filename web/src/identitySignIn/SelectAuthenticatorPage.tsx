import { Alert, Button, Flex, Typography } from 'antd'
import { IdentityFrame } from './IdentityFrame'
import type { SelectAuthenticatorContext } from './signInContext'

/**
 * The page behind "Try another way": one row per method that Keycloak offers, with its name and a help line. It is one
 * real `<form method="post">` to the login address, and each row is a submit button that posts its method's execution
 * id as `authenticationExecution`, as Keycloak's own page does. "Back to sign in" restarts the sign-in.
 */
export function SelectAuthenticatorPage({ context }: { context: SelectAuthenticatorContext }) {
  const { texts, error, loginAction, restartUrl, methods } = context
  return (
    <IdentityFrame title={texts.title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
      <form method="post" action={loginAction}>
        <Flex vertical gap="small">
          {methods.map((method) => (
            // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
            <Button
              key={method.id}
              block
              htmlType="submit"
              name="authenticationExecution"
              value={method.id}
              style={{
                height: 'auto',
                justifyContent: 'flex-start',
                textAlign: 'left',
                whiteSpace: 'normal',
                padding: '8px 16px',
              }}
            >
              <Flex vertical>
                <Typography.Text strong>{method.label}</Typography.Text>
                {method.help && <Typography.Text type="secondary">{method.help}</Typography.Text>}
              </Flex>
            </Button>
          ))}
        </Flex>
      </form>
      <Typography.Link href={restartUrl}>{texts.backToSignIn}</Typography.Link>
    </IdentityFrame>
  )
}
