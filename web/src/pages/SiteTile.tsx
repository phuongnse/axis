import { Flex, Typography } from 'antd'
import type { CSSProperties } from 'react'
import { Link } from 'react-router'
import { SiteMark } from '../platform/SiteMark'
import { localized, siteTitle, type SiteListItem } from '../platform/site'
import { siteAddress } from '../platform/siteLinks'
import { useDesignTokens } from '../platform/themeMode'

/** The keyboard focus ring of a link tile. Inline styles cannot target focus. */
export function TileFocusStyle() {
  const { colour } = useDesignTokens()
  return (
    <style>{`[data-home-tile]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: 2px; }`}</style>
  )
}

/**
 * A site as the home page shows it: its mark, its title as the link and its description under it. With `newTab`, as
 * Studio's Overview shows a live site, it opens in a new tab, which its accessible name says.
 */
export function SiteTile({ site, locale, newTab }: { site: SiteListItem; locale: string; newTab?: string }) {
  const { colour, spacing } = useDesignTokens()
  const title = siteTitle(site, locale)
  const description = localized(site.descriptions, locale)
  const style: CSSProperties = {
    display: 'flex',
    alignItems: description === null ? 'center' : 'flex-start',
    gap: spacing.sm,
    minWidth: 0,
  }
  const content = (
    <>
      <SiteMark title={title} size={20} />
      <Flex vertical style={{ minWidth: 0 }}>
        <Typography.Text strong style={{ color: colour.primaryText }}>
          {title}
        </Typography.Text>
        {description !== null && (
          <Typography.Paragraph
            ellipsis={{ rows: 2, tooltip: description }}
            style={{ margin: 0, color: colour.textMuted }}
          >
            {description}
          </Typography.Paragraph>
        )}
      </Flex>
    </>
  )
  if (newTab !== undefined) {
    return (
      <a
        href={siteAddress(site.path)}
        target="_blank"
        rel="noreferrer"
        aria-label={newTab}
        data-home-tile
        style={style}
      >
        {content}
      </a>
    )
  }
  return (
    <Link to={siteAddress(site.path)} data-home-tile style={style}>
      {content}
    </Link>
  )
}
