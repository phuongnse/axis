import { Flex, Typography } from 'antd'
import { useContext, useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import {
  fetchStudioResource,
  folderDiffers,
  releaseGroups,
  resourceTitle,
  type StudioReleases,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { starterKinds } from '../platform/studioStarters'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { studioKindGroups, type StudioView } from './studioViews'

/**
 * Proposed: the first view of an open application, which says what to do next rather than what the application
 * holds. An application with no entity yet starts with the steps that build one, each a link to where it is done.
 * Then what differs from the live release, file by file, each opening its resource, or that everything is live,
 * with links to open each live site and to see every release. Problems are not listed here: the band's problems panel
 * lists them on every page. Last, the kinds the application has none of yet, each with what it is for and when one is
 * needed.
 */
export function StudioOverview({
  application,
  locale,
  resources,
  releases,
  addressOf,
}: {
  application: string
  locale: string
  resources: readonly StudioResourceItem[]
  /** The release history, `failed` when it did not load, or undefined while it loads. */
  releases: StudioReleases | 'failed' | undefined
  /** The workspace address of a view, or of a resource, at a path inside it. */
  addressOf: (target: StudioView | { resource: string; path?: string }) => string
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const starting = !resources.some((item) => item.kind === 'entity')
  const published = releases !== undefined && releases !== 'failed' && releases.active !== null
  const site = resources.find((item) => item.kind === 'site')

  return (
    <Flex vertical gap={spacing.md} data-testid="studio-overview">
      {starting && (
        <Panel title={t('shell.studio.overview.start.title')} testId="studio-overview-start">
          <Typography.Text style={{ color: colour.textMuted }}>
            {t('shell.studio.overview.start.message')}
          </Typography.Text>
          <ol style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: spacing.sm }}>
            <Step number={1} to={addressOf({ name: 'kind', kind: 'entity' })} name="entity" />
            <Step number={2} to={addressOf({ name: 'kind', kind: 'page' })} name="page" />
            <Step
              number={3}
              to={site ? addressOf({ resource: site.id }) : addressOf({ name: 'kind', kind: 'site' })}
              name="site"
            />
            <Step number={4} name="try" />
          </ol>
        </Panel>
      )}
      {(!starting || published) && releases !== undefined && (
        <Release
          application={application}
          releases={releases}
          resources={resources}
          locale={locale}
          addressOf={addressOf}
        />
      )}
      <More resources={resources} skip={starting ? startKinds : []} addressOf={addressOf} />
    </Flex>
  )
}

/** The kinds that Get started already leads to. */
const startKinds = ['entity', 'page', 'site']

/** Kinds Studio writes itself, as the entity designer saves an entity's migration with it (D54), which are not offered. */
const generatedKinds = ['migration']

/**
 * Proposed: every kind the application has no resource of yet, by the sidebar's groups: the kind as a link to its
 * list, where New resource… starts with it, what it is for, and in textMuted when one is needed. A kind Studio writes
 * itself is left out.
 */
function More({
  resources,
  skip,
  addressOf,
}: {
  resources: readonly StudioResourceItem[]
  skip: readonly string[]
  addressOf: (target: StudioView) => string
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const present = new Set(resources.map((item) => item.kind))
  const groups = studioKindGroups
    .map((group) => ({
      key: group.key,
      kinds: group.kinds
        .map((entry) => entry.kind)
        .filter(
          (kind) =>
            starterKinds.includes(kind) && !present.has(kind) && !skip.includes(kind) && !generatedKinds.includes(kind),
        ),
    }))
    .filter((group) => group.kinds.length > 0)
  if (groups.length === 0) {
    return null
  }
  return (
    <Panel title={t('shell.studio.overview.more.title')} testId="studio-overview-more">
      <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.overview.more.message')}</Typography.Text>
      {groups.map((group) => (
        <Flex key={group.key} vertical gap={spacing.xs}>
          <Typography.Text strong>{t(`shell.studio.nav.${group.key}`)}</Typography.Text>
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: spacing.sm }}>
            {group.kinds.map((kind) => (
              <li key={kind} style={{ display: 'flex', flexDirection: 'column' }}>
                <span>
                  <Link to={addressOf({ name: 'kind', kind })} style={{ color: colour.primaryText }}>
                    {t(`shell.studio.kind.${kind}`)}
                  </Link>
                  <Typography.Text>
                    {' · '}
                    {t(`shell.studio.overview.kind.${kind}`)}
                  </Typography.Text>
                </span>
                <Typography.Text style={{ color: colour.textMuted }}>
                  {t(`shell.studio.overview.when.${kind}`)}
                </Typography.Text>
              </li>
            ))}
          </ul>
        </Flex>
      ))}
    </Panel>
  )
}

/** A block of the overview: a `heading5` title over its content, on `surface`. */
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

