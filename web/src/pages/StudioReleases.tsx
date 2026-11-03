import { PlayCircleOutlined } from '@ant-design/icons'
import { Flex, Modal, Tag, Typography } from 'antd'
import { useContext, useState, type HTMLAttributes } from 'react'
import { AxisTable } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import { releaseActorName, releaseGroups, type StudioReleaseGroup, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/**
 * Proposed: the releases of the open application, one row per release, the highest number first: its number, with
 * Live beside the one that runs, as the band says, its label, when it was published and by whom, and its note last. A
 * label or note its publish did not give shows as no value. When
 * `canActivate`, every release that is not live has an icon button, Activate…, off while `busy`: activating goes
 * either way, back to an older release or forward to a newer one, so one verb names it. The button opens a dialog that
 * says which release replaces the live one, and its confirmation calls `onActivate`. An off-standard release shows the
 * Off-standard tag with each relaxed module, point and reason.
 */
export function StudioReleasesList({
  history,
  canRollBack: canActivate,
  busy,
  onRollBack: onActivate,
}: {
  history: StudioReleases
  canRollBack: boolean
  busy: boolean
  onRollBack: (release: { releaseId: string }) => void
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
            // Proposed: the release's number, with Live beside the one that runs, as the band says. No status column:
            // a release that is not live has no state of its own to name.
            key: 'number',
            title: t('shell.studio.releases.number'),
            kind: 'fit',
            render: (release) => (
              <Flex align="center" gap="small">
                {release.number}
                {release.live && <Tag color="success">{t('shell.studio.releases.live')}</Tag>}
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
          {
            key: 'relaxed',
            title: t('shell.studio.releases.relaxed'),
            kind: 'text',
            render: (release) => <ReleaseOffStandard relaxed={release.relaxed} />,
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
    </section>
  )
}
