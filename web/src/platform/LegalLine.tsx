import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useDesignTokens } from './themeMode'

/** Where the legal links lead. Privacy and terms are the organization's and show only when set. */
export interface LegalLinks {
  privacy?: string
  terms?: string
  /** The page that lists the licences of Axis and of every library it ships. Always set once the realm sync writes it. */
  licences?: string
}

/** The names of the legal links and of the navigation that holds them. */
export interface LegalTexts {
  label: string
  privacy: string
  terms: string
  licences: string
}

/**
 * Proposed: one line of the copyright and the legal links, shared by the identity provider's pages and the shell's
 * footer. The copyright sits at the start and the links at the end, drawn as links: Privacy and Terms when the site or
 * its tenant sets them, then Open source licences, whose page also links Axis's source, as the AGPL asks. `end`
 * follows the links, such as the shell's server status. At narrow width the links wrap and stay at the end.
 */
export function LegalLine({
  copyright,
  links,
  texts,
  end,
}: {
  copyright: string
  links: LegalLinks
  texts: LegalTexts
  end?: ReactNode
}) {
  const { colour, spacing } = useDesignTokens()
  const entries = [
    { key: 'privacy', href: links.privacy, label: texts.privacy },
    { key: 'terms', href: links.terms, label: texts.terms },
    { key: 'licences', href: links.licences, label: texts.licences },
  ].filter((entry): entry is { key: string; href: string; label: string } => Boolean(entry.href))
  return (
    <Flex align="center" justify="space-between" wrap gap={spacing.md} style={{ width: '100%', minWidth: 0 }}>
      <Typography.Text style={{ color: colour.textMuted }}>{copyright}</Typography.Text>
      <Flex align="center" justify="flex-end" wrap gap={spacing.md} style={{ marginInlineStart: 'auto' }}>
        <nav aria-label={texts.label}>
          <Flex component="ul" wrap gap={spacing.md} style={{ listStyle: 'none', margin: 0, padding: 0 }}>
            {entries.map((entry) => (
              <li key={entry.key}>
                <Typography.Link href={entry.href} style={{ color: colour.primaryText }}>
                  {entry.label}
                </Typography.Link>
              </li>
            ))}
          </Flex>
        </nav>
        {end}
      </Flex>
    </Flex>
  )
}
