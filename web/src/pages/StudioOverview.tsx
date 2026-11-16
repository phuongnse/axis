import { Flex, Typography } from 'antd'
import { useContext, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { EmptyState } from '../platform/EmptyState'
import { fetchSites, type SiteListItem } from '../platform/site'
import { changeRows, type StudioReleases, type StudioResourceItem } from '../platform/studio'
import { kindName, studioKinds } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { HomeTileStyle } from './HomeTileStyle'
import { ResourceFormSection, ResourceFormSections } from './ResourceFormSection'
import { SiteTiles } from './SiteTiles'
import { StudioChangeList } from './StudioChangeList'
import { StudioPageHeading } from './StudioPageHeading'

/** The kinds that Get started leads to, which More you can add leaves out while Get started shows. */
const getStartedKinds = ['entity', 'page', 'site']

/**
 * The Overview of an open application: Get started while it has no entity, Live with its live sites, the changes since
 * the live release, then More you can add for each kind it lacks. Live and the changes wait for the release history.
 * While it loads or when it fails, both say so. Once it has loaded, Live is left out when no release is live.
 * See docs/reference/studio-workspace.md#overview.
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
      <ResourceFormSections>
        {getStarted && (
          <ResourceFormSection testName="get-started" heading={{ title: t('shell.studio.overview.getStarted') }}>
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
          </ResourceFormSection>
        )}
        {(loaded === undefined || releaseId !== null) && (
          <ResourceFormSection testName="live" heading={{ title: t('shell.studio.overview.live') }}>
            {releaseId === null ? (
              <ReleasesState failed={history === 'failed'} />
            ) : (
              <LiveSites application={application} locale={locale} releaseId={releaseId} />
            )}
          </ResourceFormSection>
        )}
        <ResourceFormSection testName="changes" heading={{ title: t('shell.studio.overview.changes') }}>
          {loaded === undefined ? (
            <ReleasesState failed={history === 'failed'} />
          ) : (
            <Flex vertical gap={spacing.sm}>
              {loaded.changes === null ? (
                <Typography.Text>{t('shell.studio.overview.changes.notPublished')}</Typography.Text>
              ) : loaded.changes.length === 0 ? (
                <EmptyState text={t('shell.studio.overview.changes.allLive')} />
              ) : (
                <StudioChangeList rows={changeRows(loaded.changes, resources, locale, t)} application={application} />
              )}
              <Link to={address({ view: 'releases' })}>{t('shell.studio.overview.changes.releases')}</Link>
            </Flex>
          )}
        </ResourceFormSection>
        {missing.length > 0 && (
          <ResourceFormSection testName="more" heading={{ title: t('shell.studio.overview.more') }}>
            <Flex vertical gap={spacing.sm}>
              {missing.map((entry) => (
                <Flex key={entry.kind} vertical>
                  <Link to={address({ kind: entry.kind })}>{kindName(entry.kind, t)}</Link>
                  <Typography.Text>{t(`shell.studio.overview.more.${entry.kind}.purpose`)}</Typography.Text>
                  <Typography.Text style={{ color: colour.textMuted }}>
                    {t(`shell.studio.overview.more.${entry.kind}.when`)}
                  </Typography.Text>
                </Flex>
              ))}
            </Flex>
          </ResourceFormSection>
        )}
      </ResourceFormSections>
    </Flex>
  )
}

/** What Live and Changes say while the release history loads, or when it did not load. */
function ReleasesState({ failed }: { failed: boolean }) {
  const t = useText()
  const { colour } = useDesignTokens()
  return failed ? (
    <Typography.Text style={{ color: colour.warningText }}>{t('shell.studio.overview.releasesFailed')}</Typography.Text>
  ) : (
    <Typography.Text>{t('shell.studio.overview.releasesLoading')}</Typography.Text>
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
    return <Typography.Text>{t('shell.studio.overview.live.loading')}</Typography.Text>
  }
  if (sites === 'failed') {
    return (
      <Typography.Text style={{ color: colour.warningText }}>{t('shell.studio.overview.live.failed')}</Typography.Text>
    )
  }
  const own = sites.filter((site) => site.application.name === application)
  if (own.length === 0) {
    return <EmptyState text={t('shell.studio.overview.live.none')} />
  }
  return <SiteTiles sites={own} locale={locale} newTab />
}
