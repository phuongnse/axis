import { AxisTable, type AxisColumn, type AxisTableView } from './AxisTable'
import type { CellHostRequestState } from './cell'
import type { HostTexts } from './HostRequestDialog'
import { useText } from './texts'

interface HostRequestTableProps {
  requests: CellHostRequestState[]
  /** The prefix of the texts: `hostRequest.host|state|expiresAt`, `hostRequest.state.*` and `noHostRequests`. */
  texts: HostTexts
  locale: string
  testId: string
  /** The view of the table. Without it the table has no Filter, and with it each column filters its own rows. */
  view?: AxisTableView
}

const hostRequestStates = ['pending', 'verified', 'expired'] as const

/**
 * The list of the hosts that a tenant asked for, with each host's state and, while it is pending, the date it expires.
 * The cell console's Tenant view and the users page's Hosts section both draw it, see
 * docs/reference/shared-widgets.md#which-widget. The console gives it a view, whose filter lives in the address, see
 * docs/reference/cell-console.md#views.
 */
export function HostRequestTable({ requests, texts, locale, testId, view }: HostRequestTableProps) {
  const t = useText()
  const columns: AxisColumn<CellHostRequestState>[] = [
    {
      key: 'host',
      kind: 'freeText',
      title: t(`${texts}.hostRequest.host`),
      render: (_value, row) => row.host,
      filter: { kind: 'text', value: (row) => row.host },
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t(`${texts}.hostRequest.state`),
      render: (_value, row) => t(`${texts}.hostRequest.state.${row.state}`),
      filter: {
        kind: 'enum',
        value: (row) => row.state,
        options: hostRequestStates.map((state) => ({
          value: state,
          label: t(`${texts}.hostRequest.state.${state}`),
        })),
      },
    },
    {
      key: 'expiresAt',
      kind: 'fixed',
      title: t(`${texts}.hostRequest.expiresAt`),
      render: (_value, row) => (row.state === 'pending' ? new Date(row.expiresAt).toLocaleString(locale) : null),
      // The date is the UTC day of the expiry, and a request that is not pending has no expiry to filter on.
      filter: {
        kind: 'date',
        value: (row) => (row.state === 'pending' ? new Date(row.expiresAt).toISOString().slice(0, 10) : null),
      },
    },
  ]
  return (
    <AxisTable<CellHostRequestState>
      testId={testId}
      rowKey={(row) => row.host}
      dataSource={requests}
      columns={columns}
      view={view}
      emptyText={t(`${texts}.noHostRequests`)}
    />
  )
}
