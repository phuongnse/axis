import { DashboardOutlined, ToolOutlined } from '@ant-design/icons'
import { Alert, Avatar, Empty, Flex, Spin, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { AxisButton } from '../platform/AxisButton'
import type { CurrentUser } from '../platform/currentUser'
import { localizedTitle, useCurrentUser, useSites } from '../platform/launcher'
import { LaunchTile, TitleMark } from '../platform/LaunchTile'
import type { SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'
import { usePlatform } from './context'

/** Proposed: the home page lays tiles out in three columns of one width, one column at narrow width. */
function ThreeColumns({ children }: { children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const narrow = useNarrowWidth()
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: narrow ? 'minmax(0, 1fr)' : 'repeat(3, minmax(0, 1fr))',
        gap: spacing.md,
      }}
    >
      {children}
    </div>
  )
}

/** A panel on `surface` with a quiet border, the frame of every block of the home page. */
function Panel({ children, padding }: { children: ReactNode; padding: number }) {
  const { colour, radius } = useDesignTokens()
  return (
    <div
      style={{
        padding,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        minWidth: 0,
      }}
    >
      {children}
    </div>
  )
}

/**
 * Proposed: the tools of the platform, each only for the users who may open it: Studio for authors, the console for
 * operators. They sit in the welcome band, as they work across every application.
 */
function Tools({ user }: { user: CurrentUser }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const icon = (glyph: ReactNode) => (
    <Avatar aria-hidden shape="square" size={32} icon={glyph} style={{ flex: 'none', background: colour.primary }} />
  )
  return (
    // Proposed: the tools sit under the welcome, in the first columns of the page's three.
    <nav aria-label={t('shell.home.tools.title')}>
      <ThreeColumns>
        {user.studio && (
          <LaunchTile
            to="/studio"
            mark={icon(<ToolOutlined />)}
            title={t('shell.studio.link')}
            description={t('shell.home.studio.description')}
            alignTop
          />
        )}
        {user.console && (
          <LaunchTile
            to="/console"
            mark={icon(<DashboardOutlined />)}
            title={t('shell.console.link')}
            description={t('shell.home.console.description')}
            alignTop
          />
        )}
      </ThreeColumns>
    </nav>
  )
}

/**
 * Proposed: the welcome band, the platform level of the page. It greets the user, with the tools of their roles
 * under the greeting, or the Sign in button when nobody is signed in.
 */
function WelcomeBand({ user }: { user: CurrentUser | null | undefined }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const hasTools = Boolean(user?.studio || user?.console)
  return (
    <Panel padding={spacing.lg}>
      <Flex vertical gap={spacing.lg}>
        <Flex vertical gap={spacing.xs} align="flex-start">
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t('shell.home.title')}
          </Typography.Title>
          {user === null ? (
            <>
              <Typography.Text type="secondary" data-testid="sites-sign-in">
                {t('shell.home.sites.signIn')}
              </Typography.Text>
              <AxisButton role="finish" onClick={startSignIn} style={{ marginTop: spacing.xs }}>
                {t('shell.account.signIn')}
              </AxisButton>
            </>
          ) : (
            <Typography.Text type="secondary">{t('shell.home.subtitle')}</Typography.Text>
          )}
        </Flex>
        {user && hasTools && <Tools user={user} />}
      </Flex>
    </Panel>
  )
}

/** Proposed: one site of an application, a row with its mark and title that opens the site, with no icon after it. */
function SiteRow({ site, locale }: { site: SiteListItem; locale: string }) {
  const { colour, spacing, radius } = useDesignTokens()
  const title = localizedTitle(site.titles, locale, site.path)
  return (
    <Link
      to={`/${site.path}`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: spacing.sm,
        padding: spacing.xs,
        borderRadius: radius.base,
        justifySelf: 'start',
        maxWidth: '100%',
        color: colour.text,
      }}
    >
      <TitleMark title={title} size={24} />
      <Typography.Text ellipsis>{title}</Typography.Text>
    </Link>
  )
}

