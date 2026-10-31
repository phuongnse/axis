import { Flex, Typography } from 'antd'
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
 * Proposed: one quiet line of the copyright and the legal links, shared by the identity provider's pages and the
 * shell's footer: Privacy and Terms when the site or its tenant sets them, then Open source licences, whose page also
 * links Axis's source, as the AGPL asks. It wraps at narrow width.
 */
export function LegalLine({ copyright, links, texts }: { copyright: string; links: LegalLinks; texts: LegalTexts }) {
  const { colour, spacing } = useDesignTokens()
  const entries = [
    { key: 'privacy', href: links.privacy, label: texts.privacy },
    { key: 'terms', href: links.terms, label: texts.terms },
    { key: 'licences', href: links.licences, label: texts.licences },
  ].filter((entry): entry is { key: string; href: string; label: string } => Boolean(entry.href))
  return (
    <Flex align="center" justify="center" wrap gap={spacing.md} style={{ minWidth: 0 }}>
      <Typography.Text style={{ color: colour.textMuted }}>{copyright}</Typography.Text>
      <nav aria-label={texts.label}>
        <Flex component="ul" wrap gap={spacing.md} style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {entries.map((entry) => (
            <li key={entry.key}>
              <Typography.Link href={entry.href} style={{ color: colour.textMuted }}>
                {entry.label}
              </Typography.Link>
            </li>
          ))}
        </Flex>
      </nav>
    </Flex>
  )
}
