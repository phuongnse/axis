import { PlayCircleOutlined } from '@ant-design/icons'
import { Flex, Modal, Tag, Typography } from 'antd'
import { useContext, useState, type HTMLAttributes } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import { releaseList, releasePublisher, type StudioReleaseListRow, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/** The Live tag of the release that runs, in the release list and on the release page's heading. */
export function StudioLiveTag() {
  const t = useText()
  return <Tag color="success">{t('shell.studio.releases.live')}</Tag>
}

/**
 * The dialog that asks before a release replaces the live one: it names `release`, the number to activate, and says it
 * replaces release `liveNumber`, or becomes the live release when none runs. It shows while `release` is not null.
 * Confirming calls `onConfirm`, and Cancel or closing calls `onCancel`. The release list and the release page both use it.
 */
export function StudioActivateDialog({
  release,
  liveNumber,
  onConfirm,
  onCancel,
}: {
  release: number | null
  liveNumber: number | undefined
  onConfirm: () => void
  onCancel: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  return (
    <Modal
      open={release !== null}
      width={size.dialog.small}
      title={release !== null && t('shell.studio.activate.title').replace('{release}', String(release))}
      okText={t('shell.studio.activate.confirm')}
      cancelText={t('shell.studio.cancel')}
      onOk={onConfirm}
      onCancel={onCancel}
    >
      {release !== null && (
        <Typography.Text data-testid="studio-activate-release">
          {liveNumber === undefined
            ? t('shell.studio.activate.becomesLive').replace('{release}', String(release))
            : t('shell.studio.activate.replaces')
                .replace('{release}', String(release))
                .replace('{live}', String(liveNumber))}
        </Typography.Text>
      )}
    </Modal>
  )
}

/**
 * The release list of the open application: one row for each release, the highest number first, on one page. A row
 * shows the release number, a link to the release's page of `application`, with the Live tag when it is the release that runs, and the Off-standard tag with each
 * relaxed module, point and reason when it relaxed a contract point. The other columns are the label, the time it was
 * published, who published it and the note, from the release's first activation. An empty label or note shows the em
 * dash, and Published by is a name, never an id. When `canActivate`, every row but the live one has an Activate… icon
 * button, off while `busy`. The button opens a dialog that says which release replaces the live one, and its
 * confirmation calls `onActivate`. The list has a level-5 title unless `titled` is false, where the page's own heading
 * names it.
 */
export function StudioReleasesList({
  application,
  history,
  canActivate,
  busy,
  titled = true,
  onActivate,
}: {
  application: string
  history: StudioReleases
  canActivate: boolean
  busy: boolean
  titled?: boolean
  onActivate: (row: StudioReleaseListRow) => void
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
      onActivate(row)
    }
  }

  const columns: AxisColumn<StudioReleaseListRow>[] = [
    {
      key: 'release',
      kind: 'fixed',
      title: t('shell.studio.releases.release'),
      render: (_, row) => (
        <Flex wrap align="flex-start" gap="small">
          <Link
            to={`/studio?${new URLSearchParams({ app: application, view: 'release', release: String(row.number) })}`}
          >
            {row.number}
          </Link>
          {row.live && <StudioLiveTag />}
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
        {titled && <Typography.Title level={5}>{t('shell.studio.releases')}</Typography.Title>}
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
            canActivate
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
      <StudioActivateDialog
        release={pending?.number ?? null}
        liveNumber={liveNumber}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
    </section>
  )
}
