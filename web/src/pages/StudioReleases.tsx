import { PlayCircleOutlined } from '@ant-design/icons'
import { Flex, Modal, Tag, Typography } from 'antd'
import { useContext, useState, type HTMLAttributes } from 'react'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import { releaseList, releasePublisher, type StudioReleaseListRow, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { PlatformContext } from './context'

/**
 * The release list of the open application: one row for each release, the highest number first, on one page. A row
 * shows the release number with the Live tag when it is the release that runs, and the Off-standard tag with each
 * relaxed module, point and reason when it relaxed a contract point. The other columns are the label, the time it was
 * published, who published it and the note, from the release's first activation. An empty label or note shows the em
 * dash, and Published by is a name, never an id. When `canRollBack`, every row but the live one has an Activate… icon
 * button, off while `busy`. The button opens a dialog that says which release replaces the live one, and its
 * confirmation calls `onRollBack`.
 */
export function StudioReleasesList({
  history,
  canRollBack,
  busy,
  onRollBack,
}: {
  history: StudioReleases
  canRollBack: boolean
  busy: boolean
  onRollBack: (row: StudioReleaseListRow) => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale
  const [pending, setPending] = useState<StudioReleaseListRow | null>(null)
  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  const liveNumber = history.active?.number

  function confirm() {
    const row = pending
    setPending(null)
    if (row) {
      onRollBack(row)
    }
  }

  const columns: AxisColumn<StudioReleaseListRow>[] = [
    {
      key: 'release',
      kind: 'fixed',
      title: t('shell.studio.releases.release'),
      render: (_, row) => (
        <Flex wrap align="flex-start" gap="small">
          {row.number}
          {row.live && <Tag color="success">{t('shell.studio.releases.live')}</Tag>}
          <ReleaseOffStandard relaxed={row.relaxed} />
        </Flex>
      ),
    },
    { key: 'label', kind: 'freeText', title: t('shell.studio.releases.label'), render: (_, row) => row.label || null },
    {
      key: 'published',
      kind: 'fixed',
      title: t('shell.studio.releases.published'),
      render: (_, row) => time.format(new Date(row.publishedAt)),
    },
    {
      key: 'publishedBy',
      kind: 'freeText',
      title: t('shell.studio.releases.publishedBy'),
      render: (_, row) => releasePublisher(row, t),
    },
    { key: 'note', kind: 'freeText', title: t('shell.studio.releases.note'), render: (_, row) => row.note || null },
  ]

  return (
    <section data-testid="studio-releases">
      <Flex vertical gap="small">
        <Typography.Title level={3}>{t('shell.studio.releases')}</Typography.Title>
        <AxisTable<StudioReleaseListRow>
          columns={columns}
          rowKey={(row) => row.releaseId}
          dataSource={releaseList(history)}
          loading={false}
          emptyText={t('shell.studio.releases.empty')}
          pagination={false}
          toolbar={{}}
          rowAttributes={(row) =>
            ({
              'data-testid': 'studio-release-row',
              'data-number': row.number,
              'data-active': row.live ? 'true' : undefined,
              'data-off-standard': row.offStandard ? 'true' : undefined,
            }) as HTMLAttributes<HTMLElement>
          }
          rowActions={
            canRollBack
              ? {
                  configured: 1,
                  actions: (row) =>
                    row.live
                      ? null
                      : [
                          {
                            key: 'activate',
                            labelKey: 'shell.studio.activate',
                            nameSuffix: String(row.number),
                            icon: <PlayCircleOutlined />,
                            disabled: busy,
                            onClick: () => setPending(row),
                          },
                        ],
                }
              : undefined
          }
        />
      </Flex>
      <Modal
        open={pending !== null}
        title={pending && t('shell.studio.activate.title').replace('{release}', String(pending.number))}
        okText={t('shell.studio.activate.confirm')}
        cancelText={t('shell.studio.cancel')}
        onOk={confirm}
        onCancel={() => setPending(null)}
      >
        {pending && (
          <Typography.Text data-testid="studio-rollback-release">
            {liveNumber === undefined
              ? t('shell.studio.activate.becomesLive').replace('{release}', String(pending.number))
              : t('shell.studio.activate.replaces')
                  .replace('{release}', String(pending.number))
                  .replace('{live}', String(liveNumber))}
          </Typography.Text>
        )}
      </Modal>
    </section>
  )
}
