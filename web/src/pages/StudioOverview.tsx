import { Flex, Spin, Typography } from 'antd'
import { type ReactNode, useContext, useEffect, useId, useState } from 'react'
import { Link } from 'react-router'
import { fetchSites, type SiteListItem } from '../platform/site'
import { changeRows, type StudioReleases, type StudioResourceItem } from '../platform/studio'
import { kindName, studioKinds } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { HomeTileStyle } from './HomeTileStyle'
import { SiteTiles } from './SiteTiles'
import { StudioChangeList } from './StudioChangeList'
import { StudioPageHeading } from './StudioPageHeading'

/** The kinds that Get started leads to, which More you can add leaves out while Get started shows. */
const getStartedKinds = ['entity', 'page', 'site']

/**
 * The Overview of an open application: Get started while it has no entity, Live with its live sites, the changes since
 * the live release, then More you can add for each kind it lacks. Live and the changes wait for the release history,
 * and show nothing while it loads or when it fails.
 */
export function StudioOverview({
  application,
  title,
  description,
  resources,
  moduleKinds,
  history,
}: {
  application: string
  title: string
  description: string | null
  resources: StudioResourceItem[]
  moduleKinds: ReadonlySet<string>
  history: StudioReleases | 'failed' | undefined
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  const loaded = history === undefined || history === 'failed' ? undefined : history
  const releaseId = loaded?.active?.releaseId ?? null
  const present = new Set([...resources.map((item) => item.kind), ...moduleKinds])
  const getStarted = !present.has('entity')
  const missing = studioKinds.filter(
    (entry) =>
      entry.group !== 'other' && !present.has(entry.kind) && !(getStarted && getStartedKinds.includes(entry.kind)),
  )
  const address = (extra: Record<string, string>) => `/studio?${new URLSearchParams({ app: application, ...extra })}`

  return (
    <Flex vertical gap={spacing.lg}>
      <HomeTileStyle />
      <StudioPageHeading title={title} line={description} />
      {getStarted && (
        <OverviewBlock title={t('shell.studio.overview.getStarted')}>
          <ol style={{ margin: 0, paddingInlineStart: spacing.lg }}>
            <li>
              <Link to={address({ kind: 'entity' })}>{t('shell.studio.overview.start.entity')}</Link>
            </li>
            <li>
              <Link to={address({ kind: 'page' })}>{t('shell.studio.overview.start.page')}</Link>
            </li>
            <li>
              <Link to={address({ kind: 'site' })}>{t('shell.studio.overview.start.site')}</Link>
            </li>
            <li>
              <Link to={address({ view: 'releases' })}>{t('shell.studio.overview.start.publish')}</Link>
            </li>
          </ol>
        </OverviewBlock>
      )}
      {releaseId !== null && (
        <OverviewBlock title={t('shell.studio.overview.live')}>
          <LiveSites application={application} locale={locale} releaseId={releaseId} />
        </OverviewBlock>
      )}
      {loaded !== undefined && (
        <OverviewBlock title={t('shell.studio.overview.changes')}>
          {loaded.changes === null ? (
            <Typography.Text>{t('shell.studio.overview.changes.notPublished')}</Typography.Text>
          ) : loaded.changes.length === 0 ? (
            <Typography.Text>{t('shell.studio.overview.changes.allLive')}</Typography.Text>
          ) : (
            <StudioChangeList rows={changeRows(loaded.changes, resources, locale, t)} application={application} />
          )}
          <Link to={address({ view: 'releases' })}>{t('shell.studio.overview.changes.releases')}</Link>
        </OverviewBlock>
      )}
      {missing.length > 0 && (
        <OverviewBlock title={t('shell.studio.overview.more')}>
          {missing.map((entry) => (
            <Flex key={entry.kind} vertical>
              <Link to={address({ kind: entry.kind })}>{kindName(entry.kind, t)}</Link>
              <Typography.Text>{t(`shell.studio.overview.more.${entry.kind}.purpose`)}</Typography.Text>
              <Typography.Text style={{ color: colour.textMuted }}>
                {t(`shell.studio.overview.more.${entry.kind}.when`)}
              </Typography.Text>
            </Flex>
          ))}
        </OverviewBlock>
      )}
    </Flex>
  )
}

function OverviewBlock({ title, children }: { title: string; children: ReactNode }) {
  const { spacing } = useDesignTokens()
  const headingId = useId()
  return (
    <section aria-labelledby={headingId}>
      <Flex vertical gap={spacing.sm}>
        <Typography.Title level={5} id={headingId} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {children}
      </Flex>
    </section>
  )
}

function LiveSites({ application, locale, releaseId }: { application: string; locale: string; releaseId: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const [sites, setSites] = useState<SiteListItem[] | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchSites(controller.signal).then(
      (answer) => setSites(answer ?? 'failed'),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the sites failed', error)
        setSites('failed')
      },
    )
    return () => controller.abort()
  }, [releaseId])

  if (sites === undefined) {
    return <Spin />
  }
  if (sites === 'failed') {
    return (
      <Typography.Text style={{ color: colour.warningText }}>{t('shell.studio.overview.live.failed')}</Typography.Text>
    )
  }
  const own = sites.filter((site) => site.application.name === application)
  if (own.length === 0) {
    return <Typography.Text>{t('shell.studio.overview.live.none')}</Typography.Text>
  }
  return <SiteTiles sites={own} locale={locale} newTab />
}
