import { PlayCircleOutlined } from '@ant-design/icons'
import { Flex, Modal, Spin, Tag, Typography } from 'antd'
import { useContext, useEffect, useState, type HTMLAttributes } from 'react'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { ReleaseOffStandard } from '../platform/ReleaseOffStandard'
import {
  fetchStudioRelease,
  releaseList,
  releasePublisher,
  ServerUnreachableError,
  type StudioReleaseBlocker,
  type StudioReleaseListRow,
  type StudioReleases,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'

/** The Live tag of the release that runs, in the release list and on the release page's heading. */
export function StudioLiveTag() {
  const t = useText()
  return <Tag color="success">{t('shell.studio.releases.live')}</Tag>
}

/** The sentence that says release `release` cannot replace live release `live`: the dialog's title and the release page's lead. */
function cannotReplaceTitle(t: (key: string) => string, release: number, live: number): string {
  return t('shell.studio.activate.cannotReplace').replace('{release}', String(release)).replace('{live}', String(live))
}

/**
 * Why release `release` cannot replace live release `liveNumber`: the sentence, then one line for each blocker, with the
 * module's label and its rule. A release that lacks the module says so, and one that pins it says its `version` lacks
 * some of the module's entities or fields. The release page and the Activate… dialog both show it.
 */
export function StudioReleaseBlockers({
  release,
  liveNumber,
  blockers,
}: {
  release: number
  liveNumber: number
  blockers: StudioReleaseBlocker[]
}) {
  const t = useText()
  return (
    <Flex vertical gap="small" data-testid="studio-release-blockers">
      <Typography.Text strong>{cannotReplaceTitle(t, release, liveNumber)}</Typography.Text>
      <BlockerLines blockers={blockers} />
    </Flex>
  )
}

function BlockerLines({ blockers }: { blockers: StudioReleaseBlocker[] }) {
  const t = useText()
  return (
    <Flex vertical gap="small">
      {blockers.map((blocker) => (
        <Typography.Text key={blocker.module} data-testid="studio-release-blocker">
          {t(blocker.version === null ? 'shell.studio.activate.blocker.lacks' : 'shell.studio.activate.blocker.version')
            .replace('{label}', blocker.label)
            .replace('{liveVersion}', blocker.liveVersion)
            .replace('{version}', blocker.version ?? '')}
        </Typography.Text>
      ))}
    </Flex>
  )
}

/**
 * The dialog that asks before a release replaces the live one: it names `release`, the number to activate, and says it
 * replaces release `liveNumber`, or becomes the live release when none runs. It shows while `release` is not null.
 * Confirming calls `onConfirm`, and Cancel or closing calls `onCancel`. It knows the release's `blockers` from the
 * caller, or else loads the release of `application` when it opens, with a spinner and Activate off meanwhile. A release
 * with blockers cannot replace the live one, so the title is the cannot-replace sentence, the body lists the blockers
 * and Close is the only button. When the release does not load, the dialog falls back to the replaces sentence and
 * Activate. The release list and the release page both use it.
 */
export function StudioActivateDialog({
  application,
  release,
  liveNumber,
  blockers,
  onConfirm,
  onCancel,
}: {
  application: string
  release: number | null
  liveNumber: number | undefined
  blockers?: StudioReleaseBlocker[]
  onConfirm: () => void
  onCancel: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const { lost } = useStudioConnection()
  const [loaded, setLoaded] = useState<{ release: number; value: StudioReleaseBlocker[] | 'failed' }>()
  const known = blockers === undefined ? undefined : { release, value: blockers }

  useEffect(() => {
    if (release === null || blockers !== undefined) {
      return
    }
    const controller = new AbortController()
    fetchStudioRelease(application, String(release), controller.signal).then(
      (value) => setLoaded({ release, value: value?.blockers ?? 'failed' }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
        } else {
          console.warn(`Loading release '${release}' of '${application}' failed`, error)
        }
        setLoaded({ release, value: 'failed' })
      },
    )
    return () => controller.abort()
  }, [application, release, blockers, lost])

  const answer = known ?? (loaded?.release === release ? loaded : undefined)
  const loading = release !== null && answer === undefined
  const blocked =
    release !== null && liveNumber !== undefined && Array.isArray(answer?.value) && answer.value.length > 0
      ? answer.value
      : null

  const title =
    release === null
      ? null
      : blocked === null
        ? t('shell.studio.activate.title').replace('{release}', String(release))
        : cannotReplaceTitle(t, release, liveNumber as number)
  return (
    <Modal
      open={release !== null}
      width={size.dialog.small}
      title={title}
      okText={t('shell.studio.activate.confirm')}
      okButtonProps={{ disabled: loading }}
      cancelText={t('shell.studio.cancel')}
      footer={
        blocked === null ? undefined : (
          <AxisButton role="secondary" onClick={onCancel}>
            {t('shell.studio.close')}
          </AxisButton>
        )
      }
      onOk={onConfirm}
      onCancel={onCancel}
    >
      {release !== null &&
        (loading ? (
          <Spin />
        ) : blocked !== null ? (
          <BlockerLines blockers={blocked} />
        ) : (
          <Typography.Text data-testid="studio-activate-release">
            {liveNumber === undefined
              ? t('shell.studio.activate.becomesLive').replace('{release}', String(release))
              : t('shell.studio.activate.replaces')
                  .replace('{release}', String(release))
                  .replace('{live}', String(liveNumber))}
          </Typography.Text>
        ))}
    </Modal>
  )
}

/**
 * The release list of the open application: one row for each release, the highest number first, on one page. A row
 * shows the release number, a link to the release's page of `application`, with the Live tag when it is the release that runs, and the Off-standard tag with each
 * relaxed module, point and reason when it relaxed a contract point. The other columns are the label, the time it was
 * published, who published it and the note, from the release's first activation. An empty label or note shows the em
 * dash, and Published by is a name, never an id. When `canActivate`, every row but the live one has an Activate… icon
 * button, off while `busy`. The button opens a dialog that says which release replaces the live one, or why it cannot, and its
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
          <ReleaseOffStandard relaxed={row.relaxed} locale={locale ?? 'en'} />
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
        application={application}
        release={pending?.number ?? null}
        liveNumber={liveNumber}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
    </section>
  )
}
