import { Descriptions, Flex, Spin, Typography } from 'antd'
import { type HTMLAttributes, type ReactNode, useContext, useEffect, useId, useState } from 'react'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import {
  changeRows,
  fetchStudioRelease,
  releasePublisher,
  ServerUnreachableError,
  type StudioRelaxedPoint,
  type StudioRelease,
  type StudioReleaseItem,
  type StudioReleasePart,
  type StudioReleases,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { StudioActivateDialog, StudioLiveTag, StudioReleaseBlockers } from './StudioReleases'
import { StudioChangeList } from './StudioChangeList'
import { StudioPageHeading } from './StudioPageHeading'

/** One activation of the release, as a row of the Activations table. */
interface ActivationRow {
  key: string
  when: string
  by: string
  replaced: number | null
  until: string | null
}

/** One row of the modules and packages table. */
interface PartRow {
  key: string
  name: string
  type: 'module' | 'package'
  version: string
}

/**
 * The page of one release, at `&view=release&release=N`: the heading "Release N" with the Live and Off-standard tags,
 * and Activate… on its right for a publisher (`canActivate`) when the release is not live, which asks for the same
 * confirmation as the release list and calls `onActivate` with the release's id. A release with blockers cannot replace
 * the live one: a sentence and each blocking module's line follow the heading, and Activate… is off. Under it, in order, come the facts
 * (Label, Status, Published, Published by and Note), the relaxed points in words grouped by module, the changes under
 * a title that names the release they compare with, where the modules and packages that differ follow the files, then
 * the modules and packages the release pins, and every activation of
 * the release. The facts and the activations come from `history`, and the rest from the release route, which loads
 * again when `history` does. A number that the history lacks shows one line and a link to every release, and so does a
 * failed load.
 */
export function StudioReleasePage({
  application,
  number,
  history,
  resources,
  canActivate,
  busy,
  onActivate,
}: {
  application: string
  number: string
  history: StudioReleases | 'failed' | undefined
  resources: StudioResourceItem[]
  canActivate: boolean
  busy: boolean
  onActivate: (releaseId: string) => void
}) {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{
    application: string
    number: string
    value: StudioRelease | null | 'failed'
  }>()
  const [confirming, setConfirming] = useState(false)
  const ready = history !== undefined && history !== 'failed'

  useEffect(() => {
    if (!ready) {
      return
    }
    const controller = new AbortController()
    fetchStudioRelease(application, number, controller.signal).then(
      (value) => setLoaded({ application, number, value }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading release '${number}' of '${application}' failed`, error)
        setLoaded({ application, number, value: 'failed' })
      },
    )
    return () => controller.abort()
  }, [application, number, generation, history, lost, ready])

  const all = (
    <Link to={`/studio?${new URLSearchParams({ app: application, view: 'releases' })}`}>
      {t('shell.studio.release.all')}
    </Link>
  )
  const line = (testId: string, key: string) => (
    <Flex vertical gap="small" data-testid={testId}>
      <Typography.Text>{t(key)}</Typography.Text>
      {all}
    </Flex>
  )

  if (history === 'failed') {
    return line('studio-release-failed', 'shell.studio.release.failed')
  }
  const current = loaded?.application === application && loaded.number === number ? loaded.value : undefined
  if (history === undefined || current === undefined) {
    return <Spin />
  }
  if (current === 'failed') {
    return line('studio-release-failed', 'shell.studio.release.failed')
  }
  const own = current === null ? [] : history.items.filter((item) => item.number === current.number)
  if (current === null || own.length === 0) {
    return line('studio-release-not-found', 'shell.studio.release.notFound')
  }
  return (
    <>
      <ReleaseContent
        application={application}
        release={current}
        own={own}
        history={history}
        resources={resources}
        canActivate={canActivate}
        busy={busy}
        onActivate={() => setConfirming(true)}
      />
      <StudioActivateDialog
        application={application}
        blockers={current.blockers}
        release={confirming ? current.number : null}
        liveNumber={history.active?.number}
        onConfirm={() => {
          setConfirming(false)
          onActivate(own[0].releaseId)
        }}
        onCancel={() => setConfirming(false)}
      />
    </>
  )
}

function ReleaseContent({
  application,
  release,
  own,
  history,
  resources,
  canActivate,
  busy,
  onActivate,
}: {
  application: string
  release: StudioRelease
  own: StudioReleaseItem[]
  history: StudioReleases
  resources: StudioResourceItem[]
  canActivate: boolean
  busy: boolean
  onActivate: () => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { spacing } = useDesignTokens()
  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  const format = (value: string) => time.format(new Date(value))
  const address = (extra: Record<string, string>) => `/studio?${new URLSearchParams({ app: application, ...extra })}`

  const sorted = [...history.items].sort((left, right) => Date.parse(left.activatedAt) - Date.parse(right.activatedAt))
  const ownIndexes = sorted.flatMap((item, index) => (item.number === release.number ? [index] : []))
  const first = own.reduce((known, item) =>
    Date.parse(item.activatedAt) < Date.parse(known.activatedAt) ? item : known,
  )
  const lastIndex = ownIndexes[ownIndexes.length - 1]
  const status = release.live
    ? t('shell.studio.release.liveSince').replace('{time}', format(sorted[lastIndex].activatedAt))
    : sorted[lastIndex + 1] !== undefined
      ? t('shell.studio.release.replacedOn').replace('{time}', format(sorted[lastIndex + 1].activatedAt))
      : null

  const activations: ActivationRow[] = [...ownIndexes].reverse().map((index) => {
    const item = sorted[index]
    const next = sorted[index + 1]
    return {
      key: `${index}`,
      when: format(item.activatedAt),
      by: releasePublisher(item, t),
      replaced: sorted[index - 1]?.number ?? null,
      until: next !== undefined ? format(next.activatedAt) : release.live ? t('shell.studio.release.now') : null,
    }
  })
  const activationColumns: AxisColumn<ActivationRow>[] = [
    { key: 'when', kind: 'fixed', title: t('shell.studio.release.when'), render: (_, row) => row.when },
    { key: 'by', kind: 'freeText', title: t('shell.studio.release.by'), render: (_, row) => row.by },
    { key: 'replaced', kind: 'fixed', title: t('shell.studio.release.replaced'), render: (_, row) => row.replaced },
    { key: 'until', kind: 'fixed', title: t('shell.studio.release.until'), render: (_, row) => row.until },
  ]

  const parts: PartRow[] = [
    ...release.modules.map((part) => partRow('module', part)),
    ...release.packages.map((part) => partRow('package', part)),
  ]
  const partColumns: AxisColumn<PartRow>[] = [
    { key: 'name', kind: 'freeText', title: t('shell.studio.release.name'), render: (_, row) => row.name },
    {
      key: 'type',
      kind: 'fixed',
      title: t('shell.studio.release.type'),
      render: (_, row) => t(`shell.studio.release.type.${row.type}`),
    },
    { key: 'version', kind: 'fixed', title: t('shell.studio.release.version'), render: (_, row) => row.version },
  ]

  return (
    <Flex vertical gap={spacing.lg} data-testid="studio-release">
      <StudioPageHeading
        title={t('shell.studio.release.title').replace('{release}', String(release.number))}
        tags={
          <>
            {release.live && <StudioLiveTag />}
            <ReleaseOffStandard tagOnly relaxed={first.relaxed} />
          </>
        }
        actions={
          canActivate &&
          !release.live && (
            <AxisButton
              role="secondary"
              data-testid="studio-release-activate"
              disabled={busy || release.blockers.length > 0}
              onClick={onActivate}
            >
              {t('shell.studio.activate.button')}
            </AxisButton>
          )
        }
      />
      {!release.live && release.blockers.length > 0 && history.active && (
        <StudioReleaseBlockers
          release={release.number}
          liveNumber={history.active.number}
          blockers={release.blockers}
        />
      )}
      <Descriptions
        column={1}
        items={[
          { key: 'label', label: t('shell.studio.releases.label'), children: first.label || <EmptyDash /> },
          { key: 'status', label: t('shell.studio.release.status'), children: status ?? <EmptyDash /> },
          { key: 'published', label: t('shell.studio.releases.published'), children: format(first.activatedAt) },
          { key: 'publishedBy', label: t('shell.studio.releases.publishedBy'), children: releasePublisher(first, t) },
          { key: 'note', label: t('shell.studio.releases.note'), children: first.note || <EmptyDash /> },
        ]}
      />
      {release.modules.length > 0 && (
        <ReleaseBlock title={t('shell.studio.release.relaxed')}>
          <RelaxedPoints relaxed={first.relaxed} locale={locale} />
        </ReleaseBlock>
      )}
      <ReleaseBlock title={compareTitle(release, t)}>
        {release.previous === null || release.changes === null ? (
          <Typography.Text>{t('shell.studio.release.changes.first')}</Typography.Text>
        ) : (
          <>
            {release.changes.length === 0 && (
              <Typography.Text>
                {t('shell.studio.release.changes.none').replace('{previous}', String(release.previous))}
              </Typography.Text>
            )}
            <StudioChangeList
              plainChanged
              rows={changeRows(release.changes, resources, locale, t)}
              parts={release.partChanges ?? []}
              address={address}
            />
          </>
        )}
      </ReleaseBlock>
      <ReleaseBlock title={t('shell.studio.release.parts')}>
        {parts.length === 0 ? (
          <Typography.Text>{t('shell.studio.release.parts.none')}</Typography.Text>
        ) : (
          <AxisTable<PartRow>
            columns={partColumns}
            rowKey={(row) => row.key}
            dataSource={parts}
            loading={false}
            emptyText={t('shell.studio.release.parts.none')}
            pagination={false}
            toolbar={{}}
          />
        )}
      </ReleaseBlock>
      <ReleaseBlock title={t('shell.studio.release.activations')}>
        <AxisTable<ActivationRow>
          columns={activationColumns}
          rowKey={(row) => row.key}
          dataSource={activations}
          loading={false}
          emptyText={t('shell.studio.releases.empty')}
          pagination={false}
          toolbar={{}}
          rowAttributes={() => ({ 'data-testid': 'studio-release-activation' }) as HTMLAttributes<HTMLElement>}
        />
      </ReleaseBlock>
    </Flex>
  )
}

function partRow(type: PartRow['type'], part: StudioReleasePart): PartRow {
  return { key: `${type}:${part.id}`, name: part.label, type, version: part.version }
}

function compareTitle(release: StudioRelease, t: (key: string) => string): string {
  if (release.previous === null) {
    return t('shell.studio.release.changes')
  }
  return t(
    release.previousIsLive ? 'shell.studio.release.changes.ifActivated' : 'shell.studio.release.changes.from',
  ).replace('{previous}', String(release.previous))
}

function ReleaseBlock({ title, children }: { title: string; children: ReactNode }) {
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

/** The relaxed points in words, grouped by the label of their module. */
function RelaxedPoints({ relaxed, locale }: { relaxed: StudioRelaxedPoint[]; locale: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  if (relaxed.length === 0) {
    return <Typography.Text>{t('shell.studio.release.relaxed.none')}</Typography.Text>
  }
  const groups = new Map<string, StudioRelaxedPoint[]>()
  for (const point of relaxed) {
    const module = point.moduleTitle ?? point.module
    groups.set(module, [...(groups.get(module) ?? []), point])
  }
  return (
    <Flex vertical gap={spacing.md}>
      {[...groups].map(([module, points]) => (
        <Flex key={module} vertical gap={spacing.sm}>
          <Typography.Text strong>{module}</Typography.Text>
          {points.map((point) => {
            const entity = point.entityTitles?.[locale] ?? point.point.split('/')[0]
            const label =
              point.titles?.[locale] ??
              (point.point.endsWith('ceiling/others') ? t('shell.studio.release.relaxed.others') : point.point)
            const kind =
              point.part === null ? entity : t(`shell.studio.release.part.${point.part}`).replace('{entity}', entity)
            return (
              <Flex key={point.point} vertical data-testid="studio-release-relaxed-point">
                <Typography.Text>{label}</Typography.Text>
                <Typography.Text style={{ color: colour.textMuted }}>{kind}</Typography.Text>
                <Typography.Text>{point.reason}</Typography.Text>
              </Flex>
            )
          })}
        </Flex>
      ))}
    </Flex>
  )
}
