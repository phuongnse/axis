import { SettingOutlined } from '@ant-design/icons'
import { Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { NamedIconButton } from '../platform/AxisButton'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { nativeLanguageName } from '../platform/languageName'
import { PreferencesDropdown } from '../platform/PreferencesMenu'
import { useDesignTokens } from '../platform/themeMode'
import { LegalLine } from '../platform/LegalLine'
import type { SignInLegal, SignInPreferences } from './signInContext'

/**
 * The frame of the identity provider's pages: the organization's mark and name above one card, the copyright and the
 * legal links below, and the language and theme button at the page's top right.
 */
export function IdentityFrame({
  title,
  organization,
  preferences,
  legal,
  children,
}: {
  title: string
  /** The organization's name for the heading above the card. Absent means Axis. */
  organization?: string
  preferences: SignInPreferences
  legal: SignInLegal
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
        position: 'relative',
      }}
    >
      {/* Proposed: the shell's language and theme button sits at the page's top right, where the shell header has it,
          as far from the top edge as from the right one and level with the organization's heading. The card holds
          only the sign-in. Its menu holds every language, named in its own language, and System, Light and Dark, so
          more languages only add rows. */}
      <div style={{ position: 'absolute', top: spacing.lg, insetInlineEnd: spacing.lg }}>
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
      </div>
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
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {children}
        </Flex>
      </Card>
      {/* Proposed: the line is as wide as the card, so the copyright lines up with its start and the links with its end. */}
      <div style={{ width: '100%', maxWidth: 400 }}>
        <LegalLine copyright={`© ${year} Axis`} links={legal.links} texts={legal.texts} />
      </div>
    </Flex>
  )
}
