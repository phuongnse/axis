import { Flex, Select, Typography } from 'antd'
import { AxisAlert } from './AxisAlert'
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
import { SectionPart } from './SectionParts'
import { StatusTag, type StatusTone } from './StatusTag'
import { recordPageSize, recordQuery, tablePageSizes } from './tableQuery'
import { releasePublisher } from './studio'
import { useText } from './texts'
import { useTableView } from './useTableView'

interface ConsolePublishingProps {
  /** The applications of the tenant, for the application picker. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The application and query of the request, so a result for an older request shows as loading. */
  key?: string
  /** The last answer loaded. It stays while the next view loads. */
  publishing?: ConsolePublishing
  failed: boolean
}

const statusTones: Record<ConsolePublishRequest['status'], StatusTone> = {
  queued: 'neutral',
  running: 'neutral',
  succeeded: 'success',
  failed: 'danger',
}

/** The columns the requests table's search, filter and sort can name, see docs/reference/operator-console.md#api. */
const requestFields = [
  { name: 'kind', type: 'enum' },
  { name: 'target', type: 'enum' },
  { name: 'status', type: 'enum' },
  { name: 'label', type: 'text' },
  { name: 'requestedBy', type: 'text' },
  { name: 'requestedAt', type: 'date' },
  { name: 'finishedAt', type: 'date' },
] as const
// The view has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []
const requestKinds: ConsolePublishRequest['kind'][] = ['validate', 'publish', 'activate', 'test', 'accept']
const requestTargets: ConsolePublishRequest['target'][] = ['tenant', 'preview']
const requestStatuses: ConsolePublishRequest['status'][] = ['queued', 'running', 'succeeded', 'failed']

function formatTime(value: string | null, locale: string) {
  return value === null ? null : new Date(value).toLocaleString(locale)
}

/**
 * The Publishing view of the operator console: the release history and the publish requests of one application, read
 * only. The picker starts on the first application of the tenant and sits above both tables, outside the address. The
 * releases table marks the active release and every off-standard release with its relaxed points, and is never paged.
 * The requests table is paged on the server and has the full toolbar, with its view kept in the address under the
 * `publishRequests.` prefix, see docs/reference/operator-console.md#views and docs/reference/actions-and-controls.md#tables.
 * The view has no publish or activate command, because those stay in Studio. A failed load shows an error in each table in
 * place of its rows, and the requests table keeps its toolbar.
 */
export function ConsolePublishing({ applications, locale }: ConsolePublishingProps) {
  const t = useText()
  const [application, setApplication] = useState<string | undefined>(applications[0]?.id)
  const { view, setView, saved } = useTableView(
    requestFields,
    recordPageSize,
    noParameters,
    true,
    true,
    true,
    'publishRequests.',
    'console.publishRequests',
  )
  const query = recordQuery(view)
  const [reload, setReload] = useState(0)
  const [load, setLoad] = useState<LoadState>({ failed: false })
  const key = application === undefined ? undefined : `${application}?${query}`
  const loading = key !== undefined && load.key !== key
  // A failed answer is for the view that asked, so it shows only once that view has loaded.
  const failed = load.failed && !loading

  useEffect(() => {
    if (application === undefined) {
      return
    }
    const controller = new AbortController()
    const requested = `${application}?${query}`
    fetchConsolePublishing(application, query, controller.signal)
      .then((result) => {
        if (result.kind === 'forbidden') {
          setLoad({ key: requested, failed: true })
          return
        }
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
  }, [application, query, reload])

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
            {row === activeRow && <StatusTag tone="neutral">{t('shell.console.publishing.active')}</StatusTag>}
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
        render: (_value, row) =>
          row.offStandard ? <ReleaseOffStandard relaxed={row.relaxed} locale={locale} /> : null,
      },
    ],
    [activeRow, locale, t],
  )

  // Kind and target have no order a person uses, so their headers do not sort. The other columns sort on the server.
  const requestColumns = useMemo<AxisColumn<ConsolePublishRequest>[]>(
    () => [
      {
        key: 'kind',
        kind: 'fixed',
        title: t('shell.console.publishing.kind'),
        filter: { kind: 'enum', options: requestKinds.map((value) => ({ value, label: value })) },
        dataIndex: 'kind',
      },
      {
        key: 'target',
        kind: 'fixed',
        title: t('shell.console.publishing.target'),
        filter: { kind: 'enum', options: requestTargets.map((value) => ({ value, label: value })) },
        dataIndex: 'target',
      },
      {
        key: 'status',
        kind: 'fixed',
        title: t('shell.console.publishing.status'),
        filter: {
          kind: 'enum',
          options: requestStatuses.map((value) => ({ value, label: t(`shell.console.publishing.status.${value}`) })),
        },
        sorter: true,
        render: (_value, row) => (
          <StatusTag tone={statusTones[row.status]}>{t(`shell.console.publishing.status.${row.status}`)}</StatusTag>
        ),
      },
      {
        key: 'label',
        kind: 'freeText',
        title: t('shell.console.publishing.label'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, row) => row.label,
      },
      {
        key: 'requestedBy',
        kind: 'freeText',
        title: t('shell.console.publishing.requestedBy'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, row) => row.requestedByName ?? row.requestedBy,
      },
      {
        key: 'requestedAt',
        kind: 'fixed',
        title: t('shell.console.publishing.requestedAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, row) => formatTime(row.requestedAt, locale),
      },
      {
        key: 'finishedAt',
        kind: 'fixed',
        title: t('shell.console.publishing.finishedAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, row) => formatTime(row.finishedAt, locale),
      },
    ],
    [locale, t],
  )

  return (
    <SectionPart testId="console-publishing" heading={{ title: t('shell.console.publishing.title') }}>
      <Flex vertical gap="middle">
        <Select
          style={{ minWidth: 200, maxWidth: 400 }}
          aria-label={t('shell.console.publishing.application')}
          value={application}
          options={applications.map((candidate) => ({ value: candidate.id, label: candidate.name }))}
          onChange={(value: string) => {
            setApplication(value)
            setView({ ...view, page: 1 })
          }}
        />
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.console.publishing.releases')}</Typography.Text>
          <AxisTable<ConsoleReleaseItem>
            testId="console-publishing-releases"
            rowKey={(row) => `${row.releaseId}/${row.activatedAt}`}
            columns={releaseColumns}
            dataSource={load.publishing?.releases}
            loading={loading}
            emptyText={t('shell.console.publishing.empty')}
            failure={
              failed ? (
                <AxisAlert
                  data-testid="console-publishing-releases-error"
                  kind="error"
                  message={t('shell.console.publishing.loadFailed')}
                />
              ) : undefined
            }
          />
        </Flex>
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.console.publishing.requests')}</Typography.Text>
          <AxisTable<ConsolePublishRequest>
            testId="console-publishing-requests"
            rowKey={(row) => row.id}
            columns={requestColumns}
            dataSource={load.publishing?.requests ?? []}
            loading={loading}
            emptyText={t('shell.console.publishing.empty')}
            view={{
              query: view,
              onChange: setView,
              saved,
              pageSizes: tablePageSizes(),
              total: load.publishing?.totalCount ?? 0,
              search: true,
              columns: true,
            }}
            toolbar={{ onRefresh: () => setReload((count) => count + 1) }}
            failure={
              failed ? (
                <AxisAlert
                  data-testid="console-publishing-requests-error"
                  kind="error"
                  message={t('shell.console.publishing.loadFailed')}
                />
              ) : undefined
            }
          />
        </Flex>
      </Flex>
    </SectionPart>
  )
}
