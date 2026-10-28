import { Alert, Checkbox, Divider, Flex, Input, Typography } from 'antd'
import type { ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { TitleMark } from '../platform/LaunchTile'
import { useDesignTokens } from '../platform/themeMode'
import { format, type IdentityMessages } from './messages'

/** The step of the sign-in flow a page shows. */
export type IdentityStep = 'signIn' | 'invalid' | 'organizationOnly' | 'otp'

/** An identity provider of the tenant's organization that Axis's identity provider brokers to, by its display name. */
export interface BrokeredProvider {
  alias: string
  name: string
}

/**
 * Proposed: the frame of every page of Axis's identity provider: the Axis mark and the organization above one card in
 * the middle of the page, with the language links and the copyright line below it. It draws on `canvas`, as the SPA
 * does, in light and dark mode.
 */
function IdentityFrame({ messages, children }: { messages: IdentityMessages; children: ReactNode }) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <Flex vertical align="center" gap={spacing.md} style={{ padding: spacing.xl, background: colour.canvas }}>
      <Flex align="center" gap={spacing.sm}>
        <TitleMark title="Axis" size={32} />
        <Typography.Text strong>Axis</Typography.Text>
      </Flex>
      <div
        style={{
          width: '100%',
          maxWidth: 400,
          padding: spacing.xl,
          background: colour.surface,
          border: `1px solid ${colour.borderSubtle}`,
          borderRadius: radius.base,
        }}
      >
        {children}
      </div>
      <Flex gap={spacing.md}>
        <Typography.Link href="#">{messages.english}</Typography.Link>
        <Typography.Link href="#">{messages.vietnamese}</Typography.Link>
      </Flex>
      <Typography.Text type="secondary">© 2026 Axis</Typography.Text>
    </Flex>
  )
}

/** The page's title over the organization the realm signs in to, its display name. */
function Heading({ title, organization }: { title: string; organization: string }) {
  const { spacing } = useDesignTokens()
  return (
    <Flex vertical style={{ marginBottom: spacing.lg }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        {title}
      </Typography.Title>
      <Typography.Text type="secondary">{organization}</Typography.Text>
    </Flex>
  )
}

function Field({ label, extra, children }: { label: string; extra?: ReactNode; children: ReactNode }) {
  const { spacing } = useDesignTokens()
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: spacing.xs }}>
      <Flex justify="space-between" align="baseline">
        <Typography.Text type="secondary" style={{ fontWeight: 500 }}>
          {label}
        </Typography.Text>
        {extra}
      </Flex>
      {children}
    </label>
  )
}

/**
 * Proposed: the sign-in page of Axis's identity provider. The organization's own identity providers come first, as
 * buttons that broker to them, then the account form of Axis's identity provider. An organization that signs in only
 * through its own provider shows its button alone. A wrong password keeps the username and says so above the form.
 */
export function IdentitySignIn({
  step,
  messages,
  organization,
  providers,
}: {
  step: IdentityStep
  messages: IdentityMessages
  /** The display name of the tenant's realm, the organization people sign in to. */
  organization: string
  providers: readonly BrokeredProvider[]
}) {
  const { spacing } = useDesignTokens()
  const brokers = (
    <Flex vertical gap={spacing.sm}>
      {providers.map((provider) => (
        <AxisButton key={provider.alias} role="secondary" block>
          {format(messages.continueWith, provider.name)}
        </AxisButton>
      ))}
    </Flex>
  )

  if (step === 'otp') {
    return (
      <IdentityFrame messages={messages}>
        <Heading title={messages.otpTitle} organization={organization} />
        <Flex vertical gap={spacing.md}>
          <Typography.Text>{messages.otpHint}</Typography.Text>
          <Field label={messages.otpLabel}>
            <Input.OTP length={6} />
          </Field>
          <AxisButton role="finish" block>
            {messages.verify}
          </AxisButton>
          <Flex justify="space-between">
            <Typography.Link href="#">{messages.tryAnotherWay}</Typography.Link>
            <Typography.Link href="#">{messages.back}</Typography.Link>
          </Flex>
        </Flex>
      </IdentityFrame>
    )
  }

  if (step === 'organizationOnly') {
    return (
      <IdentityFrame messages={messages}>
        <Heading title={messages.signInTitle} organization={organization} />
        <Flex vertical gap={spacing.md}>
          <Typography.Text>{format(messages.organizationOnly, providers[0]?.name ?? '')}</Typography.Text>
          {brokers}
        </Flex>
      </IdentityFrame>
    )
  }

  const invalid = step === 'invalid'
  return (
    <IdentityFrame messages={messages}>
      <Heading title={messages.signInTitle} organization={organization} />
      <Flex vertical gap={spacing.md}>
        {providers.length > 0 && (
          <>
            {brokers}
            <Divider plain style={{ margin: 0 }}>
              <Typography.Text type="secondary">{messages.or}</Typography.Text>
            </Divider>
          </>
        )}
        {invalid && <Alert type="error" showIcon title={messages.invalidCredentials} role="alert" />}
        <Field label={messages.username}>
          <Input autoComplete="username" defaultValue={invalid ? 'linh.tran@contoso.com' : undefined} />
        </Field>
        <Field label={messages.password} extra={<Typography.Link href="#">{messages.forgotPassword}</Typography.Link>}>
          <Input.Password autoComplete="current-password" status={invalid ? 'error' : undefined} />
        </Field>
        <Checkbox>{messages.rememberMe}</Checkbox>
        <AxisButton role="finish" block>
          {messages.signIn}
        </AxisButton>
      </Flex>
    </IdentityFrame>
  )
}