interface ApplicationGroup {
  key: string
  title: string
  sites: SiteListItem[]
}

/** The sites under the application they belong to, the applications in title order. */
function byApplication(sites: SiteListItem[], locale: string, fallbackTitle: string): ApplicationGroup[] {
  const groups = new Map<string, ApplicationGroup>()
  for (const site of sites) {
    const key = site.application?.name ?? ''
    const title = site.application
      ? localizedTitle(site.application.titles, locale, site.application.name)
      : fallbackTitle
    const group = groups.get(key) ?? { key, title, sites: [] }
    group.sites.push(site)
    groups.set(key, group)
  }
  return [...groups.values()].sort((a, b) => a.title.localeCompare(b.title, locale))
}

/**
 * Proposed: an application is a card across the page, the application level: its mark, its title and the number
 * of its sites, over its sites in three columns, the site level.
 */
function ApplicationCard({ group, locale }: { group: ApplicationGroup; locale: string }) {
  const { colour, spacing } = useDesignTokens()
  const id = `home-application-${group.key || 'other'}`
  return (
    <Panel padding={spacing.md}>
      <section aria-labelledby={id}>
        <Flex
          align="center"
          gap={spacing.sm}
          style={{ paddingBottom: spacing.sm, marginBottom: spacing.xs, borderBottom: `1px solid ${colour.divider}` }}
        >
          <TitleMark title={group.title} size={32} />
          <Typography.Title id={id} level={5} style={{ margin: 0, flex: 1 }} ellipsis>
            {group.title}
          </Typography.Title>
          <Typography.Text type="secondary">{group.sites.length}</Typography.Text>
        </Flex>
        <ThreeColumns>
          {group.sites.map((site) => (
            <SiteRow key={site.path} site={site} locale={locale} />
          ))}
        </ThreeColumns>
      </section>
    </Panel>
  )
}

function Applications() {
  const t = useText()
  const { spacing } = useDesignTokens()
  const { locale } = usePlatform().current
  const { sites, failed } = useSites()

  if (sites === null) {
    return null
  }
  const groups = sites ? byApplication(sites, locale, t('shell.home.sites.title')) : []
  return (
    <section aria-labelledby="home-applications">
      <Flex align="baseline" gap={spacing.xs} style={{ marginBottom: spacing.md }}>
        <Typography.Title id="home-applications" level={5} style={{ margin: 0 }}>
          {t('shell.home.applications.title')}
        </Typography.Title>
        {sites && <Typography.Text type="secondary">{groups.length}</Typography.Text>}
      </Flex>
      {failed ? (
        <Alert data-testid="sites-error" type="error" title={t('shell.home.sites.loadFailed')} />
      ) : sites === undefined ? (
        <Spin />
      ) : sites.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.sites.empty')} />
      ) : (
        // Proposed: each application takes the full width, one under another, so cards never differ in width.
        <Flex vertical gap={spacing.md}>
          {groups.map((group) => (
            <ApplicationCard key={group.key} group={group} locale={locale} />
          ))}
        </Flex>
      )}
    </section>
  )
}

/**
 * Proposed: the platform home page, from the highest level to the lowest: a welcome band with the tools of the user's
 * roles, then the tenant's applications, each a card that lists its sites. With nobody signed in, the band asks the
 * person to sign in and the applications are not shown.
 */
export function HomePage() {
  const t = useText()
  const user = useCurrentUser()
  const { spacing } = useDesignTokens()
  const [searchParams] = useSearchParams()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <Flex vertical gap={spacing.lg}>
      {signInFailed && (
        <Alert data-testid="sign-in-failed" role="alert" type="warning" showIcon title={t('shell.home.signInFailed')} />
      )}
      <WelcomeBand user={user} />
      <Applications />
    </Flex>
  )
}
