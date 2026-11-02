import { Button, Flex, Modal, Table, Tag, Typography } from 'antd'
import { useContext, useState, type HTMLAttributes } from 'react'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import { releaseRows, type StudioReleaseRow, type StudioReleases } from '../platform/studio'
import { useText } from '../platform/texts'
import { visuallyHidden } from '../platform/visuallyHidden'
import { PlatformContext } from './context'

/**
 * The release history of the open application, newest activation first, with one row marked Active. A release that
 * was activated more than once has a row for each activation, and only its newest row is marked. When `canRollBack`,
 * a row of an off-standard release shows the Off-standard tag with each relaxed module, point and reason. When
 * `canRollBack`, every row of a release that is not active has a Roll back button, off while `busy`. The button opens a dialog that
 * names the release number and label, and its confirmation calls `onRollBack`.
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
  onRollBack: (row: StudioReleaseRow) => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale
  const [pending, setPending] = useState<StudioReleaseRow | null>(null)
  const time = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' })
  const activeId = history.active?.releaseId

  function confirm() {
    const row = pending
    setPending(null)
    if (row) {
      onRollBack(row)
    }
  }

  return (
    <section data-testid="studio-releases">
      <Flex vertical gap="small">
        <Typography.Title level={3}>{t('shell.studio.releases')}</Typography.Title>
        <Table<StudioReleaseRow>
          size="small"
          pagination={false}
          rowKey={(row) => `${row.releaseId}/${row.activatedAt}`}
          dataSource={releaseRows(history)}
          locale={{ emptyText: t('shell.studio.releases.empty') }}
          onRow={(row) =>
            ({
              'data-testid': 'studio-release-row',
              'data-number': row.number,
              'data-active': row.active ? 'true' : undefined,
              'data-off-standard': row.offStandard ? 'true' : undefined,
            }) as HTMLAttributes<HTMLElement>
          }
          columns={[
            { key: 'number', title: t('shell.studio.releases.number'), render: (_, row) => row.number },
            { key: 'label', title: t('shell.studio.releases.label'), render: (_, row) => row.label },
            { key: 'note', title: t('shell.studio.releases.note'), render: (_, row) => row.note },
            { key: 'actor', title: t('shell.studio.releases.actor'), render: (_, row) => row.actorName ?? row.actor },
            {
              key: 'time',
              title: t('shell.studio.releases.time'),
              render: (_, row) => time.format(new Date(row.activatedAt)),
            },
            {
              key: 'active',
              title: t('shell.studio.releases.active'),
              render: (_, row) => (row.active ? <Tag color="success">{t('shell.studio.releases.active')}</Tag> : null),
            },
            {
              key: 'relaxed',
              title: t('shell.studio.releases.relaxed'),
              render: (_, row) => <ReleaseOffStandard relaxed={row.relaxed} />,
            },
            ...(canRollBack
              ? [
                  {
                    key: 'rollback',
                    title: <span style={visuallyHidden}>{t('shell.studio.rollback')}</span>,
                    render: (_: unknown, row: StudioReleaseRow) =>
                      row.releaseId === activeId ? null : (
                        <Button size="small" disabled={busy} onClick={() => setPending(row)}>
                          {t('shell.studio.rollback')}
                        </Button>
                      ),
                  },
                ]
              : []),
          ]}
        />
      </Flex>
      <Modal
        open={pending !== null}
        title={t('shell.studio.rollback.title')}
        okText={t('shell.studio.rollback.confirm')}
        cancelText={t('shell.studio.cancel')}
        onOk={confirm}
        onCancel={() => setPending(null)}
      >
        {pending && (
          <Typography.Text data-testid="studio-rollback-release">
            {t('shell.studio.rollback.release')} {pending.number}
            {pending.label ? ` ${pending.label}` : ''}
          </Typography.Text>
        )}
      </Modal>
    </section>
  )
}
