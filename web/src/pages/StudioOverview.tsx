import { Flex, Spin, Typography } from 'antd'
import { useContext, useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { fetchSites, type SiteListItem } from '../platform/site'
import { changeRows, type StudioReleases, type StudioResourceItem } from '../platform/studio'
import { kindGroupNames, kindName, kindSingular, studioKinds } from '../platform/studioKinds'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { HomeTileStyle } from './HomeTileStyle'
import { SiteTiles } from './SiteTiles'
import { StudioChangeList } from './StudioChangeList'
import type { StudioView } from './studioViews'

/** The kinds that Get started leads to, which More you can add leaves out while Get started shows. */
const getStartedKinds = ['entity', 'page', 'site']

/**
 * The Overview of an open application: Get started while it has no entity, Live with its live sites, the changes since
 * the live release, then More you can add for each kind it lacks, a module's kinds counting as present. Live and the
 * changes wait for the release history, and show nothing while it loads or when it fails. Proposed, the look: each block
 * is a panel on `surface`, Get started's steps are numbered, the changes are Studio's change list, and More you can add
 * groups its kinds as the sidebar does.
 */
export function StudioOverview({
  application,
  resources,
  moduleKinds,
  history,
  addressOf = mainAddress(application),
}: {
  application: string
  /** The application's title and description, which the workspace's page heading shows above the Overview. */
  title: string
  description: string | null
  resources: readonly StudioResourceItem[]
  moduleKinds: ReadonlySet<string>
  /** The release history, `failed` when it did not load, or undefined while it loads. */
  history: StudioReleases | 'failed' | undefined
  /** Proposed: the workspace address of a view, or of a resource, as the prototype's workspace builds them. */
  addressOf?: (target: StudioView | { resource: string; path?: string }) => string
}) {
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const releases = history
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const loaded = releases === undefined || releases === 'failed' ? undefined : releases
  const releaseId = loaded?.active?.releaseId ?? null
  const present = new Set([...resources.map((item) => item.kind), ...moduleKinds])
  const getStarted = !present.has('entity')
  const missing = studioKinds.filter(
    (entry) =>
      entry.group !== 'other' && !present.has(entry.kind) && !(getStarted && getStartedKinds.includes(entry.kind)),
  )
  const steps: { name: string; to: string }[] = [
    { name: 'entity', to: addressOf({ name: 'kind', kind: 'entity' }) },
    { name: 'page', to: addressOf({ name: 'kind', kind: 'page' }) },
    { name: 'site', to: addressOf({ name: 'kind', kind: 'site' }) },
    { name: 'publish', to: addressOf({ name: 'releases' }) },
  ]

  return (
    <Flex vertical gap={spacing.md} data-testid="studio-overview">
      <HomeTileStyle />
      {getStarted && (
        <Panel title={t('shell.studio.overview.getStarted')} testId="studio-overview-start">
          <ol style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: spacing.sm }}>
            {steps.map((step, index) => (
              <li key={step.name} style={{ display: 'flex', gap: spacing.xs }}>
                <Typography.Text style={{ color: colour.textMuted, minWidth: 16 }}>{index + 1}.</Typography.Text>
                <Link to={step.to} style={{ color: colour.primaryText }}>
                  {t(`shell.studio.overview.start.${step.name}`)}
                </Link>
              </li>
            ))}
          </ol>
        </Panel>
      )}
      {releaseId !== null && (
        <Panel title={t('shell.studio.overview.live')} testId="studio-overview-live">
          <LiveSites application={application} locale={locale} releaseId={releaseId} />
        </Panel>
      )}
      {loaded !== undefined && (
        <Panel title={t('shell.studio.overview.changes')} testId="studio-overview-release">
          {loaded.changes === null ? (
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.overview.changes.notPublished')}
            </Typography.Text>
          ) : loaded.changes.length === 0 ? (
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.overview.changes.allLive')}
            </Typography.Text>
          ) : (
            <StudioChangeList
              testId="studio-overview-changes"
              rows={changeRows(loaded.changes, [...resources], locale, t).map((row, index) => ({
                key: row.key,
                name: row.name,
                kind: row.kind === null ? '' : kindSingular(row.kind, t),
                change: t(`shell.studio.overview.change.${row.change}`),
                to: row.resourceId === null ? undefined : addressOf({ resource: row.resourceId }),
                // The rows come in the order `changeRows` gives them.
                order: index,
              }))}
            />
          )}
          <Link to={addressOf({ name: 'releases' })} style={{ color: colour.primaryText, alignSelf: 'flex-start' }}>
            {t('shell.studio.overview.changes.releases')}
          </Link>
        </Panel>
      )}
      {missing.length > 0 && (
        <Panel title={t('shell.studio.overview.more')} testId="studio-overview-more">
          {kindGroupNames
            .map((group) => ({ group, kinds: missing.filter((entry) => entry.group === group) }))
            .filter(({ kinds }) => kinds.length > 0)
            .map(({ group, kinds }) => (
              <Flex key={group} vertical gap={spacing.xs}>
                <Typography.Text strong>{t(`shell.studio.nav.group.${group}`)}</Typography.Text>
                <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: spacing.sm }}>
                  {kinds.map((entry) => (
                    <li key={entry.kind} style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start' }}>
                      <Link to={addressOf({ name: 'kind', kind: entry.kind })} style={{ color: colour.primaryText }}>
                        {kindName(entry.kind, t)}
                      </Link>
                      <Typography.Text>{t(`shell.studio.overview.more.${entry.kind}.purpose`)}</Typography.Text>
                      <Typography.Text style={{ color: colour.textMuted }}>
                        {t(`shell.studio.overview.more.${entry.kind}.when`)}
                      </Typography.Text>
                    </li>
                  ))}
                </ul>
              </Flex>
            ))}
        </Panel>
      )}
    </Flex>
  )
}

/** The addresses `main`'s workspace reads: a kind's list by `kind`, the releases by `view`, a resource by `resource`. */
function mainAddress(application: string) {
  return (target: StudioView | { resource: string; path?: string }) => {
    const extra: Record<string, string> =
      'resource' in target
        ? { resource: target.resource }
        : target.name === 'kind'
          ? { kind: target.kind }
          : target.name === 'releases'
            ? { view: 'releases' }
            : {}
    return `/studio?${new URLSearchParams({ app: application, ...extra })}`
  }
}

/** Proposed: a block of the overview: a `heading5` title over its content, on `surface`. */
function Panel({ title, testId, children }: { title: string; testId: string; children: ReactNode }) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <section
      data-testid={testId}
      aria-label={title}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: spacing.sm,
        padding: spacing.md,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      <Typography.Title level={5} style={{ margin: 0 }}>
        {title}
      </Typography.Title>
      {children}
    </section>
  )
}

/** The application's live sites as the home page shows them, each opening in a new tab, so Studio stays open. */
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
    return <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.overview.live.none')}</Typography.Text>
  }
  return <SiteTiles sites={own} locale={locale} newTab />
}
