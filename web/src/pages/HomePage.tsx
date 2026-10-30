import { Alert, Button, Card, Empty, Flex, Spin, Typography } from 'antd'
import { createElement, useEffect, useId, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { fetchCurrentUser, type CurrentUser } from '../platform/currentUser'
import { colourTokens } from '../platform/designTokens'
import { navigationIcon } from '../platform/navigationIcons'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { SiteMark } from '../platform/SiteMark'
import {
  applicationDescription,
  applicationTitle,
  fetchSites,
  siteDescription,
  siteTitle,
  type SiteListItem,
} from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'
import { usePlatform } from './context'
import { siteAddress } from '../platform/siteLinks'

/** What the page loaded. `null` means that the call answered 401, and `'failed'` that it failed otherwise. */
interface Loaded {
  user: CurrentUser | null | 'failed'
  sites: SiteListItem[] | null | 'failed'
}

function useHomeGrid(): CSSProperties {
  const narrow = useNarrowWidth()
  const { spacing } = useDesignTokens()
  return {
    display: 'grid',
    gridTemplateColumns: narrow ? '1fr' : 'repeat(3, minmax(0, 1fr))',
    gap: spacing.md,
  }
}

/**
 * Proposed: the sign-in screen draws the identity provider's frame, the organization's mark and name above the card.
 * Nobody is signed in yet, so the organization's name needs an answer that does not need a session; until it has one,
 * the screen names Axis.
 *
 * Proposed: the home page sends a signed-out person straight to the identity provider, so this card shows only where
 * that would loop or surprise: after a refused sign-in, `/?signIn=failed`, and after signing out, `/?signedOut`.
 */
export function SignInCard({ organization = 'Axis' }: { organization?: string }) {
  const t = useText()
  const [searchParams] = useSearchParams()
  const { colour, spacing, radius } = useDesignTokens()
  const signedOut = searchParams.has('signedOut')

  return (
    <Flex vertical align="center" gap={spacing.md} style={{ paddingBlock: spacing.xl }}>
      <OrganizationHeading name={organization} />
      <Card
        data-testid="sign-in-card"
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
            {t(signedOut ? 'shell.home.signedOut.title' : 'shell.home.signIn.title')}
          </Typography.Title>
          {signedOut ? (
            <Typography.Paragraph style={{ margin: 0, color: colour.textMuted }}>
              {t('shell.home.signedOut.message')}
            </Typography.Paragraph>
          ) : (
            <Alert
              data-testid="sign-in-failed"
              role="alert"
              type="warning"
              showIcon
              title={t('shell.home.signInFailed')}
            />
          )}
          <Button type="primary" block onClick={startSignIn}>
            {t(signedOut ? 'shell.account.sessions.signInAgain' : 'shell.account.signIn')}
          </Button>
        </Flex>
      </Card>
    </Flex>
  )
}

/**
 * Proposed: one tile of the home page, for a tool or an application alike: its mark, its title and one line that says
 * what it is for. With `to` the whole tile is the link; without it, `children` hold the links, such as an application's
 * sites when it has more than one.
 */
function HomeTile({
  to,
  mark,
  title,
  description,
  children,
}: {
  to?: string
  mark: ReactNode
  title: string
  description?: string
  children?: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  // Proposed: a tile that is a link shows it by its title in `primaryText`, as every link, and changes nothing on hover.
  // Keyboard focus draws the `focusRing` outline.
  const [focused, setFocused] = useState(false)
  const style: CSSProperties = {
    display: 'flex',
    alignItems: 'flex-start',
    gap: spacing.sm,
    minWidth: 0,
    padding: spacing.md,
    background: colour.surface,
    border: `1px solid ${colour.borderSubtle}`,
    borderRadius: radius.base,
    outline: focused ? `2px solid ${colour.focusRing}` : 'none',
    outlineOffset: 2,
  }
  const body = (
    <>
      {mark}
      <Flex vertical style={{ flex: 1, minWidth: 0 }}>
        {/* A title without a line under it is centred on the 32 px mark. */}
        <Typography.Text
          strong
          style={{ color: to ? colour.primaryText : undefined, lineHeight: description ? undefined : '32px' }}
        >
          {title}
        </Typography.Text>
        {description && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
        {children}
      </Flex>
    </>
  )
  return to ? (
    <Link
      to={to}
      style={style}
      onFocus={(event) => setFocused(event.currentTarget.matches(':focus-visible'))}
      onBlur={() => setFocused(false)}
    >
      {body}
    </Link>
  ) : (
    <div style={style}>{body}</div>
  )
}

function ToolMark({ icon }: { icon: string }) {
  const { colour, radius } = useDesignTokens()
  return (
    <span
      data-testid="tool-icon"
      style={{
        display: 'grid',
        placeItems: 'center',
        flex: 'none',
        width: 32,
        height: 32,
        background: colour.primary,
        color: colourTokens.surface.light,
        borderRadius: radius.base,
      }}
    >
      {createElement(navigationIcon(icon), { 'aria-hidden': true, style: { fontSize: 20 } })}
    </span>
  )
}

/** Proposed: a titled part of the home page, Applications or Tools, with no count beside its title. */
function HomeSection({ title, children }: { title: string; children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const headingId = useId()
  return (
    <section aria-labelledby={headingId}>
      <Flex vertical gap={spacing.md}>
        <Typography.Title level={5} id={headingId} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {children}
      </Flex>
    </section>
  )
}

/** Proposed: the tools of the person's roles, under the applications on the home page, as tiles like theirs. */
function Tools({ user }: { user: CurrentUser }) {
  const t = useText()
  const grid = useHomeGrid()
  if (!user.studio && !user.console && !user.users) {
    return null
  }
  return (
    <HomeSection title={t('shell.appSwitcher.tools')}>
      <div data-testid="home-grid" style={grid}>
        {user.studio && (
          <HomeTile
            to="/studio"
            mark={<ToolMark icon="tool" />}
            title={t('shell.appSwitcher.studio')}
            description={t('shell.home.studio.description')}
          />
        )}
        {user.console && (
          <HomeTile
            to="/console"
            mark={<ToolMark icon="dashboard" />}
            title={t('shell.appSwitcher.console')}
            description={t('shell.home.console.description')}
          />
        )}
        {user.users && (
          <HomeTile
            to="/users"
            mark={<ToolMark icon="people" />}
            title={t('shell.appSwitcher.users')}
            description={t('shell.home.users.description')}
          />
        )}
      </div>
    </HomeSection>
  )
}

interface ApplicationGroup {
  name: string
  title: string
  description?: string
  sites: SiteListItem[]
}

/**
 * Proposed: an application is one tile across the page with its mark, its title and what it is for as a whole, over its
 * sites in the three-column grid. Each site is a link with its own mark, its title and what that site does, as an
 * application often has several sites for different people. Both descriptions are optional.
 */
function ApplicationTile({ group, locale }: { group: ApplicationGroup; locale: string }) {
  const { colour, spacing } = useDesignTokens()
  const grid = useHomeGrid()
  const sorted = [...group.sites].sort((a, b) => siteTitle(a, locale).localeCompare(siteTitle(b, locale), locale))
  return (
    <HomeTile mark={<SiteMark title={group.title} size={32} />} title={group.title} description={group.description}>
      <div
        data-testid="application-sites"
        style={{
          ...grid,
          rowGap: spacing.sm,
          marginTop: spacing.sm,
          paddingTop: spacing.sm,
          borderTop: `1px solid ${colour.divider}`,
        }}
      >
        {sorted.map((site) => {
          const description = siteDescription(site, locale)
          return (
            <Link
              key={site.path}
              to={siteAddress(site.path)}
              style={{ display: 'flex', alignItems: 'flex-start', gap: spacing.xs, minWidth: 0 }}
            >
              <SiteMark title={siteTitle(site, locale)} size={20} />
              <Flex vertical style={{ minWidth: 0 }}>
                <Typography.Text style={{ color: colour.primaryText, lineHeight: '20px' }}>
                  {siteTitle(site, locale)}
                </Typography.Text>
                {description && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
              </Flex>
            </Link>
          )
        })}
      </div>
    </HomeTile>
  )
}

function Applications({ sites, locale }: { sites: SiteListItem[] | 'failed'; locale: string }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const groups = useMemo(() => {
    if (sites === 'failed') {
      return []
    }
    const byName = new Map<string, ApplicationGroup>()
    for (const site of sites) {
      const group = byName.get(site.application.name)
      if (group) {
        group.sites.push(site)
      } else {
        byName.set(site.application.name, {
          name: site.application.name,
          title: applicationTitle(site.application, locale),
          description: applicationDescription(site.application, locale),
          sites: [site],
        })
      }
    }
    return [...byName.values()].sort((a, b) => a.title.localeCompare(b.title, locale))
  }, [sites, locale])

  let body: ReactNode
  if (sites === 'failed') {
    body = <Alert data-testid="applications-error" type="error" title={t('shell.home.applications.loadFailed')} />
  } else if (groups.length === 0) {
    body = <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.applications.empty')} />
  } else {
    // Proposed: each application takes the whole row, and its sites share the three-column grid inside it.
    body = (
      <Flex vertical gap={spacing.md}>
        {groups.map((group) => (
          <ApplicationTile key={group.name} group={group} locale={locale} />
        ))}
      </Flex>
    )
  }

  return (
    <HomeSection title={t('shell.home.applications.title')}>
      {body}
    </HomeSection>
  )
}

export function HomePage() {
  const { locale } = usePlatform().current
  const { spacing } = useDesignTokens()
  const [loaded, setLoaded] = useState<Loaded>()
  const [searchParams] = useSearchParams()
  // Proposed: only a refused sign-in and a finished sign-out stop on the card; otherwise sign-in starts at once.
  const stopOnCard = searchParams.get('signIn') === 'failed' || searchParams.has('signedOut')

  useEffect(() => {
    const controller = new AbortController()
    Promise.allSettled([fetchCurrentUser(controller.signal), fetchSites(controller.signal)]).then(([user, sites]) => {
      if (controller.signal.aborted) {
        return
      }
      if (user.status === 'rejected') {
        console.warn('Loading the current user failed', user.reason)
      }
      if (sites.status === 'rejected') {
        console.warn('Loading the sites failed', sites.reason)
      }
      setLoaded({
        user: user.status === 'fulfilled' ? user.value : 'failed',
        sites: sites.status === 'fulfilled' ? sites.value : 'failed',
      })
    })
    return () => controller.abort()
  }, [])

  // Either call answering 401 means the session is gone, even if the other call went through before it ended.
  const signedOut = loaded !== undefined && (loaded.user === null || loaded.sites === null)
  useEffect(() => {
    if (signedOut && !stopOnCard) {
      startSignIn()
    }
  }, [signedOut, stopOnCard])

  if (loaded === undefined || (signedOut && !stopOnCard)) {
    return <Spin />
  }
  if (loaded.user === null || loaded.sites === null) {
    return <SignInCard />
  }

  return (
    <Flex vertical gap={spacing.lg}>
      {/* Proposed: no welcome band; the applications come first, as people use them most, then the tools. */}
      <Applications sites={loaded.sites} locale={locale} />
      {loaded.user !== 'failed' && <Tools user={loaded.user} />}
    </Flex>
  )
}
