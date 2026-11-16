import { Flex, Tag } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import {
  fetchCellTenants,
  type CellFailedWork,
  type CellTenant,
  type CellTenantStatus,
  type CellTenantUsage,
} from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { recordPageSize } from '../platform/tableQuery'
import { formatBytes } from '../platform/tenantUsage'
import { useTableView } from '../platform/useTableView'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { CreateCellTenantModal } from './CreateCellTenantModal'

const statuses: CellTenantStatus[] = ['active', 'readOnly', 'suspended', 'provisioning', 'deleting']

/** The fields the view's filter can name: the Status column. */
const tenantFields = [{ name: 'status', type: 'enum' }] as const
// The tenant table has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

/** An archive older than this is stale, see `docs/reference/cell-console.md#views`. The daily run leaves two hours of slack. */
const staleArchiveMs = 26 * 60 * 60 * 1000

/**
 * The Tenants view of the cell console: one row per tenant of the cell, with its failed work, its usage against its
 * limits and the time of its newest archive, which carries a mark when it is more than 26 hours old. AxisTable's Search
 * looks in the id and the hosts, and its Filter filters on the status. Both are kept in the address and run in the
 * browser on the loaded list, and the table pages 20 rows at a time, see `docs/reference/cell-console.md#views`. Create
 * tenant opens the dialog that creates a tenant, and the list loads again when the create job ends.
 */
export function CellTenantsPage() {
  const t = useText()
  const { onRefused } = useCellConsole()
  const { locale } = usePlatform().current
  const { colour } = useDesignTokens()
  const [tenants, setTenants] = useState<CellTenant[]>()
  const [failed, setFailed] = useState(false)
  const [creating, setCreating] = useState(false)
  // Counts the create jobs that ended, so the list loads again.
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    fetchCellTenants(controller.signal).then(
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
          setFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [onRefused, reload])

  const { view, setView } = useTableView(tenantFields, recordPageSize, noParameters, true, true)
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

  const columns: AxisColumn<CellTenant>[] = [
    {
      key: 'id',
      kind: 'fixed',
      title: t('shell.cell.tenants.id'),
      render: (_value, tenant) => <Link to={`/tenants/${encodeURIComponent(tenant.id)}`}>{tenant.id}</Link>,
      searchText: (tenant) => tenant.id,
    },
    {
      key: 'status',
      kind: 'fixed',
      title: t('shell.cell.tenants.status'),
      render: (_value, tenant) => t(`shell.cell.tenants.status.${tenant.status}`),
      filter: { kind: 'enum', value: (tenant) => tenant.status, options: statusOptions },
    },
    {
      key: 'application',
      kind: 'fixed',
      title: t('shell.cell.tenants.application'),
      render: (_value, tenant) => tenant.application ?? <EmptyDash />,
    },
    {
      key: 'hosts',
      kind: 'freeText',
      title: t('shell.cell.tenants.hosts'),
      render: (_value, tenant) => tenant.hosts.join(', '),
      searchText: (tenant) => tenant.hosts.join(' '),
    },
    {
      key: 'instances',
      kind: 'fixed',
      title: t('shell.cell.tenants.failedInstances'),
      render: failedCount('instances'),
    },
    {
      key: 'unknownCalls',
      kind: 'fixed',
      title: t('shell.cell.tenants.unknownCalls'),
      render: failedCount('unknownCalls'),
    },
    {
      key: 'deadLetters',
      kind: 'fixed',
      title: t('shell.cell.tenants.deadLetters'),
      render: failedCount('deadLetters'),
    },
    { key: 'storage', kind: 'fixed', title: t('shell.cell.tenants.storage'), render: usageCell('storage') },
    { key: 'users', kind: 'fixed', title: t('shell.cell.tenants.users'), render: usageCell('users') },
    {
      key: 'applications',
      kind: 'fixed',
      title: t('shell.cell.tenants.applications'),
      render: usageCell('applications'),
    },
    {
      key: 'emailsToday',
      kind: 'fixed',
      title: t('shell.cell.tenants.emailsToday'),
      render: usageCell('emailsPerDay'),
    },
    { key: 'newestArchive', kind: 'fixed', title: t('shell.cell.tenants.newestArchive'), render: archiveCell },
  ]

  return (
    <PageLayout title={t('shell.cell.tenants.title')}>
      {failed && (
        <AxisAlert data-testid="cell-tenants-error" kind="error" message={t('shell.cell.tenants.loadFailed')} />
      )}
      {tenants && (
        <AxisTable<CellTenant>
          testId="cell-tenants"
          rowKey={(tenant) => tenant.id}
          dataSource={tenants}
          columns={columns}
          view={{ query: view, onChange: setView }}
          emptyText={t('shell.cell.tenants.empty')}
          toolbar={{
            main: (
              <AxisButton role="main" data-testid="cell-tenants-create" onClick={() => setCreating(true)}>
                {t('shell.cell.create.button')}
              </AxisButton>
            ),
          }}
        />
      )}
      {creating && (
        <CreateCellTenantModal onClose={() => setCreating(false)} onEnded={() => setReload((count) => count + 1)} />
      )}
    </PageLayout>
  )
}
