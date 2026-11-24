import { Flex, Spin, Typography } from 'antd'
import { type HTMLAttributes, useContext, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyState } from '../platform/EmptyState'
import { EmptyDash } from '../platform/EmptyValue'
import { sortByName } from '../platform/listOrder'
import { PageActions, PageLayout } from '../platform/PageLayout'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import { ReleasePublisher } from '../platform/ReleasePublisher'
import { studioViewAddress } from '../platform/studioAddress'
import {
  changeRows,
  fetchStudioRelease,
  releasePublisher,
  ServerUnreachableError,
  textIn,
  type StudioRelaxedPoint,
  type StudioRelease,
  type StudioReleaseItem,
  type StudioReleasePart,
  type StudioReleases,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { localDay } from '../platform/tableFilter'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useTableView } from '../platform/useTableView'
import { PlatformContext } from './context'
import { relaxedPointLabel } from './relaxEntries'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { TermList } from '../platform/TermList'
import { StudioActivateDialog, StudioLiveTag, StudioReleaseBlockers } from './StudioReleases'
import { StudioChangeList } from './StudioChangeList'

/** One activation of the release, as a row of the Activations table. */
interface ActivationRow {
  key: string
  when: string
  by: string
  actor: string
  actorName: string | null
  replaced: number | null
  until: string | null
  /** The local calendar day of `when`, `YYYY-MM-DD`, which the When filter compares. */
  whenDay: string
  /** The local calendar day of `until`, which the Until filter compares. Null for the live release and the last activation. */
  untilDay: string | null
}

/** One row of the modules and packages table. */
interface PartRow {
  key: string
  name: string
  type: 'module' | 'package'
  version: string
}

