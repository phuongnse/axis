import { Flex, Tag } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import {
  fetchCellTenants,
  type CellFailedWork,
  type CellTenant,
  type CellTenantPage,
  type CellTenantStatus,
  type CellTenantUsage,
} from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { formatBytes } from '../platform/tenantUsage'
import { useTableView } from '../platform/useTableView'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { CreateCellTenantModal } from './CreateCellTenantModal'

const statuses: CellTenantStatus[] = ['active', 'readOnly', 'suspended', 'provisioning', 'deleting']

/** The columns the view's filter and sort can name, in table order, see `docs/reference/tenant-lifecycle.md#routes`. */
const tenantFields = [
  { name: 'id', type: 'text' },
  { name: 'status', type: 'enum' },
  { name: 'application', type: 'text' },
  { name: 'hosts', type: 'text' },
  { name: 'instances', type: 'integer' },
  { name: 'unknownCalls', type: 'integer' },
  { name: 'deadLetters', type: 'integer' },
  { name: 'storage', type: 'integer' },
  { name: 'users', type: 'integer' },
  { name: 'applications', type: 'integer' },
  { name: 'emailsPerDay', type: 'integer' },
  { name: 'newestArchiveAt', type: 'date' },
] as const
// The tenant table has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** An archive older than this is stale, see `docs/reference/cell-console.md#views`. The daily run leaves two hours of slack. */
const staleArchiveMs = 26 * 60 * 60 * 1000

/**
 * The Tenants view of the cell console: one row per tenant of the cell, with its failed work, its usage against its
 * limits and the time of its newest archive, which carries a mark when it is more than 26 hours old. The server searches,
 * filters, sorts and pages the list. The table has Search, which looks in the id and the hosts, Filter on the status,
 * Columns, a sort on every column but Hosts, and a pager with a page size, all kept in the address, see
 * `docs/reference/cell-console.md#views` and `docs/reference/actions-and-controls.md#tables`. Create tenant opens the
 * dialog that creates a tenant, and the list loads again when the create job ends.
 */
