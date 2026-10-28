import { DashboardOutlined, ToolOutlined } from '@ant-design/icons'
import { Alert, Avatar, Empty, Flex, Spin, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { AxisButton } from '../platform/AxisButton'
import type { CurrentUser } from '../platform/currentUser'
import { localizedTitle, useCurrentUser, useSites } from '../platform/launcher'
import { LaunchTile, TitleMark } from '../platform/LaunchTile'
import { PageLayout } from '../platform/PageLayout'
import type { SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useNarrowWidth } from '../platform/useNarrowWidth'
import { useDesignTokens } from '../platform/themeMode'
import { usePlatform } from './context'

/** Proposed: the tiles of a section fill a grid of columns at least 220 px wide. */
function TileGrid({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: spacing.md }}>
      {children}
    </div>
  )
}

/** Proposed: a section of the home page is a panel on `surface`, titled, with the number of its tiles. */
function Section({ id, title, count, children }: { id: string; title: string; count?: number; children: ReactNode }) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <section
      aria-labelledby={id}
      style={{
        padding: spacing.lg,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        minWidth: 0,
      }}
    >
      <Flex align="baseline" gap={spacing.xs} style={{ marginBottom: spacing.md }}>
        <Typography.Title id={id} level={5} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {count !== undefined && <Typography.Text type="secondary">{count}</Typography.Text>}
      </Flex>
      {children}
    </section>
  )
}

/** Proposed: with nobody signed in, the sites give way to one panel that asks the person to sign in. */
function SignInPanel() {
  const t = useText()
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{
        padding: spacing.xl,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      <Typography.Text data-testid="sites-sign-in">{t('shell.home.sites.signIn')}</Typography.Text>
      <AxisButton role="finish" onClick={startSignIn}>
        {t('shell.account.signIn')}
      </AxisButton>
    </Flex>
  )
}

/** Proposed: the sites, ordered by their application, each tile naming its application under the site's title. */
function SiteTiles({ sites, locale }: { sites: SiteListItem[]; locale: string }) {
  const application = (site: SiteListItem) =>
    site.application ? localizedTitle(site.application.titles, locale, site.application.name) : ''
  const ordered = [...sites].sort((a, b) => application(a).localeCompare(application(b), locale))
  return (
    <TileGrid>
      {ordered.map((site) => {
        const title = localizedTitle(site.titles, locale, site.path)
        return (
          <LaunchTile
            key={site.path}
            to={`/${site.path}`}
            mark={<TitleMark title={title} size={32} />}
            title={title}
            description={application(site) || undefined}
          />
        )
      })}
    </TileGrid>
  )
}

function SitesSection() {
  const t = useText()
  const { locale } = usePlatform().current
  const { sites, failed } = useSites()

  if (sites === null) {
    return <SignInPanel />
  }
  return (
    <Section id="home-sites" title={t('shell.home.sites.title')} count={sites?.length}>
      {failed ? (
        <Alert data-testid="sites-error" type="error" title={t('shell.home.sites.loadFailed')} />
      ) : sites === undefined ? (
        <Spin />
      ) : sites.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.sites.empty')} />
      ) : (
        <SiteTiles sites={sites} locale={locale} />
      )}
    </Section>
  )
}

/** Proposed: the tools of the platform, each only for the users who may open it: Studio for authors, the console for operators. */
function ToolsSection({ user }: { user: CurrentUser }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const icon = (glyph: ReactNode) => (
    <Avatar aria-hidden shape="square" size={32} icon={glyph} style={{ flex: 'none', background: colour.primary }} />
  )
  return (
    <Section id="home-tools" title={t('shell.home.tools.title')}>
      <Flex vertical gap={spacing.md}>
        {user.studio && (
          <LaunchTile
            to="/studio"
            mark={icon(<ToolOutlined />)}
            title={t('shell.studio.link')}
            description={t('shell.home.studio.description')}
          />
        )}
        {user.console && (
          <LaunchTile
            to="/console"
            mark={icon(<DashboardOutlined />)}
            title={t('shell.console.link')}
            description={t('shell.home.console.description')}
          />
        )}
      </Flex>
    </Section>
  )
}

/**
 * Proposed: the platform home page launches what the signed-in user may open: their sites, grouped by application,
 * beside the tools of their roles. With nobody signed in it asks them to sign in.
 */
export function HomePage() {
  const t = useText()
  const user = useCurrentUser()
  const narrow = useNarrowWidth()
  const { spacing } = useDesignTokens()
  const hasTools = Boolean(user?.studio || user?.console)
  const [searchParams] = useSearchParams()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <PageLayout title={t('shell.home.title')}>
      <Typography.Text type="secondary">{t('shell.home.subtitle')}</Typography.Text>
      {signInFailed && (
        <Alert data-testid="sign-in-failed" role="alert" type="warning" showIcon title={t('shell.home.signInFailed')} />
      )}
      {/* Proposed: the sites fill the wider column and the tools the narrower one, stacked at narrow width. */}
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: narrow || !hasTools ? 'minmax(0, 1fr)' : 'minmax(0, 2fr) minmax(0, 1fr)',
          gap: spacing.lg,
          alignItems: 'start',
        }}
      >
        <SitesSection />
        {user && hasTools && <ToolsSection user={user} />}
      </div>
    </PageLayout>
  )
}
