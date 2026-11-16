import { useEffect, useState } from 'react'
import { AxisAlert } from '../platform/AxisAlert'
import { fetchCellTenantUsageDays, type CellTenantUsageDayPage } from '../platform/cell'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { TenantUsageDayTable } from '../platform/TenantUsageDayTable'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { useCellConsole } from './cellConsoleContext'

/** The columns the view's filter and sort can name, see docs/reference/tenant-lifecycle.md#routes. */
const usageDayFields = [
  { name: 'day', type: 'date' },
  { name: 'databaseBytes', type: 'integer' },
  { name: 'fileBytes', type: 'integer' },
  { name: 'users', type: 'integer' },
  { name: 'applications', type: 'integer' },
  { name: 'emailsSent', type: 'integer' },
  { name: 'connectorCalls', type: 'integer' },
  { name: 'workItemsRun', type: 'integer' },
] as const
// The view has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/**
 * The Usage table of the cell console's Tenant view: the days of the tenant's last 13 months, newest first until the
 * view sorts them, paged on the server. The table has Filter and Columns, a sort on every column and a pager with a page
 * size, all kept in the address under their plain names because it is the only table of the view with a view of its
 * own, and no Search because a day holds no text, see docs/reference/cell-console.md#views and
 * docs/reference/actions-and-controls.md#tables. A failure shows an error in this part only.
 */
export function CellTenantUsage({ tenant, locale }: { tenant: string; locale: string }) {
  const t = useText()
  const { onRefused } = useCellConsole()
  const { view, setView } = useTableView(usageDayFields, recordPageSize, noParameters, false, true, true)
  const query = recordQuery(view)
  const [days, setDays] = useState<CellTenantUsageDayPage | 'failed'>()

  useEffect(() => {
    const controller = new AbortController()
    fetchCellTenantUsageDays(tenant, query, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setDays(result.value)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenant usage days failed', error)
          setDays('failed')
        }
      },
    )
    return () => controller.abort()
  }, [tenant, query, onRefused])

  const page = typeof days === 'object' ? days : undefined

  return (
    <TenantUsageDayTable
      days={page?.items ?? []}
      locale={locale}
      testId="cell-tenant-usage"
      loading={days === undefined}
      view={{
        query: view,
        onChange: setView,
        pageSizes: tablePageSizes(),
        total: page?.totalCount ?? 0,
        columns: true,
      }}
      failure={
        days === 'failed' ? (
          <AxisAlert
            data-testid="cell-tenant-usage-error"
            kind="error"
            message={t('shell.cell.tenant.usageLoadFailed')}
          />
        ) : undefined
      }
    />
  )
}