export function CellTenantsPage() {
  const t = useText()
  const { onRefused } = useCellConsole()
  const { locale } = usePlatform().current
  const { colour } = useDesignTokens()
  const { view, setView } = useTableView(tenantFields, recordPageSize, noParameters, true, true, true)
  const query = recordQuery(view)
  const [tenants, setTenants] = useState<CellTenantPage | 'failed'>()
  const [creating, setCreating] = useState(false)
  // Counts the create jobs that ended, so the list loads again.
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchCellTenants(query, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setTenants(result.value)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenants failed', error)
          setTenants('failed')
        }
      },
    )
    return () => controller.abort()
  }, [onRefused, query, reload])

  const columns = useMemo<AxisColumn<CellTenant>[]>(() => {
    const statusOptions = statuses.map((value) => ({ value, label: t(`shell.cell.tenants.status.${value}`) }))

    const failedCount = (key: keyof CellFailedWork) => (_value: unknown, tenant: CellTenant) =>
      tenant.failedWork ? tenant.failedWork[key] : <EmptyDash />

    const usageCell = (key: keyof CellTenantUsage) => (_value: unknown, tenant: CellTenant) => {
      const { limit, used } = tenant.usage[key]
      if (used === null) {
        return <EmptyDash />
      }
      const amount = (value: number) => (key === 'storage' ? formatBytes(value, locale) : value.toLocaleString(locale))
      return limit === null
        ? t('shell.cell.tenants.usedNoLimit').replace('{used}', amount(used))
        : t('shell.cell.tenants.usedOfLimit').replace('{used}', amount(used)).replace('{limit}', amount(limit))
    }

    const archiveCell = (_value: unknown, tenant: CellTenant) => {
      if (tenant.newestArchiveAt === null) {
        return <EmptyDash />
      }
      const stale = Date.now() - Date.parse(tenant.newestArchiveAt) > staleArchiveMs
      return (
        <Flex gap="small" align="center" wrap>
          {new Date(tenant.newestArchiveAt).toLocaleString(locale)}
          {stale && (
            <Tag
              data-testid="cell-tenants-archive-stale"
              variant="outlined"
              style={{ color: colour.dangerText, borderColor: colour.danger, background: 'transparent' }}
            >
              {t('shell.cell.tenants.archiveStale')}
            </Tag>
          )}
        </Flex>
      )
    }

    return [
      {
        key: 'id',
        kind: 'fixed',
        title: t('shell.cell.tenants.id'),
        sorter: true,
        render: (_value, tenant) => <Link to={`/tenants/${encodeURIComponent(tenant.id)}`}>{tenant.id}</Link>,
      },
      {
        key: 'status',
        kind: 'fixed',
        title: t('shell.cell.tenants.status'),
        sorter: true,
        render: (_value, tenant) => t(`shell.cell.tenants.status.${tenant.status}`),
        filter: { kind: 'enum', options: statusOptions },
      },
      {
        key: 'application',
        kind: 'fixed',
        title: t('shell.cell.tenants.application'),
        sorter: true,
        render: (_value, tenant) => tenant.application ?? <EmptyDash />,
      },
      {
        key: 'hosts',
        kind: 'freeText',
        title: t('shell.cell.tenants.hosts'),
        render: (_value, tenant) => tenant.hosts.join(', '),
      },
      {
        key: 'instances',
        kind: 'fixed',
        title: t('shell.cell.tenants.failedInstances'),
        sorter: true,
        render: failedCount('instances'),
      },
      {
        key: 'unknownCalls',
        kind: 'fixed',
        title: t('shell.cell.tenants.unknownCalls'),
        sorter: true,
        render: failedCount('unknownCalls'),
      },
      {
        key: 'deadLetters',
        kind: 'fixed',
        title: t('shell.cell.tenants.deadLetters'),
        sorter: true,
        render: failedCount('deadLetters'),
      },
      {
        key: 'storage',
        kind: 'fixed',
        title: t('shell.cell.tenants.storage'),
        sorter: true,
        render: usageCell('storage'),
      },
      { key: 'users', kind: 'fixed', title: t('shell.cell.tenants.users'), sorter: true, render: usageCell('users') },
      {
        key: 'applications',
        kind: 'fixed',
        title: t('shell.cell.tenants.applications'),
        sorter: true,
        render: usageCell('applications'),
      },
      {
        key: 'emailsPerDay',
        kind: 'fixed',
        title: t('shell.cell.tenants.emailsToday'),
        sorter: true,
        render: usageCell('emailsPerDay'),
      },
      {
        key: 'newestArchiveAt',
        kind: 'fixed',
        title: t('shell.cell.tenants.newestArchive'),
        sorter: true,
        render: archiveCell,
      },
    ]
  }, [colour, locale, t])
  const page = typeof tenants === 'object' ? tenants : undefined

  return (
    <PageLayout title={t('shell.cell.tenants.title')}>
      <AxisTable<CellTenant>
        testId="cell-tenants"
        rowKey={(tenant) => tenant.id}
        dataSource={page?.items ?? []}
        loading={tenants === undefined}
        columns={columns}
        view={{
          query: view,
          onChange: setView,
          pageSizes: tablePageSizes(),
          total: page?.totalCount ?? 0,
          search: true,
          columns: true,
        }}
        emptyText={t('shell.cell.tenants.empty')}
        toolbar={{
          main: (
            <AxisButton role="main" data-testid="cell-tenants-create" onClick={() => setCreating(true)}>
              {t('shell.cell.create.button')}
            </AxisButton>
          ),
        }}
        failure={
          tenants === 'failed' ? (
            <AxisAlert data-testid="cell-tenants-error" kind="error" message={t('shell.cell.tenants.loadFailed')} />
          ) : undefined
        }
      />
      {creating && (
        <CreateCellTenantModal onClose={() => setCreating(false)} onEnded={() => setReload((count) => count + 1)} />
      )}
    </PageLayout>
  )
}
