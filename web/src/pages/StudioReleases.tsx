import { PlayCircleOutlined } from '@ant-design/icons'
import { Alert, Button, Flex, Modal, Tag, Typography } from 'antd'
import { useContext, useEffect, useState, type HTMLAttributes, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router'
import { AxisTable } from '../platform/AxisTable'
import { EmptyDash, NotSet } from '../platform/EmptyValue'
import {
  fetchStudioRelease,
  relaxedPointWords,
  resourceTitle,
  type StudioReleaseDetail as StudioReleaseDetailData,
  type StudioReleasePart,
  releaseActorName,
  releaseGroups,
  type StudioRelaxedPoint,
  type StudioReleaseGroup,
  type StudioReleases,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { kindName } from './studioViews'
import { dialogWidth } from '../platform/dialogButtons'

/** Proposed: what one release holds beyond its history row, loaded again when the history changes. */
function useReleaseDetail(application: string, number: number | undefined, refresh: number) {
  const [detail, setDetail] = useState<{ number: number; value: StudioReleaseDetailData | 'failed' }>()
  useEffect(() => {
    if (number === undefined) {
      return
    }
    const controller = new AbortController()
    fetchStudioRelease(application, number, controller.signal).then(
      (value) => setDetail({ number, value }),
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn(`Loading release ${number} failed`, error)
          setDetail({ number, value: 'failed' })
        }
      },
    )
    return () => controller.abort()
  }, [application, number, refresh])
  return number !== undefined && detail?.number === number ? detail.value : undefined
}

/**
 * Proposed: why a release cannot replace the live one, said before Activate… is sent: each module the live release
 * pins that this release lacks, or pins at a version without some of its entities or fields, then the rule.
 */
function ReleaseBlockers({ detail, plain = false }: { detail: StudioReleaseDetailData; plain?: boolean }) {
  const t = useText()
  const blockers = detail.blockers ?? []
  const live = String(detail.live ?? '')
  const reasons = (
    <Flex vertical gap={4} data-testid={plain ? 'studio-release-blockers' : undefined}>
      {blockers.map((blocker) => (
        <Typography.Text key={blocker.module}>
          {(blocker.version
            ? t('shell.studio.release.blocker.older').replace('{version}', blocker.version)
            : t('shell.studio.release.blocker.missing')
          )
            .replace('{module}', blocker.label ?? blocker.module)
            .replace('{live}', live)
            .replace('{liveVersion}', blocker.liveVersion)}
        </Typography.Text>
      ))}
      <Typography.Text>{t('shell.studio.release.blocker.why')}</Typography.Text>
    </Flex>
  )
  // Proposed: in the dialog, whose title already says it, the reasons are plain text; on the page a warning holds them.
  return plain ? (
    reasons
  ) : (
    <Alert
      type="warning"
      data-testid="studio-release-blockers"
      title={cannotReplaceTitle(t, detail)}
      description={reasons}
    />
  )
}

function cannotReplaceTitle(t: (key: string) => string, detail: StudioReleaseDetailData) {
  return t('shell.studio.release.cannotReplace')
    .replace('{number}', String(detail.number))
    .replace('{live}', String(detail.live ?? ''))
}

/**
 * Proposed: the releases of the open application, one row per release, the highest number first: its number, with
 * Live beside the one that runs, as the band says, its label, when it was published and by whom, and its note last. A
 * label or note its publish did not give shows as no value. When
 * `canActivate`, every release that is not live has an icon button, Activate…, off while `busy`: activating goes
 * either way, back to an older release or forward to a newer one, so one verb names it. The button opens a dialog that
 * says which release replaces the live one, and its confirmation calls `onActivate`. Proposed: an off-standard release
 * has the Off-standard tag beside its number, as the live one has Live, and its number opens the release's own page at
 * `releaseAddress`, which lists its relaxed points. With `release`, the component is that page: its facts and its
 * relaxed points, with Activate… in `headingSlot`, the page heading's right side.
 */
