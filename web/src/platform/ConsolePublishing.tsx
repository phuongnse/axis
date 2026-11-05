import { Alert, Card, Flex, Select, Tag, Typography } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import {
  fetchConsolePublishing,
  type ConsoleApplication,
  type ConsolePublishRequest,
  type ConsolePublishing,
  type ConsoleReleaseItem,
} from './console'
import { ReleaseOffStandard } from './ReleaseOffStandard'
import { releasePublisher } from './studio'
import { useText } from './texts'

interface ConsolePublishingProps {
  /** The applications of the tenant, for the application picker. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The application and page of the request, so a result for an older request shows as loading. */
  key?: string
  /** The last answer loaded. It stays while the next page loads. */
  publishing?: ConsolePublishing
  failed: boolean
}

const statusColors: Record<ConsolePublishRequest['status'], string> = {
  queued: 'processing',
  running: 'processing',
  succeeded: 'success',
  failed: 'error',
}

function formatTime(value: string | null, locale: string) {
  return value === null ? null : new Date(value).toLocaleString(locale)
}

/**
 * The Publishing view of the operator console: the release history and the publish requests of one application, read
 * only. The picker starts on the first application of the tenant. The releases table marks the active release and every off-standard release with its relaxed points, and the
 * requests table lists the publish requests newest first, one page at a time. The view has no publish, activate or
 * rollback command, because those stay in Studio. A failure shows an error in this card only.
 */
export function ConsolePublishing({ applications, locale }: ConsolePublishingProps) {
  const t = useText()
  const [application, setApplication] = useState<string | undefined>(applications[0]?.id)
  const [page, setPage] = useState(1)
  const [load, setLoad] = useState<LoadState>({ failed: false })
  const key = application === undefined ? undefined : `${application}/${page}`
  const loading = key !== undefined && load.key !== key

  useEffect(() => {
    if (application === undefined) {
      return
    }
    const controller = new AbortController()
    const requested = `${application}/${page}`
    fetchConsolePublishing(application, page, controller.signal)
      .then((result) => {
        if (result.kind === 'forbidden') {
          setLoad({ key: requested, failed: true })
          return
        }
        // A page past the end becomes the last page once the total is known.
        setPage((current) =>
          Math.min(current, Math.max(1, Math.ceil(result.publishing.totalCount / result.publishing.pageSize))),
        )
        setLoad({ key: requested, publishing: result.publishing, failed: false })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console publishing failed', error)
        setLoad({ key: requested, failed: true })
      })
    return () => controller.abort()
  }, [application, page])

  // Only the newest row of the active release is marked, as Studio marks it, because a release can be in the history twice.
  const activeReleaseId = load.publishing?.active?.releaseId
  const activeRow =
    activeReleaseId === undefined
      ? undefined
      : load.publishing?.releases.find((row) => row.releaseId === activeReleaseId)

  const releaseColumns = useMemo<AxisColumn<ConsoleReleaseItem>[]>(
    () => [
      {
        key: 'number',
        kind: 'fixed',
        title: t('shell.console.publishing.number'),
        render: (_value, row) => (
          <Flex gap="small" align="center">
            <span>{`#${row.number}`}</span>
            {row === activeRow && <Tag color="success">{t('shell.console.publishing.active')}</Tag>}
          </Flex>
        ),
      },
      {
        key: 'label',
        kind: 'freeText',
        title: t('shell.console.publishing.label'),
        render: (_value, row) => row.label,
      },
      {
        key: 'note',
        kind: 'freeText',
        title: t('shell.console.publishing.note'),
        render: (_value, row) => row.note,
      },
      {
        key: 'activatedBy',
        kind: 'freeText',
        title: t('shell.console.publishing.activatedBy'),
        render: (_value, row) => releasePublisher(row, t),
      },
      {
        key: 'activatedAt',
        kind: 'fixed',
        title: t('shell.console.publishing.activatedAt'),
        render: (_value, row) => formatTime(row.activatedAt, locale),
      },
      {
        key: 'relaxed',
        kind: 'freeText',
        title: t('shell.console.publishing.relaxed'),
        render: (_value, row) => (row.offStandard ? <ReleaseOffStandard relaxed={row.relaxed} /> : null),
      },
    ],
    [activeRow, locale, t],
  )

  const requestColumns = useMemo<AxisColumn<ConsolePublishRequest>[]>(
    () => [
      { key: 'kind', kind: 'fixed', title: t('shell.console.publishing.kind'), dataIndex: 'kind' },
      { key: 'target', kind: 'fixed', title: t('shell.console.publishing.target'), dataIndex: 'target' },
      {
        key: 'status',
        kind: 'fixed',
        title: t('shell.console.publishing.status'),
        render: (_value, row) => (
          <Tag color={statusColors[row.status]}>{t(`shell.console.publishing.status.${row.status}`)}</Tag>
        ),
      },
      {
        key: 'label',
        kind: 'freeText',
        title: t('shell.console.publishing.label'),
        render: (_value, row) => row.label,
      },
      {
        key: 'requestedBy',
        kind: 'freeText',
        title: t('shell.console.publishing.requestedBy'),
        render: (_value, row) => row.requestedByName ?? row.requestedBy,
      },
      {
        key: 'requestedAt',
        kind: 'fixed',
        title: t('shell.console.publishing.requestedAt'),
        render: (_value, row) => formatTime(row.requestedAt, locale),
      },
      {
        key: 'finishedAt',
        kind: 'fixed',
        title: t('shell.console.publishing.finishedAt'),
        render: (_value, row) => formatTime(row.finishedAt, locale),
      },
    ],
    [locale, t],
  )

  return (
    <Card data-testid="console-publishing" title={t('shell.console.publishing.title')}>
      <Flex vertical gap="middle">
        <Select
          style={{ minWidth: 200, maxWidth: 400 }}
          aria-label={t('shell.console.publishing.application')}
          value={application}
          options={applications.map((candidate) => ({ value: candidate.id, label: candidate.name }))}
          onChange={(value: string) => {
            setApplication(value)
            setPage(1)
          }}
        />
        {load.failed && !loading ? (
          <Alert data-testid="console-publishing-error" type="error" title={t('shell.console.publishing.loadFailed')} />
        ) : (
          <>
            <Flex vertical gap="small">
              <Typography.Text strong>{t('shell.console.publishing.releases')}</Typography.Text>
              <AxisTable<ConsoleReleaseItem>
                testId="console-publishing-releases"
                rowKey={(row) => `${row.releaseId}/${row.activatedAt}`}
                columns={releaseColumns}
                dataSource={load.publishing?.releases}
                loading={loading}
                emptyText={t('shell.console.publishing.empty')}
              />
            </Flex>
            <Flex vertical gap="small">
              <Typography.Text strong>{t('shell.console.publishing.requests')}</Typography.Text>
              <AxisTable<ConsolePublishRequest>
                testId="console-publishing-requests"
                rowKey={(row) => row.id}
                columns={requestColumns}
                dataSource={load.publishing?.requests}
                loading={loading}
                emptyText={t('shell.console.publishing.empty')}
                pagination={{
                  current: page,
                  pageSize: load.publishing?.pageSize ?? 20,
                  total: load.publishing?.totalCount ?? 0,
                }}
                onChange={(pagination) => setPage(pagination.current ?? 1)}
              />
            </Flex>
          </>
        )}
      </Flex>
    </Card>
  )
}