// The two tables filter the columns that they show. Stable arrays keep useTableView from recomputing on each render.
const partFields = [
  { name: 'name', type: 'text' },
  { name: 'type', type: 'enum' },
  { name: 'version', type: 'text' },
] as const
const activationFields = [
  { name: 'when', type: 'date' },
  { name: 'by', type: 'text' },
  { name: 'replaced', type: 'integer' },
  { name: 'until', type: 'date' },
] as const
// The tables have no parameters of their own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * The page of one release, at `&view=release&release=N`: the heading "Release N" with the Live and Off-standard tags,
 * and Activate… on its right for a publisher (`canActivate`) when the release is not live, which asks for the same
 * confirmation as the release list and calls `onActivate` with the release's id. A release with blockers cannot replace
 * the live one: a sentence and each blocking module's line follow the heading, and Activate… is off. Under it, in order, come the parts, with a
 * rule between them: the Details part (Label, Status, Published, Published by and Note), the relaxed points in words grouped by module, the changes under
 * a title that names the release they compare with, where the modules and packages that differ follow the files, then
 * the modules and packages the release pins, and every activation of
 * the release. The Details part and the activations come from `history`, and the rest from the release route, which loads
 * again when `history` does. A number that the history lacks shows one line and a link to every release, and so does a
 * failed load. The parts table and the activations each have Filter on every column, kept in the address, with the
 * activations under the `activations.` prefix, see docs/reference/tables.md#tables.
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

  const all = <Link to={studioViewAddress(application, 'releases')}>{t('shell.studio.release.all')}</Link>
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
  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  const format = (value: string) => time.format(new Date(value))
  const partView = useTableView(partFields, recordPageSize, noParameters, false, true)
  const activationView = useTableView(
    activationFields,
    recordPageSize,
    noParameters,
    false,
    true,
    false,
    'activations.',
  )

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
      actor: item.actor,
      actorName: item.actorName,
      replaced: sorted[index - 1]?.number ?? null,
      until: next !== undefined ? format(next.activatedAt) : release.live ? t('shell.studio.release.now') : null,
      whenDay: localDay(item.activatedAt)!,
      untilDay: next !== undefined ? localDay(next.activatedAt) : null,
    }
  })
  const activationColumns: AxisColumn<ActivationRow>[] = [
    {
      key: 'when',
      kind: 'fixed',
      title: t('shell.studio.release.when'),
      render: (_, row) => row.when,
      filter: { kind: 'date', value: (row) => row.whenDay },
    },
    {
      key: 'by',
      kind: 'freeText',
      title: t('shell.studio.release.by'),
      render: (_, row) => <ReleasePublisher row={row} />,
      filter: { kind: 'text', value: (row) => row.by },
    },
    {
      key: 'replaced',
      kind: 'fixed',
      title: t('shell.studio.release.replaced'),
      render: (_, row) => row.replaced,
      filter: { kind: 'number', value: (row) => row.replaced },
    },
    {
      key: 'until',
      kind: 'fixed',
      title: t('shell.studio.release.until'),
      render: (_, row) => row.until,
      filter: { kind: 'date', value: (row) => row.untilDay },
    },
  ]

  const parts = sortByName(
    [
      ...release.modules.map((part) => partRow('module', part)),
      ...release.packages.map((part) => partRow('package', part)),
    ],
    (row) => row.name,
    locale,
    (row) => (row.type === 'module' ? 0 : 1),
  )
  const partColumns: AxisColumn<PartRow>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.studio.release.name'),
      render: (_, row) => row.name,
      filter: { kind: 'text', value: (row) => row.name },
    },
    {
      key: 'type',
      kind: 'fixed',
      title: t('shell.studio.release.type'),
      render: (_, row) => t(`shell.studio.release.type.${row.type}`),
      filter: {
        kind: 'enum',
        value: (row) => row.type,
        options: (['module', 'package'] as const).map((type) => ({
          value: type,
          label: t(`shell.studio.release.type.${type}`),
        })),
      },
    },
    {
      key: 'version',
      kind: 'fixed',
      title: t('shell.studio.release.version'),
      render: (_, row) => row.version,
      filter: { kind: 'text', value: (row) => row.version },
    },
  ]

  return (
    <div data-testid="studio-release">
      <PageLayout
        title={t('shell.studio.release.title').replace('{release}', String(release.number))}
        tags={
          <>
            {release.live && <StudioLiveTag />}
            <ReleaseOffStandard tagOnly relaxed={first.relaxed} locale={locale} />
          </>
        }
        line={t('shell.studio.release.kind')}
      >
        {canActivate && !release.live && (
          <PageActions>
            <AxisButton
              role="secondary"
              data-testid="studio-release-activate"
              disabled={busy || release.blockers.length > 0}
              onClick={onActivate}
            >
              {t('shell.studio.activate.button')}
            </AxisButton>
          </PageActions>
        )}
        {!release.live && release.blockers.length > 0 && history.active && (
          <StudioReleaseBlockers
            release={release.number}
            liveNumber={history.active.number}
            blockers={release.blockers}
          />
        )}
        <SectionParts>
          <SectionPart testName="facts">
            <TermList
              terms={[
                { key: 'label', term: t('shell.studio.releases.label'), value: first.label || <EmptyDash /> },
                { key: 'status', term: t('shell.studio.release.status'), value: status ?? <EmptyDash /> },
                { key: 'published', term: t('shell.studio.releases.published'), value: format(first.activatedAt) },
                {
                  key: 'publishedBy',
                  term: t('shell.studio.releases.publishedBy'),
                  value: <ReleasePublisher row={first} />,
                },
                { key: 'note', term: t('shell.studio.releases.note'), value: first.note || <EmptyDash /> },
              ]}
            />
          </SectionPart>
          {release.modules.length > 0 && (
            <SectionPart testName="relaxed" heading={{ title: t('shell.studio.release.relaxed') }}>
              <RelaxedPoints relaxed={first.relaxed} locale={locale} />
            </SectionPart>
          )}
          <SectionPart testName="changes" heading={{ title: compareTitle(release, t) }}>
            {release.previous === null || release.changes === null ? (
              <Typography.Text>{t('shell.studio.release.changes.first')}</Typography.Text>
            ) : (
              <>
                {release.changes.length === 0 && (
                  <EmptyState
                    text={t('shell.studio.release.changes.none').replace('{previous}', String(release.previous))}
                  />
                )}
                <StudioChangeList
                  plainChanged
                  rows={changeRows(release.changes, resources, locale, t)}
                  parts={release.partChanges ?? []}
                  application={application}
                />
              </>
            )}
          </SectionPart>
          <SectionPart testName="parts" heading={{ title: t('shell.studio.release.parts') }}>
            <AxisTable<PartRow>
              columns={partColumns}
              rowKey={(row) => row.key}
              dataSource={parts}
              loading={false}
              emptyText={t('shell.studio.release.parts.none')}
              view={{ query: partView.view, onChange: partView.setView }}
              toolbar={{}}
            />
          </SectionPart>
          <SectionPart testName="activations" heading={{ title: t('shell.studio.release.activations') }}>
            <AxisTable<ActivationRow>
              columns={activationColumns}
              rowKey={(row) => row.key}
              dataSource={activations}
              loading={false}
              emptyText={t('shell.studio.releases.empty')}
              view={{ query: activationView.view, onChange: activationView.setView }}
              toolbar={{}}
              rowAttributes={() => ({ 'data-testid': 'studio-release-activation' }) as HTMLAttributes<HTMLElement>}
            />
          </SectionPart>
        </SectionParts>
      </PageLayout>
    </div>
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

/** The relaxed points in words, grouped by the label of their module. */
function RelaxedPoints({ relaxed, locale }: { relaxed: StudioRelaxedPoint[]; locale: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  if (relaxed.length === 0) {
    return <EmptyState text={t('shell.studio.release.relaxed.none')} />
  }
  const groups = new Map<string, StudioRelaxedPoint[]>()
  for (const point of relaxed) {
    const module = point.moduleTitle ?? point.module
    groups.set(module, [...(groups.get(module) ?? []), point])
  }
  const pointLabel = (point: StudioRelaxedPoint) => relaxedPointLabel(point, locale, t)
  const sortedGroups = sortByName([...groups], ([module]) => module, locale)
  return (
    <Flex vertical gap={spacing.md}>
      {sortedGroups.map(([module, points]) => (
        <Flex key={module} vertical gap={spacing.sm}>
          <Typography.Text strong>{module}</Typography.Text>
          {sortByName(points, pointLabel, locale).map((point) => {
            const entity = textIn(point.entityTitles, locale) ?? point.point.split('/')[0]
            const label = pointLabel(point)
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