export function StudioReleasesList({
  application,
  history,
  canRollBack: canActivate,
  busy,
  onRollBack: onActivate,
  release,
  releaseAddress,
  historyAddress,
  headingSlot,
}: {
  application: string
  history: StudioReleases
  canRollBack: boolean
  busy: boolean
  onRollBack: (release: { releaseId: string }) => void
  release?: number
  releaseAddress?: (number: number) => string
  historyAddress?: string
  headingSlot?: HTMLElement | null
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale
  const [pending, setPending] = useState<StudioReleaseGroup | null>(null)
  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' })
  const live = history.active?.number
  const refresh = history.items.length
  // Proposed: the release whose page shows, and the one Activate… asks about, each checked against the live release.
  const pageDetail = useReleaseDetail(application, release, refresh)
  const pendingDetail = useReleaseDetail(application, pending?.number, refresh)
  const pendingBlocked =
    pendingDetail !== undefined && pendingDetail !== 'failed' && (pendingDetail.blockers?.length ?? 0) > 0

  function confirm() {
    const release = pending
    setPending(null)
    if (release) {
      onActivate(release)
    }
  }

  const dialog = (
    <Modal
      width={dialogWidth('small')}
      open={pending !== null}
      title={
        pendingBlocked
          ? cannotReplaceTitle(t, pendingDetail)
          : t('shell.studio.activate.title').replace('{number}', String(pending?.number ?? ''))
      }
      okText={t('shell.studio.activate.confirm')}
      cancelText={pendingBlocked ? t('shell.studio.release.close') : t('shell.studio.cancel')}
      // Proposed: the dialog checks the release before it offers Activate: while it checks, Activate is off; a release
      // that cannot replace the live one says why, and the dialog offers Close alone.
      okButtonProps={{
        disabled: pendingDetail === undefined,
        style: pendingBlocked ? { display: 'none' } : undefined,
      }}
      onOk={confirm}
      onCancel={() => setPending(null)}
    >
      {pending && pendingBlocked && <ReleaseBlockers detail={pendingDetail} plain />}
      {pending && !pendingBlocked && (
        <Flex vertical gap={4} data-testid="studio-rollback-release">
          <Typography.Text>
            {(live === undefined ? t('shell.studio.activate.first') : t('shell.studio.activate.replaces'))
              .replace('{number}', String(pending.number))
              .replace('{live}', String(live ?? ''))}
          </Typography.Text>
          {pending.label && <Typography.Text style={{ color: colour.textMuted }}>{pending.label}</Typography.Text>}
        </Flex>
      )}
    </Modal>
  )

  if (release !== undefined) {
    const shown = releaseGroups(history).find((group) => group.number === release)
    if (!shown) {
      // Proposed: a release the history lacks says so in one line, with the way back to every release, as a page
      // under a workspace has no illustration of its own.
      return (
        <Flex vertical gap="small" align="flex-start" data-testid="studio-release-not-found">
          <Typography.Text>{t('shell.studio.release.notFound').replace('{number}', String(release))}</Typography.Text>
          {historyAddress && (
            <Link to={historyAddress} style={{ color: colour.primaryText }}>
              {t('shell.studio.overview.release.history')}
            </Link>
          )}
        </Flex>
      )
    }
    // Proposed: Activate… is the page's own action, on the right of its heading, as Run scenario is on a scenario's.
    // A release that cannot replace the live one has Activate… off, and the warning at the head of the page says why.
    const blocked = pageDetail && pageDetail !== 'failed' && (pageDetail.blockers?.length ?? 0) > 0
    const activate =
      canActivate && !shown.live ? (
        <Button
          data-testid={`studio-activate-${shown.number}`}
          disabled={busy || blocked}
          onClick={() => setPending(shown)}
        >
          {t('shell.studio.activate')}
        </Button>
      ) : null
    return (
      <section data-testid="studio-release" data-number={shown.number}>
        {activate && (headingSlot ? createPortal(activate, headingSlot) : activate)}
        <Flex vertical gap="large">
          {blocked && <ReleaseBlockers detail={pageDetail as StudioReleaseDetailData} />}
          <StudioReleaseDetail history={history} release={shown} detail={pageDetail} time={time} />
        </Flex>
        {dialog}
      </section>
    )
  }

  return (
    <section data-testid="studio-releases">
      <AxisTable<StudioReleaseGroup>
        rows={releaseGroups(history)}
        rowKey={(release) => release.releaseId}
        emptyText={t('shell.studio.releases.empty')}
        // Proposed: a long history pages as every table does, twenty releases a page, with no paging below that.
        pagination={{ pageSize: 20, hideOnSinglePage: true, showSizeChanger: false }}
        onRow={(release) =>
          ({
            'data-testid': 'studio-release-row',
            'data-number': release.number,
            'data-active': release.live ? 'true' : undefined,
            'data-off-standard': release.relaxed.length > 0 ? 'true' : undefined,
          }) as HTMLAttributes<HTMLElement>
        }
        columns={[
          {
            // Proposed: the release's number, the link that opens its page, with Live beside the one that runs, as the
            // band says, and Off-standard beside one with relaxed points. No status column: a release that is not live
            // has no state of its own to name, and its relaxed points are on its page, as they can be many.
            key: 'number',
            title: t('shell.studio.releases.number'),
            kind: 'fit',
            render: (release) => (
              <Flex align="center" gap="small">
                {releaseAddress ? (
                  <Link
                    to={releaseAddress(release.number)}
                    style={{ color: colour.primaryText, fontWeight: 500 }}
                    aria-label={t('shell.studio.release.title').replace('{number}', String(release.number))}
                  >
                    {release.number}
                  </Link>
                ) : (
                  release.number
                )}
                <ReleaseTags release={release} />
              </Flex>
            ),
          },
          {
            key: 'label',
            title: t('shell.studio.releases.label'),
            kind: 'text',
            render: (release) => release.label ?? <EmptyDash />,
          },
          {
            // Proposed: when and by whom a release was published are two columns, as facts of different kinds are.
            key: 'published',
            title: t('shell.studio.releases.published'),
            kind: 'fit',
            render: (release) => time.format(new Date(release.published.activatedAt)),
          },
          {
            key: 'publishedBy',
            title: t('shell.studio.releases.publishedBy'),
            kind: 'text',
            render: (release) => releaseActorName(release.published, t),
          },
          {
            // Proposed: the note, the longest text and the most often missing, comes last.
            key: 'note',
            title: t('shell.studio.releases.note'),
            kind: 'text',
            render: (release) => release.note ?? <EmptyDash />,
          },
        ]}
        rowActions={
          canActivate
            ? {
                count: 1,
                of: (release) =>
                  release.live
                    ? []
                    : [
                        {
                          key: 'activate',
                          labelKey: 'shell.studio.activate',
                          nameSuffix: `${t('shell.studio.rollback.release')} ${release.number}`,
                          icon: <PlayCircleOutlined />,
                          disabled: busy,
                          onClick: () => setPending(release),
                          attributes: { 'data-testid': `studio-activate-${release.number}` },
                        },
                      ],
              }
            : undefined
        }
      />
      {dialog}
    </section>
  )
}

/** Proposed: the tags of a release: Live on the one that runs and Off-standard on one with relaxed points. */
export function ReleaseTags({ release }: { release: StudioReleaseGroup }) {
  const t = useText()
  return (
    <>
      {release.live && <Tag color="success">{t('shell.studio.releases.live')}</Tag>}
      {release.relaxed.length > 0 && (
        <Tag color="warning" data-testid="release-off-standard">
          {t('shell.studio.offStandard')}
        </Tag>
      )}
    </>
  )
}

/** Proposed: a section of a release's page: its title, an optional muted line, then its content. */
function ReleaseSection({
  title,
  intro,
  testId,
  children,
}: {
  title: string
  intro?: string
  testId: string
  children?: ReactNode
}) {
  const { colour } = useDesignTokens()
  return (
    <Flex vertical gap="small" data-testid={testId}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        {title}
      </Typography.Title>
      {intro && <Typography.Text style={{ color: colour.textMuted }}>{intro}</Typography.Text>}
      {children}
    </Flex>
  )
}

/** Proposed: one activation of a release, with the release it replaced and when another replaced it. */
interface ReleaseActivationRow {
  key: string
  activatedAt: string
  actor: { actor: string; actorName?: string | null }
  replaced: number | null
  until: string | null
}

/**
 * Proposed: a release's page under its heading, "Release 6" with its tags: its facts as a description list (label,
 * whether it is live, when it was published and by whom, and its note on a line of its own), then, for an off-standard
 * release, its relaxed points; then what changed from the release numbered just below it, the modules and packages it
 * pins with their versions, and every time it was activated.
 */
function StudioReleaseDetail({
  history,
  release,
  detail: shown,
  time,
}: {
  history: StudioReleases
  release: StudioReleaseGroup
  detail: StudioReleaseDetailData | 'failed' | undefined
  time: Intl.DateTimeFormat
}) {
  const t = useText()
  const { colour, spacing, type } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'

  // Proposed: every activation of the release, newest first, from the whole history in time order: the one before it
  // is the release it replaced, and the one after it is when it stopped being live.
  const ordered = history.items.toSorted((left, right) => Date.parse(left.activatedAt) - Date.parse(right.activatedAt))
  const activations: ReleaseActivationRow[] = ordered
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => item.releaseId === release.releaseId)
    .map(({ item, index }) => ({
      key: `${item.activatedAt}/${index}`,
      activatedAt: item.activatedAt,
      actor: item,
      replaced: index > 0 && ordered[index - 1].releaseId !== item.releaseId ? ordered[index - 1].number : null,
      until: ordered[index + 1]?.activatedAt ?? null,
    }))
    .reverse()
  const last = activations[0]
  const at = (value: string) => time.format(new Date(value))

  const fact = (key: string, span: number, label: string, value: ReactNode) => (
    <div key={key} style={{ gridColumn: `span ${span}`, minWidth: 0 }} data-testid={`studio-release-${key}`}>
      <dt style={{ fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }}>{label}</dt>
      <dd style={{ margin: '6px 0 0', fontSize: type.fontSize, lineHeight: '22px', color: colour.text }}>{value}</dd>
    </div>
  )
  const previous = shown && shown !== 'failed' ? shown.previous : null
  const parts: StudioReleasePartRow[] =
    shown && shown !== 'failed'
      ? [
          ...shown.modules.map((part) => ({ ...part, type: 'module' as const })),
          ...shown.packages.map((part) => ({ ...part, type: 'package' as const })),
        ]
      : []

  return (
    <Flex vertical gap={spacing.lg}>
      <dl
        style={{
          margin: 0,
          display: 'grid',
          gridTemplateColumns: 'repeat(12, minmax(0, 1fr))',
          columnGap: spacing.md,
          rowGap: spacing.md,
          alignItems: 'start',
        }}
      >
        {fact('label', 3, t('shell.studio.releases.label'), release.label ?? <NotSet />)}
        {fact(
          'status',
          3,
          t('shell.studio.release.status'),
          release.live
            ? t('shell.studio.release.liveSince').replace('{time}', at(last.activatedAt))
            : last?.until
              ? t('shell.studio.release.replacedOn').replace('{time}', at(last.until))
              : t('shell.studio.release.notLive'),
        )}
        {fact('published', 3, t('shell.studio.releases.published'), at(release.published.activatedAt))}
        {fact('publishedBy', 3, t('shell.studio.releases.publishedBy'), releaseActorName(release.published, t))}
        {fact('note', 12, t('shell.studio.releases.note'), release.note ?? <NotSet />)}
      </dl>
      {/* Proposed: every part of a release shows, an empty one with a sentence that says what it means; only a part
          that does not apply is left out: a release that pins no module has no contract to relax. */}
      {(release.relaxed.length > 0 || (shown && shown !== 'failed' && shown.modules.length > 0)) && (
        <ReleaseSection
          title={t('shell.studio.releases.relaxed')}
          intro={t('shell.studio.release.relaxedIntro')}
          testId="studio-release-relaxed"
        >
          {/* Proposed: an empty table keeps its header and says what it means in its body, as every table does. */}
          {
            <AxisTable<StudioRelaxedPoint>
              rows={release.relaxed}
              rowKey={(point) => `${point.module}/${point.point}`}
              emptyText={t('shell.studio.release.relaxed.none')}
              pagination={false}
              onRow={() => ({ 'data-testid': 'release-relaxed-point' }) as HTMLAttributes<HTMLElement>}
              columns={[
                {
                  key: 'module',
                  title: t('shell.studio.relaxed.module'),
                  kind: 'text',
                  render: (point) => relaxedPointWords(point, locale, t).module,
                },
                {
                  // Proposed: the point by its label, with what kind of point it is and its entity on a muted line.
                  key: 'point',
                  title: t('shell.studio.relaxed.point'),
                  kind: 'text',
                  render: (point) => {
                    const words = relaxedPointWords(point, locale, t)
                    return (
                      <Flex vertical>
                        <span>{words.title}</span>
                        {words.kind && <span style={{ color: colour.textMuted }}>{words.kind}</span>}
                      </Flex>
                    )
                  },
                },
                {
                  key: 'reason',
                  title: t('shell.studio.relaxed.reason'),
                  kind: 'text',
                  render: (point) => point.reason,
                },
              ]}
            />
          }
        </ReleaseSection>
      )}
      {shown === 'failed' && (
        <Alert type="error" data-testid="studio-release-detail-error" title={t('shell.studio.release.loadFailed')} />
      )}
      {shown && shown !== 'failed' && (
        <>
          <ReleaseSection
            // Proposed: what this release changed from the release just below it, as a table: its resources, named from
            // the release that holds each, and its modules and packages, by their versions.
            title={
              previous === null
                ? t('shell.studio.release.changes.title')
                : t('shell.studio.release.changes.from').replace('{previous}', String(previous))
            }
            intro={previous === null ? t('shell.studio.release.changes.first') : undefined}
            testId="studio-release-changes"
          >
            {previous !== null && (
              <AxisTable<ReleaseChangeRow>
                rows={changeRows(shown, locale, t)}
                rowKey={(row) => row.key}
                emptyText={t('shell.studio.release.changes.none').replace('{previous}', String(previous))}
                pagination={false}
                onRow={() => ({ 'data-testid': 'studio-release-change' }) as HTMLAttributes<HTMLElement>}
                columns={[
                  {
                    key: 'name',
                    title: t('shell.studio.release.change.name'),
                    kind: 'text',
                    render: (row) => row.name,
                  },
                  { key: 'kind', title: t('shell.studio.release.change.kind'), kind: 'fit', render: (row) => row.kind },
                  {
                    key: 'change',
                    title: t('shell.studio.release.change.change'),
                    kind: 'text',
                    render: (row) => row.change,
                  },
                ]}
              />
            )}
          </ReleaseSection>
          <ReleaseSection title={t('shell.studio.release.parts')} testId="studio-release-parts">
            <AxisTable<StudioReleasePartRow>
              rows={parts}
              rowKey={(part) => `${part.type}/${part.id}`}
              emptyText={t('shell.studio.release.parts.none')}
              pagination={false}
              columns={[
                {
                  key: 'name',
                  title: t('shell.studio.release.part.name'),
                  kind: 'text',
                  render: (part) => part.label ?? part.id,
                },
                {
                  key: 'type',
                  title: t('shell.studio.release.part.type'),
                  kind: 'fit',
                  render: (part) => t(`shell.studio.release.part.${part.type}`),
                },
                {
                  key: 'version',
                  title: t('shell.studio.release.part.version'),
                  kind: 'fit',
                  render: (part) => <span style={{ fontVariantNumeric: 'tabular-nums' }}>{part.version}</span>,
                },
              ]}
            />
          </ReleaseSection>
        </>
      )}
      <ReleaseSection title={t('shell.studio.release.activations')} testId="studio-release-activations">
        <AxisTable<ReleaseActivationRow>
          rows={activations}
          rowKey={(row) => row.key}
          emptyText=""
          pagination={false}
          columns={[
            {
              key: 'activatedAt',
              title: t('shell.studio.release.activatedAt'),
              kind: 'fit',
              render: (row) => at(row.activatedAt),
            },
            {
              key: 'by',
              title: t('shell.studio.release.activatedBy'),
              kind: 'text',
              render: (row) => releaseActorName(row.actor, t),
            },
            {
              key: 'replaced',
              title: t('shell.studio.release.replaced'),
              kind: 'fit',
              render: (row) =>
                row.replaced === null ? (
                  <EmptyDash />
                ) : (
                  t('shell.studio.release.title').replace('{number}', String(row.replaced))
                ),
            },
            {
              key: 'until',
              title: t('shell.studio.release.liveUntil'),
              kind: 'fit',
              render: (row) => (row.until ? at(row.until) : t('shell.studio.release.now')),
            },
          ]}
        />
      </ReleaseSection>
    </Flex>
  )
}

type StudioReleasePartRow = StudioReleasePart & { type: 'module' | 'package' }

/** Proposed: one row of a release's changes: a resource, or a module or package by its version. */
interface ReleaseChangeRow {
  key: string
  name: string
  kind: string
  change: string
}

function changeRows(detail: StudioReleaseDetailData, locale: string, t: (key: string) => string): ReleaseChangeRow[] {
  const files = detail.changes.map((change) => ({
    key: `file/${change.path}`,
    name: resourceTitle(
      { file: change.path, name: change.name ?? null, titles: change.titles ?? null, locale: change.locale ?? null },
      locale,
    ),
    kind: change.kind ? kindName(change.kind, t) : '',
    change: t(`shell.studio.overview.change.${change.change}`),
  }))
  const parts = (detail.partChanges ?? []).map((part) => ({
    key: `${part.type}/${part.id}`,
    name: part.label ?? part.id,
    kind: t(`shell.studio.release.part.${part.type}`),
    change: t(`shell.studio.release.partChange.${part.change}`)
      .replace('{version}', part.version ?? '')
      .replace('{previous}', part.previousVersion ?? ''),
  }))
  return [...files, ...parts]
}
