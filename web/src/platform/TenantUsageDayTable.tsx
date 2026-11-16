import type { ReactNode } from 'react'
import { EmptyDash } from './EmptyValue'
import { AxisTable, type AxisTableView } from './AxisTable'
import { useText } from './texts'
import { formatBytes, type TenantUsageDay } from './tenantUsage'

/**
 * The day table of a tenant's usage, as the users page's "Usage" section and the cell console's Tenant view show it. The
 * sizes and counts are formatted for `locale`, and `testId` names the table. Without a `view` the table is the users
 * page's, newest first, with no filter and no sort. With a `view` every column filters and sorts, for a table that the server
 * pages, see docs/reference/actions-and-controls.md#tables. `loading` and `failure` are those of {@link AxisTable}.
 */
export function TenantUsageDayTable({
  days,
  locale,
  testId,
  view,
  loading,
  failure,
}: {
  days: TenantUsageDay[]
  locale: string
  testId: string
  view?: AxisTableView
  loading?: boolean
  failure?: ReactNode
}) {
  const t = useText()
  // AxisTable keeps a column's `sorter`, so a table with no view must not ask for one.
  const sorter = view !== undefined
  const count = (value: number | null) => (value === null ? <EmptyDash /> : value.toLocaleString(locale))

  return (
    <AxisTable<TenantUsageDay>
      testId={testId}
      rowKey={(row) => row.day}
      dataSource={days}
      view={view}
      loading={loading}
      failure={failure}
      emptyText={t('shell.users.usage.noDays')}
      columns={[
        {
          key: 'day',
          kind: 'fixed',
          filter: { kind: 'date' },
          sorter,
          title: t('shell.users.usage.day.date'),
          render: (_value, row) => new Date(row.day).toLocaleDateString(locale, { timeZone: 'UTC' }),
        },
        {
          key: 'databaseBytes',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.databaseBytes'),
          render: (_value, row) => formatBytes(row.databaseBytes, locale),
        },
        {
          key: 'fileBytes',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.fileBytes'),
          render: (_value, row) => formatBytes(row.fileBytes, locale),
        },
        {
          key: 'users',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.users'),
          render: (_value, row) => count(row.users),
        },
        {
          key: 'applications',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.applications'),
          render: (_value, row) => count(row.applications),
        },
        {
          key: 'emailsSent',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.emailsSent'),
          render: (_value, row) => count(row.emailsSent),
        },
        {
          key: 'connectorCalls',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.connectorCalls'),
          render: (_value, row) => count(row.connectorCalls),
        },
        {
          key: 'workItemsRun',
          kind: 'fixed',
          filter: { kind: 'number' },
          sorter,
          title: t('shell.users.usage.day.workItemsRun'),
          render: (_value, row) => count(row.workItemsRun),
        },
      ]}
    />
  )
}
