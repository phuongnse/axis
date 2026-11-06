import { EmptyDash } from './EmptyValue'
import { AxisTable } from './AxisTable'
import { useText } from './texts'
import { formatBytes, type TenantUsageDay } from './tenantUsage'

/**
 * The day table of a tenant's usage, newest first, as the users page's "Usage" section and the cell console's Tenant
 * view show it. The sizes and counts are formatted for `locale`, and `testId` names the table.
 */
export function TenantUsageDayTable({
  days,
  locale,
  testId,
}: {
  days: TenantUsageDay[]
  locale: string
  testId: string
}) {
  const t = useText()
  const count = (value: number | null) => (value === null ? <EmptyDash /> : value.toLocaleString(locale))

  return (
    <AxisTable<TenantUsageDay>
      testId={testId}
      rowKey={(row) => row.day}
      dataSource={days}
      emptyText={t('shell.users.usage.noDays')}
      columns={[
        {
          key: 'day',
          kind: 'fixed',
          title: t('shell.users.usage.day.date'),
          render: (_value, row) => new Date(row.day).toLocaleDateString(locale, { timeZone: 'UTC' }),
        },
        {
          key: 'databaseBytes',
          kind: 'fixed',
          title: t('shell.users.usage.day.databaseBytes'),
          render: (_value, row) => formatBytes(row.databaseBytes, locale),
        },
        {
          key: 'fileBytes',
          kind: 'fixed',
          title: t('shell.users.usage.day.fileBytes'),
          render: (_value, row) => formatBytes(row.fileBytes, locale),
        },
        {
          key: 'users',
          kind: 'fixed',
          title: t('shell.users.usage.day.users'),
          render: (_value, row) => count(row.users),
        },
        {
          key: 'applications',
          kind: 'fixed',
          title: t('shell.users.usage.day.applications'),
          render: (_value, row) => count(row.applications),
        },
        {
          key: 'emailsSent',
          kind: 'fixed',
          title: t('shell.users.usage.day.emailsSent'),
          render: (_value, row) => count(row.emailsSent),
        },
        {
          key: 'connectorCalls',
          kind: 'fixed',
          title: t('shell.users.usage.day.connectorCalls'),
          render: (_value, row) => count(row.connectorCalls),
        },
        {
          key: 'workItemsRun',
          kind: 'fixed',
          title: t('shell.users.usage.day.workItemsRun'),
          render: (_value, row) => count(row.workItemsRun),
        },
      ]}
    />
  )
}
