import { AxisTable, type AxisColumn } from './AxisTable'
import type { CellHostRequestState } from './cell'
import type { HostTexts } from './HostRequestDialog'
import { useText } from './texts'

interface HostRequestTableProps {
  requests: CellHostRequestState[]
  /** The prefix of the texts: `hostRequest.host|state|expiresAt`, `hostRequest.state.*` and `noHostRequests`. */
  texts: HostTexts
  locale: string
  testId: string
}

/**
 * The list of the hosts that a tenant asked for, with each host's state and, while it is pending, the date it expires.
 * The cell console's Tenant view and the users page's Hosts section both draw it, see
 * docs/reference/shared-widgets.md#which-widget.
 */
export function HostRequestTable({ requests, texts, locale, testId }: HostRequestTableProps) {
  const t = useText()
  const columns: AxisColumn<CellHostRequestState>[] = [
    { key: 'host', kind: 'freeText', title: t(`${texts}.hostRequest.host`), render: (_value, row) => row.host },
    {
      key: 'state',
      kind: 'fixed',
      title: t(`${texts}.hostRequest.state`),
      render: (_value, row) => t(`${texts}.hostRequest.state.${row.state}`),
    },
    {
      key: 'expiresAt',
      kind: 'fixed',
      title: t(`${texts}.hostRequest.expiresAt`),
      render: (_value, row) => (row.state === 'pending' ? new Date(row.expiresAt).toLocaleString(locale) : null),
    },
  ]
  return (
    <AxisTable<CellHostRequestState>
      testId={testId}
      rowKey={(row) => row.host}
      dataSource={requests}
      columns={columns}
      emptyText={t(`${texts}.noHostRequests`)}
    />
  )
}