/** One step of building a new application: its number, its title as the link to where it is done, and one line. */
function Step({ number, to, name }: { number: number; to?: string; name: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const title = t(`shell.studio.overview.start.${name}.title`)
  return (
    <li style={{ display: 'flex', gap: spacing.xs }}>
      <Typography.Text style={{ color: colour.textMuted, minWidth: 16 }}>{number}.</Typography.Text>
      <Flex vertical>
        {to ? (
          <Link to={to} style={{ color: colour.primaryText, fontWeight: 500 }}>
            {title}
          </Link>
        ) : (
          <Typography.Text style={{ fontWeight: 500 }}>{title}</Typography.Text>
        )}
        <Typography.Text style={{ color: colour.textMuted }}>
          {t(`shell.studio.overview.start.${name}.text`)}
        </Typography.Text>
      </Flex>
    </li>
  )
}

/**
 * Proposed: what differs from the live release, which is what a publish would change: one row per file that
 * differs, its resource's label as the link that opens it, its kind and whether it was added, changed or removed, or
 * one line that everything is live. Then the sites, each its address as a link that opens it in a new tab, leaving
 * out a site that is not live yet, and a link to every release.
 */
function Release({
  application,
  releases,
  resources,
  locale,
  addressOf,
}: {
  application: string
  releases: StudioReleases | 'failed'
  resources: readonly StudioResourceItem[]
  locale: string
  addressOf: (target: StudioView | { resource: string }) => string
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const sites = useSitePaths(application, resources)
  let body: ReactNode
  if (releases === 'failed') {
    body = <Typography.Text type="danger">{t('shell.studio.releases.loadFailed')}</Typography.Text>
  } else {
    // Proposed: no line names the live release or says that changes are not live: the band says both, beside
    // Publish…, and the Releases list says who published it and when.
    const live = releaseGroups(releases).find((release) => release.live)
    if (!live) {
      body = (
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.overview.release.none')}</Typography.Text>
      )
    } else {
      const changes = releases.changes ?? []
      const added = new Set(changes.filter((change) => change.change === 'added').map((change) => change.path))
      const liveSites = sites.filter((site) => !added.has(site.file))
      body = (
        <>
          {folderDiffers(releases) ? (
            <ul
              data-testid="studio-overview-changes"
              style={{ margin: 0, padding: 0, listStyle: 'none', display: 'grid', gap: spacing.xs }}
            >
              {changes.map((change) => {
                const item = resources.find((resource) => resource.file === change.path)
                return (
                  <li key={change.path} style={{ display: 'flex', gap: spacing.xs, alignItems: 'baseline' }}>
                    {item ? (
                      <Link to={addressOf({ resource: item.id })} style={{ color: colour.primaryText }}>
                        {resourceTitle(item, locale)}
                      </Link>
                    ) : (
                      <Typography.Text>{change.path}</Typography.Text>
                    )}
                    <Typography.Text style={{ color: colour.textMuted }}>
                      {item ? `${kindName(item.kind, t)} · ` : ''}
                      {t(`shell.studio.overview.change.${change.change}`)}
                    </Typography.Text>
                  </li>
                )
              })}
            </ul>
          ) : (
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.overview.release.allLive')}
            </Typography.Text>
          )}
          {liveSites.length > 0 && (
            // Proposed: each site is shown by its address, so it reads as a place on the web and not as the site's
            // resource in Studio, which the sidebar opens, and can be copied for the application's people. It opens
            // in a new tab, which its accessible name says. Here the address is this host and the site's
            // path; a site that a tenant serves at a host of its own shows that host, which the API has to give Studio.
            <Typography.Text data-testid="studio-overview-sites">
              <span style={{ color: colour.textMuted }}>{t('shell.studio.overview.sites')} </span>
              {liveSites.map((site, index) => {
                const address = `${window.location.host}/${site.path}`
                const opens = t('shell.studio.overview.site.opens').replace('{site}', site.title)
                return (
                  <span key={site.file}>
                    {index > 0 && ' · '}
                    <a
                      href={`/${site.path}`}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={`${address}, ${opens}`}
                      style={{ color: colour.primaryText }}
                    >
                      {address}
                    </a>
                  </span>
                )
              })}
            </Typography.Text>
          )}
          <Link to={addressOf({ name: 'releases' })} style={{ color: colour.primaryText, alignSelf: 'flex-start' }}>
            {t('shell.studio.overview.release.history')}
          </Link>
        </>
      )
    }
  }
  return (
    <Panel title={t('shell.studio.overview.changes.title')} testId="studio-overview-release">
      {body}
    </Panel>
  )
}

/** A kind's name for one resource, as the sidebar names its list. */
function kindName(kind: string, t: (key: string) => string): string {
  const key = `shell.studio.kind.${kind}`
  const name = t(key)
  return name === key ? kind : name
}

/** Proposed: the path and title of each site of the application, read from its resource, to open it where it runs. */
function useSitePaths(application: string, resources: readonly StudioResourceItem[]) {
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const siteItems = resources.filter((item) => item.kind === 'site')
  const key = siteItems.map((item) => `${item.id}@${item.version}`).join(',')
  const [paths, setPaths] = useState<Record<string, string>>({})
  useEffect(() => {
    const controller = new AbortController()
    Promise.all(
      siteItems.map((item) =>
        fetchStudioResource(application, item.id, controller.signal).then(
          (resource) => [item.id, typeof resource.content.path === 'string' ? resource.content.path : null] as const,
        ),
      ),
    ).then(
      (pairs) =>
        setPaths(Object.fromEntries(pairs.filter((pair): pair is readonly [string, string] => pair[1] !== null))),
      () => setPaths({}),
    )
    return () => controller.abort()
    // The key holds the sites' ids and versions, so a site saved again is read again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [application, key])
  return siteItems
    .filter((item) => paths[item.id])
    .map((item) => ({ file: item.file, path: paths[item.id], title: resourceTitle(item, locale) }))
}
