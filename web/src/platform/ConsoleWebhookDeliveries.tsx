import { Alert, Card, Flex, Select, Tag } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import {
  fetchConsoleWebhookDeliveries,
  type ConsoleApplication,
  type ConsoleWebhookDelivery,
  type ConsoleWebhookDeliveryPage,
} from './console'
import { useText } from './texts'

interface ConsoleWebhookDeliveriesProps {
  /** The applications of the tenant, for the application filter. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The request query, so a result for an older request shows as loading. */
  query?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: ConsoleWebhookDeliveryPage
  failed: boolean
}

const stateColors: Record<ConsoleWebhookDelivery['state'], string> = {
  delivered: 'green',
  pending: 'blue',
  deadLettered: 'red',
  discarded: 'default',
}

/**
 * The Webhook deliveries view of the operator console: the recent deliveries of the tenant's webhooks, newest first,
 * with the state, the attempts and the last status code of each. Two filters narrow the list to one application or one
 * webhook. The webhook filter offers the webhooks of the answer, narrowed to the chosen application. A failure shows an
 * error in this card only.
 */
export function ConsoleWebhookDeliveries({ applications, locale }: ConsoleWebhookDeliveriesProps) {
  const t = useText()
  const [application, setApplication] = useState<string>()
  const [webhook, setWebhook] = useState<string>()
  const [page, setPage] = useState(1)
  const [load, setLoad] = useState<LoadState>({ failed: false })
  const query = new URLSearchParams({
    ...(application ? { application } : {}),
    ...(webhook ? { webhook } : {}),
    ...(page > 1 ? { page: String(page) } : {}),
  }).toString()
  const loading = load.query !== query

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleWebhookDeliveries(query, controller.signal)
      .then((result) => {
        if (result.kind === 'forbidden') {
          setLoad({ query, failed: true })
          return
        }
        // A page past the end becomes the last page once the total is known.
        setPage((current) => Math.min(current, Math.max(1, Math.ceil(result.page.totalCount / result.page.pageSize))))
        setLoad({ query, page: result.page, failed: false })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console webhook deliveries failed', error)
        setLoad({ query, failed: true })
      })
    return () => controller.abort()
  }, [query])

  const webhooks = (load.page?.webhooks ?? []).filter(
    (candidate) => !application || candidate.applicationId === application,
  )

  const columns = useMemo<AxisColumn<ConsoleWebhookDelivery>[]>(
    () => [
      {
        key: 'application',
        kind: 'freeText',
        title: t('shell.console.webhooks.application'),
        render: (_value, row) => row.application,
      },
      {
        key: 'webhook',
        kind: 'freeText',
        title: t('shell.console.webhooks.webhook'),
        render: (_value, row) => row.webhook,
      },
      {
        key: 'event',
        kind: 'freeText',
        title: t('shell.console.webhooks.event'),
        render: (_value, row) => row.event,
      },
      {
        key: 'state',
        kind: 'fixed',
        title: t('shell.console.webhooks.state'),
        render: (_value, row) => (
          <Tag color={stateColors[row.state]}>{t(`shell.console.webhooks.state.${row.state}`)}</Tag>
        ),
      },
      { key: 'attempts', kind: 'fixed', title: t('shell.console.webhooks.attempts'), dataIndex: 'attempts' },
      {
        key: 'statusCode',
        kind: 'fixed',
        title: t('shell.console.webhooks.statusCode'),
        render: (_value, row) => row.statusCode,
      },
      {
        key: 'deliveredAt',
        kind: 'fixed',
        title: t('shell.console.webhooks.deliveredAt'),
        render: (_value, row) => (row.deliveredAt === null ? null : new Date(row.deliveredAt).toLocaleString(locale)),
      },
    ],
    [locale, t],
  )

  return (
    <Card data-testid="console-webhook-deliveries" title={t('shell.console.webhooks.title')}>
      <Flex vertical gap="middle">
        <Flex gap="small" wrap>
          <Select
            allowClear
            style={{ minWidth: 200 }}
            aria-label={t('shell.console.webhooks.application')}
            placeholder={t('shell.console.webhooks.filter.application')}
            value={application}
            options={applications.map((candidate) => ({ value: candidate.id, label: candidate.name }))}
            onChange={(value: string | undefined) => {
              setApplication(value)
              setWebhook(undefined)
              setPage(1)
            }}
          />
          <Select
            allowClear
            style={{ minWidth: 200 }}
            aria-label={t('shell.console.webhooks.webhook')}
            placeholder={t('shell.console.webhooks.filter.webhook')}
            value={webhook}
            options={webhooks.map((candidate) => ({
              value: candidate.id,
              label: application ? candidate.name : `${candidate.application} / ${candidate.name}`,
            }))}
            onChange={(value: string | undefined) => {
              setWebhook(value)
              setPage(1)
            }}
          />
        </Flex>
        {load.failed && !loading ? (
          // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
          <Alert
            data-testid="console-webhook-deliveries-error"
            type="error"
            title={t('shell.console.webhooks.loadFailed')}
          />
        ) : (
          <AxisTable<ConsoleWebhookDelivery>
            rowKey={(row) => row.id}
            columns={columns}
            dataSource={load.page?.items}
            loading={loading}
            emptyText={t('shell.console.webhooks.empty')}
            pagination={{
              current: page,
              pageSize: load.page?.pageSize ?? 20,
              total: load.page?.totalCount ?? 0,
            }}
            onChange={(pagination) => setPage(pagination.current ?? 1)}
          />
        )}
      </Flex>
    </Card>
  )
}
