import { Flex, Typography } from 'antd'
import { useDesignTokens } from './themeMode'

/** The labels of the legal line: the landmark's name and its three links. */
export interface LegalTexts {
  legal: string
  privacy: string
  terms: string
  licences: string
}

/** The addresses of the legal line's links, each absent when not set, and the labels. */
export interface LegalLinks {
  privacyUrl?: string
  termsUrl?: string
  licencesUrl?: string
  texts: LegalTexts
}

/**
 * The legal line that ends the identity provider's pages and the shell's footer: the copyright in the muted colour at the
 * start, then Privacy, Terms and Licences as links in a navigation landmark. A link whose address is missing is left out.
 */
export function LegalLine({ copyright, links }: { copyright: string; links: LegalLinks }) {
  const { colour, spacing } = useDesignTokens()
  const { privacyUrl, termsUrl, licencesUrl, texts } = links
  const items = [
    { href: privacyUrl, label: texts.privacy },
    { href: termsUrl, label: texts.terms },
    { href: licencesUrl, label: texts.licences },
  ].filter((item): item is { href: string; label: string } => Boolean(item.href))

  return (
    <Flex wrap justify="space-between" align="center" gap={spacing.sm} style={{ width: '100%' }}>
      <Typography.Text style={{ color: colour.textMuted }}>{copyright}</Typography.Text>
      {items.length > 0 && (
        <nav aria-label={texts.legal} style={{ marginInlineStart: 'auto' }}>
          <Flex wrap justify="flex-end" gap={spacing.sm}>
            {items.map(({ href, label }) => (
              <Typography.Link key={label} href={href} style={{ color: colour.primaryText }}>
                {label}
              </Typography.Link>
            ))}
          </Flex>
        </nav>
      )}
    </Flex>
  )
}
