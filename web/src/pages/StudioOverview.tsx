import { Flex, Tag, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { formatValue } from '../platform/formatValue'
import {
  folderDiffers,
  releaseRows,
  type StudioDiagnostic,
  type StudioReleases,
  type StudioResourceItem,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { StudioView } from './studioViews'

/**
 * Proposed: the first view of an open application, which says what to do next rather than what the application
 * holds. An application with no entity yet starts with the steps that build one, each a link to where it is done.
 * Then what needs attention: the folder's errors first, as they stop Publish, then its warnings, each opening the
 * resource at the place it names. Then the live release, and whether the folder has changes it does not have yet.
 * It shows no counts: the sidebar already lists the resources.
 */
export function StudioOverview({
  locale,
  description,
  resources,
  diagnostics,
  releases,
  addressOf,
}: {
  locale: string
  description: string | undefined
  resources: readonly StudioResourceItem[]
  diagnostics: readonly StudioDiagnostic[]
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
      {description && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
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
      {(!starting || diagnostics.length > 0) && (
        <Attention
          diagnostics={diagnostics}
          addressOf={(diagnostic) =>
            diagnostic.resourceId === null
              ? null
              : addressOf({ resource: diagnostic.resourceId, path: diagnostic.path })
          }
        />
      )}
      {(!starting || published) && releases !== undefined && (
        <Release releases={releases} locale={locale} history={addressOf({ name: 'releases' })} />
      )}
    </Flex>
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

/** The folder's diagnostics, errors first, with one line that says whether they stop Publish. */
function Attention({
  diagnostics,
  addressOf,
}: {
  diagnostics: readonly StudioDiagnostic[]
  addressOf: (diagnostic: StudioDiagnostic) => string | null
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'error')
  const ordered = [...errors, ...diagnostics.filter((diagnostic) => diagnostic.severity !== 'error')]
  let summary = 'shell.studio.overview.attention.none'
  if (errors.length > 0) {
    summary = 'shell.studio.overview.attention.errors'
  } else if (diagnostics.length > 0) {
    summary = 'shell.studio.overview.attention.warnings'
  }
  return (
    <Panel title={t('shell.studio.overview.attention.title')} testId="studio-diagnostics">
      <Typography.Text style={{ color: errors.length > 0 ? colour.dangerText : colour.textMuted }}>
        {t(summary)}
      </Typography.Text>
      {ordered.length > 0 && (
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {ordered.map((diagnostic, index) => (
            <li
              key={index}
              style={{
                paddingBlock: spacing.xs,
                borderTop: index > 0 ? `1px solid ${colour.divider}` : undefined,
              }}
            >
              <DiagnosticRow diagnostic={diagnostic} to={addressOf(diagnostic)} />
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

/** The live release: its number and label, who published it and when, and whether the folder has changes it lacks. */
function Release({
  releases,
  locale,
  history,
}: {
  releases: StudioReleases | 'failed'
  locale: string
  history: string
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  let body: ReactNode
  if (releases === 'failed') {
    body = <Typography.Text type="danger">{t('shell.studio.releases.loadFailed')}</Typography.Text>
  } else {
    const live = releaseRows(releases).find((row) => row.active)
    if (!live) {
      body = (
        <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.overview.release.none')}</Typography.Text>
      )
    } else {
      const time = formatValue({ name: 'activatedAt', type: 'date-time' }, live.activatedAt, {}, { locale, text: t })
      const changed = folderDiffers(releases)
      body = (
        <>
          <Flex vertical>
            <Typography.Text strong>
              {t('shell.studio.overview.release.number').replace('{number}', String(live.number))}
              {live.label && ` · ${live.label}`}
            </Typography.Text>
            <Typography.Text style={{ color: colour.textMuted }}>
              {t('shell.studio.overview.release.published')
                .replace('{time}', time)
                .replace('{actor}', live.actorName ?? live.actor)}
            </Typography.Text>
          </Flex>
          <Typography.Text
            data-testid="studio-overview-changes"
            style={{ color: changed ? colour.warningText : colour.textMuted }}
          >
            {t(changed ? 'shell.studio.overview.release.changes' : 'shell.studio.overview.release.upToDate')}
          </Typography.Text>
          <Link to={history} style={{ color: colour.primaryText, alignSelf: 'flex-start' }}>
            {t('shell.studio.overview.release.history')}
          </Link>
        </>
      )
    }
  }
  return (
    <Panel title={t('shell.studio.overview.release.title')} testId="studio-overview-release">
      {body}
    </Panel>
  )
}

/** One diagnostic: its severity and message, then its code and where it is, a link to that place when it is in a resource. */
function DiagnosticRow({ diagnostic, to }: { diagnostic: StudioDiagnostic; to: string | null }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const where = `${diagnostic.file}${diagnostic.path !== '' ? ` · ${t('shell.studio.path')}: ${diagnostic.path}` : ''}`
  return (
    <Flex gap={spacing.xs} align="baseline">
      <Tag color={diagnostic.severity === 'error' ? 'error' : 'warning'} style={{ marginInlineEnd: 0 }}>
        {diagnostic.severity}
      </Tag>
      <Flex vertical style={{ minWidth: 0 }}>
        <Typography.Text>{diagnostic.message}</Typography.Text>
        <span style={{ color: colour.textMuted }}>
          {diagnostic.code} ·{' '}
          {to ? (
            <Link to={to} style={{ color: colour.primaryText }}>
              {where}
            </Link>
          ) : (
            where
          )}
        </span>
      </Flex>
    </Flex>
  )
}
