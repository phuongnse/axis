import { Tag } from 'antd'
import { AxisAlert } from './AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { AxisTable, type AxisColumn } from './AxisTable'
import {
  fetchConsoleWebhookDeliveries,
  type ConsoleApplication,
  type ConsoleWebhookDelivery,
  type ConsoleWebhookDeliveryPage,
} from './console'
import { SectionPart } from './SectionParts'
import { recordPageSize, recordQuery, tablePageSizes } from './tableQuery'
import { useText } from './texts'
import { useTableView } from './useTableView'

interface ConsoleWebhookDeliveriesProps {
  /** The applications of the tenant, which name the applications of the deliveries and fill the Application filter. */
  applications: ConsoleApplication[]
  /** The UI locale, for formatting the times. */
  locale: string
}

const stateColors: Record<ConsoleWebhookDelivery['state'], string> = {
  delivered: 'green',
  pending: 'blue',
  deadLettered: 'red',
  discarded: 'default',
}

/** The columns the view's search, filter and sort can name, see docs/reference/operator-console.md#api. */
const deliveryFields = [
  { name: 'application', type: 'text' },
  { name: 'webhook', type: 'text' },
  { name: 'event', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'attempts', type: 'integer' },
  { name: 'statusCode', type: 'integer' },
  { name: 'deliveredAt', type: 'date' },
] as const
// The view has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []
const deliveryStates = ['delivered', 'pending', 'deadLettered', 'discarded']

/**
 * The Webhook deliveries view of the operator console: the recent deliveries of the tenant's webhooks, newest first until
 * the view sorts them, with the state, the attempts and the last status code of each, paged on the server. The table has
 * Search, Filter, Columns and Refresh, a sort on State, Attempts, Status code and Delivered, and a pager with a page size,
 * all kept in the address under the `webhookDeliveries.` prefix, because Schedules and Dead letters keep their own names,
 * see docs/reference/operator-console.md#views and docs/reference/actions-and-controls.md#tables. The Application and
 * Webhook columns filter by name, so the view has no pickers above the table. A failure shows an error in this part only.
 */
export function ConsoleWebhookDeliveries({ applications, locale }: ConsoleWebhookDeliveriesProps) {
  const t = useText()
  const { view, setView } = useTableView(
    deliveryFields,
    recordPageSize,
    noParameters,
    true,
    true,
    true,
    'webhookDeliveries.',
  )
  const query = recordQuery(view)
  const [reload, setReload] = useState(0)
  const [deliveries, setDeliveries] = useState<ConsoleWebhookDeliveryPage | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchConsoleWebhookDeliveries(query, controller.signal)
      .then((result) => setDeliveries(result.kind === 'forbidden' ? 'failed' : result.page))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn('Loading the console webhook deliveries failed', error)
        setDeliveries('failed')
      })
    return () => controller.abort()
  }, [query, reload])

  const page = typeof deliveries === 'object' ? deliveries : undefined
  const webhooks = page?.webhooks

  const columns = useMemo<AxisColumn<ConsoleWebhookDelivery>[]>(
    () => [
      {
        key: 'application',
        kind: 'freeText',
        title: t('shell.console.webhooks.application'),
        filter: { kind: 'enum', options: applications.map((row) => ({ value: row.id, label: row.name })) },
        render: (_value, row) => row.application,
      },
      {
        key: 'webhook',
        kind: 'freeText',
        title: t('shell.console.webhooks.webhook'),
        filter: {
          kind: 'enum',
          options: (webhooks ?? []).map((row) => ({ value: row.id, label: `${row.application} / ${row.name}` })),
        },
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
        filter: {
          kind: 'enum',
          options: deliveryStates.map((value) => ({ value, label: t(`shell.console.webhooks.state.${value}`) })),
        },
        sorter: true,
        render: (_value, row) => (
          <Tag color={stateColors[row.state]}>{t(`shell.console.webhooks.state.${row.state}`)}</Tag>
        ),
      },
      {
        key: 'attempts',
        kind: 'fixed',
        title: t('shell.console.webhooks.attempts'),
        filter: { kind: 'number' },
        sorter: true,
        dataIndex: 'attempts',
      },
      {
        key: 'statusCode',
        kind: 'fixed',
        title: t('shell.console.webhooks.statusCode'),
        filter: { kind: 'number' },
        sorter: true,
        render: (_value, row) => row.statusCode,
      },
      {
        key: 'deliveredAt',
        kind: 'fixed',
        title: t('shell.console.webhooks.deliveredAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, row) => (row.deliveredAt === null ? null : new Date(row.deliveredAt).toLocaleString(locale)),
      },
    ],
    [applications, locale, webhooks, t],
  )

  return (
    <SectionPart testId="console-webhook-deliveries" heading={{ title: t('shell.console.webhooks.title') }}>
      <AxisTable<ConsoleWebhookDelivery>
        rowKey={(row) => row.id}
        columns={columns}
        dataSource={page?.items ?? []}
        loading={deliveries === undefined}
        emptyText={t('shell.console.webhooks.empty')}
        view={{
          query: view,
          onChange: setView,
          pageSizes: tablePageSizes(),
          total: page?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        toolbar={{ onRefresh: () => setReload((count) => count + 1) }}
        failure={
          deliveries === 'failed' ? (
            <AxisAlert
              data-testid="console-webhook-deliveries-error"
              kind="error"
              message={t('shell.console.webhooks.loadFailed')}
            />
          ) : undefined
        }
      />
    </SectionPart>
  )
}
