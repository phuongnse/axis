import { Button, Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { useDesignTokens } from '../platform/themeMode'
import type { SignInLocale } from './signInContext'

/** The frame of the identity provider's pages: the organization's mark and name above one card, with the language links and the copyright line below. */
export function IdentityFrame({
  title,
  organization,
  locales,
  children,
}: {
  title: string
  /** The organization's name, shown with its mark above the card. */
  organization: string
  locales: SignInLocale[]
  children: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  // A design-system frame sets --shell-min-height to its own height, so the page fills it.
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{
        paddingBlock: spacing.lg,
        background: colour.canvas,
        minHeight: 'var(--shell-min-height, 100%)',
        boxSizing: 'border-box',
      }}
    >
      {/* Proposed: the organization people sign in to heads the page, in place of the Axis mark. */}
      <OrganizationHeading name={organization} />
      <Card
        style={{
          width: '100%',
          maxWidth: 400,
          background: colour.surface,
          borderColor: colour.borderSubtle,
          borderRadius: radius.base,
        }}
        styles={{ body: { padding: spacing.xl } }}
      >
        <Flex vertical gap={spacing.md}>
          {/* Proposed: the organization's name is above the card, so the card's title stands alone. */}
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {children}
        </Flex>
      </Card>
      <Flex gap={spacing.sm}>
        {locales.map((locale) => (
          <Button key={locale.url} type="link" href={locale.url}>
            {locale.label}
          </Button>
        ))}
      </Flex>
      <Typography.Text type="secondary">{`© ${year} Axis`}</Typography.Text>
    </Flex>
  )
}
