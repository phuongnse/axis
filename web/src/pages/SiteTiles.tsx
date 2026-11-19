import { Flex, Typography } from 'antd'
import { SiteMark } from '../platform/SiteMark'
import { localized, siteTitle, type SiteListItem } from '../platform/site'
import { siteAddress } from '../platform/siteLinks'
import { useText } from '../platform/texts'
import { Tile } from '../platform/Tile'
import { useDesignTokens } from '../platform/themeMode'
import { useHomeGrid } from './homeGrid'

/** The sites as tiles in the home page's grid, by title. `newTab` opens each site in a new tab, named so. */
export function SiteTiles({
  sites,
  locale,
  newTab = false,
}: {
  sites: SiteListItem[]
  locale: string
  newTab?: boolean
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const grid = useHomeGrid()
  const sorted = [...sites].sort((a, b) => siteTitle(a, locale).localeCompare(siteTitle(b, locale), locale))

  return (
    <div data-testid="home-grid" style={grid}>
      {sorted.map((site) => {
        const title = siteTitle(site, locale)
        const description = localized(site.descriptions, locale)
        return (
          <Tile
            key={site.path}
            to={siteAddress(site.path)}
            bordered={false}
            align={description === null ? 'center' : 'flex-start'}
            target={newTab ? '_blank' : undefined}
            rel={newTab ? 'noopener noreferrer' : undefined}
            aria-label={newTab ? t('shell.studio.overview.opensInNewTab').replace('{site}', title) : undefined}
          >
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
          </Tile>
        )
      })}
    </div>
  )
}
