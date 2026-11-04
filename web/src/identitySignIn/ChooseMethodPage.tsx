import { RightOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import { IdentityFrame } from './IdentityFrame'
import type { ChooseMethodContext } from './signInContext'
import { useDesignTokens } from '../platform/themeMode'

/**
 * Proposed: the page that "Try another way" opens: each way to sign in that Keycloak offers, such as the
 * authenticator code or the password, as one full-width row with its name, its help line and a chevron. A row is a
 * submit button of a real `<form method="post">` that posts `authenticationExecution`.
 */
export function ChooseMethodPage({ context }: { context: ChooseMethodContext }) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <IdentityFrame
      title={context.title}
      organization={context.realmName}
      preferences={context.preferences}
      legal={context.legal}
    >
      <form method="post" action={context.loginAction}>
        <Flex vertical gap={spacing.sm}>
          {context.methods.map((method) => (
            <button
              key={method.id}
              type="submit"
              name="authenticationExecution"
              value={method.id}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: spacing.md,
                width: '100%',
                padding: `${spacing.sm}px ${spacing.md}px`,
                background: colour.surface,
                border: `1px solid ${colour.border}`,
                borderRadius: radius.base,
                cursor: 'pointer',
                font: 'inherit',
                textAlign: 'start',
              }}
            >
              <Flex vertical style={{ flex: 1, minWidth: 0 }}>
                <Typography.Text strong>{method.label}</Typography.Text>
                {method.help && <Typography.Text style={{ color: colour.textMuted }}>{method.help}</Typography.Text>}
              </Flex>
              <RightOutlined aria-hidden style={{ color: colour.textMuted }} />
            </button>
          ))}
        </Flex>
      </form>
    </IdentityFrame>
  )
}
