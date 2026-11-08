import { Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { sizeTokens } from './designTokens'
import { LegalLine, type LegalLinks } from './LegalLine'
import { OrganizationHeading } from './OrganizationHeading'
import { useDesignTokens } from './themeMode'

/**
 * Proposed: the frame of every page that is one card on its own, such as the identity provider's pages and the sign-up
 * pages: the organization's mark and name above the card, the "Language and theme" gear at the top right and the legal
 * line below. Every such page is one step of the same journey, so the card has one width, a small dialog's.
 */
export function CardPage({
  title,
  organization,
  preferences,
  legal,
  children,
}: {
  title: string
  /** The organization's name above the card. No name means Axis. */
  organization?: string
  /** The "Language and theme" gear, with the texts its page resolves it with. */
  preferences: ReactNode
  legal: LegalLinks
  children: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  const width = sizeTokens.dialog.small
  // A design-system frame sets --shell-min-height to its own height, so the page fills it.
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{
        position: 'relative',
        paddingBlock: spacing.lg,
        background: colour.canvas,
        minHeight: 'var(--shell-min-height, 100vh)',
        boxSizing: 'border-box',
      }}
    >
      <div style={{ position: 'absolute', top: spacing.lg, right: spacing.lg }}>{preferences}</div>
      <OrganizationHeading name={organization} />
      <Card
        style={{
          width: '100%',
          maxWidth: width,
          background: colour.surface,
          borderColor: colour.borderSubtle,
          borderRadius: radius.base,
        }}
        styles={{ body: { padding: spacing.xl } }}
      >
        <Flex vertical gap={spacing.md}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {children}
        </Flex>
      </Card>
      <div style={{ width: '100%', maxWidth: width }}>
        <LegalLine copyright={`© ${year} Axis`} links={legal} />
      </div>
    </Flex>
  )
}
