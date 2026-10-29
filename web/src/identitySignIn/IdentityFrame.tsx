import { Button, Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { SiteMark } from '../platform/SiteMark'
import { useDesignTokens } from '../platform/themeMode'
import type { SignInLocale } from './signInContext'

/** The frame of the identity provider's pages: the Axis mark above one card, with the language links and the copyright line below. */
export function IdentityFrame({
  title,
  organization,
  locales,
  children,
}: {
  title: string
  /** The organization's name, shown under the title. */
  organization: string
  locales: SignInLocale[]
  children: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{ paddingBlock: spacing.lg, background: colour.canvas, minHeight: '100%' }}
    >
      <Flex align="center" gap={spacing.xs}>
        <SiteMark title="Axis" size={32} />
        <Typography.Text strong style={{ fontSize: 20 }}>
          Axis
        </Typography.Text>
      </Flex>
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
          <Flex vertical gap={4}>
            <Typography.Title level={4} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            <Typography.Text style={{ color: colour.textMuted }}>{organization}</Typography.Text>
          </Flex>
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
