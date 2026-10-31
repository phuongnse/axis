import { SettingOutlined } from '@ant-design/icons'
import { Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { NamedIconButton } from '../platform/AxisButton'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { nativeLanguageName } from '../platform/languageName'
import { PreferencesDropdown } from '../platform/PreferencesMenu'
import { useDesignTokens } from '../platform/themeMode'
import type { SignInPreferences } from './signInContext'

/**
 * The frame of the identity provider's pages: the organization's mark and name above one card, whose title row holds
 * the language and theme button, and the copyright line below.
 */
export function IdentityFrame({
  title,
  organization,
  preferences,
  children,
}: {
  title: string
  /** The organization's name for the heading above the card. Absent means Axis. */
  organization?: string
  preferences: SignInPreferences
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
          {/* Proposed: the shell's language and theme button sits at the card's top right, on the title's row, so it is
              as far from the top edge as from the right one. Its menu holds every language, named in its own language,
              and System, Light and Dark, so more languages only add rows. */}
          <Flex align="center" justify="space-between" gap={spacing.sm}>
            <Typography.Title level={4} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            <PreferencesDropdown
              languages={preferences.locales.map((entry) => ({ tag: entry.tag, label: nativeLanguageName(entry.tag) }))}
              language={preferences.locale}
              texts={preferences.texts}
              onLanguageChange={(tag) => {
                const target = preferences.locales.find((entry) => entry.tag === tag)
                if (target) {
                  window.location.assign(target.url)
                }
              }}
            >
              <NamedIconButton name={preferences.texts.label} icon={<SettingOutlined />} />
            </PreferencesDropdown>
          </Flex>
          {children}
        </Flex>
      </Card>
      <Typography.Text type="secondary">{`© ${year} Axis`}</Typography.Text>
    </Flex>
  )
}
