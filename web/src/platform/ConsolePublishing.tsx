import { Alert, Card, Flex, Select, Table, Tag, type TableColumnsType } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import {
  fetchConsolePublishing,
  type ConsoleApplication,
  type ConsolePublishRequest,
  type ConsolePublishing,
  type ConsoleReleaseItem,
} from './console'
import { EmptyDash } from './EmptyValue'
import { markCurrentPage } from './pagination'
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
  queued: 'blue',
  running: 'blue',
  succeeded: 'green',
  failed: 'red',
}

function formatTime(value: string | null, locale: string) {
  return value === null ? <EmptyDash /> : new Date(value).toLocaleString(locale)
}

/**
 * The Publishing view of the operator console: the release history and the publish requests of one application, read
 * only. The picker starts on the first application of the tenant. The releases table marks the active release, and the
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

  const releaseColumns = useMemo<TableColumnsType<ConsoleReleaseItem>>(
    () => [
      {
        key: 'number',
        title: t('shell.console.publishing.number'),
        render: (_value: unknown, row) => (
          <Flex gap="small" align="center">
            <span>{`#${row.number}`}</span>
            {row === activeRow && <Tag color="green">{t('shell.console.publishing.active')}</Tag>}
          </Flex>
        ),
      },
      {
        key: 'label',
        title: t('shell.console.publishing.label'),
        render: (_value: unknown, row) => row.label ?? <EmptyDash />,
      },
      {
        key: 'note',
        title: t('shell.console.publishing.note'),
        render: (_value: unknown, row) => row.note ?? <EmptyDash />,
      },
      {
        key: 'activatedBy',
        title: t('shell.console.publishing.activatedBy'),
        render: (_value: unknown, row) => row.actorName ?? row.actor,
      },
      {
        key: 'activatedAt',
        title: t('shell.console.publishing.activatedAt'),
        render: (_value: unknown, row) => formatTime(row.activatedAt, locale),
      },
    ],
    [activeRow, locale, t],
  )

  const requestColumns = useMemo<TableColumnsType<ConsolePublishRequest>>(
    () => [
      { key: 'kind', title: t('shell.console.publishing.kind'), dataIndex: 'kind' },
      { key: 'target', title: t('shell.console.publishing.target'), dataIndex: 'target' },
      {
        key: 'status',
        title: t('shell.console.publishing.status'),
        render: (_value: unknown, row) => (
          <Tag color={statusColors[row.status]}>{t(`shell.console.publishing.status.${row.status}`)}</Tag>
        ),
      },
      {
        key: 'label',
        title: t('shell.console.publishing.label'),
        render: (_value: unknown, row) => row.label ?? <EmptyDash />,
      },
      {
        key: 'requestedBy',
        title: t('shell.console.publishing.requestedBy'),
        render: (_value: unknown, row) => row.requestedByName ?? row.requestedBy,
      },
      {
        key: 'requestedAt',
        title: t('shell.console.publishing.requestedAt'),
        render: (_value: unknown, row) => formatTime(row.requestedAt, locale),
      },
      {
        key: 'finishedAt',
        title: t('shell.console.publishing.finishedAt'),
        render: (_value: unknown, row) => formatTime(row.finishedAt, locale),
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
            <Table<ConsoleReleaseItem>
              data-testid="console-publishing-releases"
              rowKey={(row) => `${row.releaseId}/${row.activatedAt}`}
              title={() => t('shell.console.publishing.releases')}
              columns={releaseColumns}
              scroll={{ x: 'max-content' }}
              dataSource={load.publishing?.releases}
              loading={loading}
              pagination={false}
              locale={{ emptyText: t('shell.console.publishing.empty') }}
            />
            <Table<ConsolePublishRequest>
              data-testid="console-publishing-requests"
              rowKey="id"
              title={() => t('shell.console.publishing.requests')}
              columns={requestColumns}
              scroll={{ x: 'max-content' }}
              dataSource={load.publishing?.requests}
              loading={loading}
              locale={{ emptyText: t('shell.console.publishing.empty') }}
              pagination={{
                current: page,
                pageSize: load.publishing?.pageSize,
                total: load.publishing?.totalCount ?? 0,
                showSizeChanger: false,
                hideOnSinglePage: true,
                itemRender: markCurrentPage(page),
              }}
              onChange={(pagination) => setPage(pagination.current ?? 1)}
            />
          </>
        )}
      </Flex>
    </Card>
  )
}
