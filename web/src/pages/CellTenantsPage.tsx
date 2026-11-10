import { Flex, Input, Select, Tag } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useMemo, useState } from 'react'
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
import { formatBytes } from '../platform/tenantUsage'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { CreateCellTenantModal } from './CreateCellTenantModal'

const statuses: CellTenantStatus[] = ['active', 'readOnly', 'suspended', 'provisioning', 'deleting']

/** An archive older than this is stale, see `docs/reference/cell-console.md#views`. The daily run leaves two hours of slack. */
const staleArchiveMs = 26 * 60 * 60 * 1000

/**
 * The Tenants view of the cell console: one row per tenant of the cell, with its failed work, its usage against its
 * limits and the time of its newest archive, which carries a mark when it is more than 26 hours old. It has a search by
 * id or host and a filter by status. Both run in the browser on the loaded list. Create tenant opens the dialog that
 * creates a tenant, and the list loads again when the create job ends.
 */
export function CellTenantsPage() {
  const t = useText()
  const { onRefused } = useCellConsole()
  const { locale } = usePlatform().current
  const { colour } = useDesignTokens()
  const [tenants, setTenants] = useState<CellTenant[]>()
  const [failed, setFailed] = useState(false)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<CellTenantStatus>()
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

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase()
    return tenants?.filter(
      (tenant) =>
        (term === '' ||
          tenant.id.toLowerCase().includes(term) ||
          tenant.hosts.some((host) => host.toLowerCase().includes(term))) &&
        (status === undefined || tenant.status === status),
    )
  }, [tenants, search, status])

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
    },
    {
      key: 'status',
      kind: 'fixed',
      title: t('shell.cell.tenants.status'),
      render: (_value, tenant) => t(`shell.cell.tenants.status.${tenant.status}`),
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
      {visible && (
        <AxisTable<CellTenant>
          testId="cell-tenants"
          rowKey={(tenant) => tenant.id}
          dataSource={visible}
          columns={columns}
          emptyText={t('shell.cell.tenants.empty')}
          toolbar={{
            main: (
              <AxisButton role="main" data-testid="cell-tenants-create" onClick={() => setCreating(true)}>
                {t('shell.cell.create.button')}
              </AxisButton>
            ),
            view: (
              <Flex gap="small" wrap>
                <Input.Search
                  allowClear
                  aria-label={t('shell.cell.tenants.search')}
                  placeholder={t('shell.cell.tenants.search')}
                  style={{ maxWidth: 400 }}
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                />
                <Select<CellTenantStatus>
                  allowClear
                  virtual={false}
                  aria-label={t('shell.cell.tenants.statusFilter')}
                  placeholder={t('shell.cell.tenants.statusFilter')}
                  style={{ minWidth: 160 }}
                  value={status}
                  onChange={(value: CellTenantStatus | undefined) => setStatus(value)}
                  options={statuses.map((value) => ({ value, label: t(`shell.cell.tenants.status.${value}`) }))}
                />
              </Flex>
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
