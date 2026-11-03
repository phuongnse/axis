import { PlayCircleOutlined } from '@ant-design/icons'
import { Button, Flex, Modal, Tag, Typography } from 'antd'
import { useContext, useState, type HTMLAttributes, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router'
import { AxisTable } from '../platform/AxisTable'
import { EmptyDash, NotSet } from '../platform/EmptyValue'
import {
  relaxedPointWords,
  releaseActorName,
  releaseGroups,
  type StudioRelaxedPoint,
  type StudioReleaseGroup,
  type StudioReleases,
} from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

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
  history,
  canRollBack: canActivate,
  busy,
  onRollBack: onActivate,
  release,
  releaseAddress,
  historyAddress,
  headingSlot,
}: {
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

  function confirm() {
    const release = pending
    setPending(null)
    if (release) {
      onActivate(release)
    }
  }

  const dialog = (
    <Modal
      open={pending !== null}
      title={t('shell.studio.activate.title').replace('{number}', String(pending?.number ?? ''))}
      okText={t('shell.studio.activate.confirm')}
      cancelText={t('shell.studio.cancel')}
      onOk={confirm}
      onCancel={() => setPending(null)}
    >
      {pending && (
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
    const activate =
      canActivate && !shown.live ? (
        <Button data-testid={`studio-activate-${shown.number}`} disabled={busy} onClick={() => setPending(shown)}>
          {t('shell.studio.activate')}
        </Button>
      ) : null
    return (
      <section data-testid="studio-release" data-number={shown.number}>
        {activate && (headingSlot ? createPortal(activate, headingSlot) : activate)}
        <StudioReleaseDetail release={shown} time={time} />
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

/**
 * Proposed: a release's page under its heading, "Release 6" with its label under it: when it was published, by whom
 * and its note, as a description list, then, for an off-standard release, its relaxed points as a table, each with its
 * module, the point in words and the reason given.
 */
function StudioReleaseDetail({ release, time }: { release: StudioReleaseGroup; time: Intl.DateTimeFormat }) {
  const t = useText()
  const { colour, spacing, type } = useDesignTokens()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const fact = (key: string, span: number, value: ReactNode) => (
    <div key={key} style={{ gridColumn: `span ${span}`, minWidth: 0 }}>
      <dt style={{ fontWeight: 500, color: colour.textMuted, fontSize: type.fontSize, lineHeight: '22px' }}>
        {t(key)}
      </dt>
      <dd style={{ margin: '6px 0 0', fontSize: type.fontSize, lineHeight: '22px', color: colour.text }}>{value}</dd>
    </div>
  )
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
        {fact('shell.studio.releases.published', 3, time.format(new Date(release.published.activatedAt)))}
        {fact('shell.studio.releases.publishedBy', 3, releaseActorName(release.published, t))}
        {fact('shell.studio.releases.note', 6, release.note ?? <NotSet />)}
      </dl>
      {release.relaxed.length > 0 && (
        <Flex vertical gap="small" data-testid="studio-release-relaxed">
          <Typography.Title level={5} style={{ margin: 0 }}>
            {t('shell.studio.releases.relaxed')}
          </Typography.Title>
          <Typography.Text style={{ color: colour.textMuted }}>
            {t('shell.studio.release.relaxedIntro')}
          </Typography.Text>
          <AxisTable<StudioRelaxedPoint>
            rows={release.relaxed}
            rowKey={(point) => `${point.module}/${point.point}`}
            emptyText=""
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
        </Flex>
      )}
    </Flex>
  )
}
