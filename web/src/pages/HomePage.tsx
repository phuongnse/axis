import { DashboardOutlined, ToolOutlined } from '@ant-design/icons'
import { Alert, Avatar, Badge, Empty, Flex, Spin, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { AxisButton } from '../platform/AxisButton'
import type { CurrentUser } from '../platform/currentUser'
import { localizedTitle, useCurrentUser, useSites } from '../platform/launcher'
import { LaunchTile, TitleMark } from '../platform/LaunchTile'
import { PageLayout } from '../platform/PageLayout'
import { useServerStatus, type ServerStatus } from '../platform/serverStatus'
import type { SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { usePlatform } from './context'

const statusBadge: Record<ServerStatus, 'processing' | 'success' | 'error'> = {
  checking: 'processing',
  ready: 'success',
  unavailable: 'error',
}

/** Proposed: the tiles of a section fill a grid of columns at least 240 px wide. */
function TileGrid({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: spacing.md }}>
      {children}
    </div>
  )
}

function Section({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section aria-labelledby={id}>
      <Typography.Title id={id} level={5} style={{ marginTop: 0 }}>
        {title}
      </Typography.Title>
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

/**
 * Proposed: the sites, grouped under the title of the application they belong to when the tenant has more than
 * one application, so a reader sees which sites one application offers.
 */
function SiteGroups({ sites, locale }: { sites: SiteListItem[]; locale: string }) {
  const { spacing } = useDesignTokens()
  const groups = new Map<string, { title: string; sites: SiteListItem[] }>()
  for (const site of sites) {
    const key = site.application?.name ?? ''
    const title = site.application ? localizedTitle(site.application.titles, locale, site.application.name) : ''
    const group = groups.get(key) ?? { title, sites: [] }
    group.sites.push(site)
    groups.set(key, group)
  }
  const tiles = (list: SiteListItem[]) => (
    <TileGrid>
      {list.map((site) => {
        const title = localizedTitle(site.titles, locale, site.path)
        return (
          <LaunchTile key={site.path} to={`/${site.path}`} mark={<TitleMark title={title} size={32} />} title={title} />
        )
      })}
    </TileGrid>
  )
  if (groups.size < 2) {
    return tiles(sites)
  }
  return (
    <Flex vertical gap={spacing.md}>
      {[...groups.values()].map((group) => (
        <Flex key={group.title} vertical gap={spacing.xs}>
          <Typography.Text type="secondary">{group.title}</Typography.Text>
          {tiles(group.sites)}
        </Flex>
      ))}
    </Flex>
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
    <Section id="home-sites" title={t('shell.home.sites.title')}>
      {failed ? (
        <Alert data-testid="sites-error" type="error" title={t('shell.home.sites.loadFailed')} />
      ) : sites === undefined ? (
        <Spin />
      ) : sites.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.sites.empty')} />
      ) : (
        <SiteGroups sites={sites} locale={locale} />
      )}
    </Section>
  )
}

/** Proposed: the tools of the platform, each only for the users who may open it: Studio for authors, the console for operators. */
function ToolsSection({ user }: { user: CurrentUser }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const icon = (glyph: ReactNode) => (
    <Avatar aria-hidden shape="square" size={32} icon={glyph} style={{ flex: 'none', background: colour.primary }} />
  )
  if (!user.studio && !user.console) {
    return null
  }
  return (
    <Section id="home-tools" title={t('shell.home.tools.title')}>
      <TileGrid>
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
      </TileGrid>
    </Section>
  )
}

/** Proposed: the server's readiness is a quiet line at the foot of the home page, not a card of its own. */
function StatusFooter() {
  const t = useText()
  const serverStatus = useServerStatus()
  const { colour, spacing } = useDesignTokens()
  return (
    <footer style={{ marginTop: spacing.lg, paddingTop: spacing.md, borderTop: `1px solid ${colour.divider}` }}>
      <Typography.Text type="secondary">
        {t('shell.serverStatus.title')}:{' '}
        <Badge
          data-testid="server-status"
          status={statusBadge[serverStatus]}
          text={<Typography.Text type="secondary">{t(`shell.serverStatus.${serverStatus}`)}</Typography.Text>}
        />
      </Typography.Text>
    </footer>
  )
}

/**
 * Proposed: the platform home page launches what the signed-in user may open: their sites, grouped by application,
 * then the tools of their roles. With nobody signed in it asks them to sign in.
 */
export function HomePage() {
  const t = useText()
  const user = useCurrentUser()
  const [searchParams] = useSearchParams()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <PageLayout title={t('shell.home.title')}>
      <Typography.Text type="secondary">{t('shell.home.subtitle')}</Typography.Text>
      {signInFailed && (
        <Alert data-testid="sign-in-failed" role="alert" type="warning" showIcon title={t('shell.home.signInFailed')} />
      )}
      <SitesSection />
      {user && <ToolsSection user={user} />}
      <StatusFooter />
    </PageLayout>
  )
}
