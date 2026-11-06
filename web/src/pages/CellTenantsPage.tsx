import { Alert, Flex, Input, Select } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { fetchCellTenants, type CellFailedWork, type CellTenant, type CellTenantStatus } from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { useCellConsole } from './cellConsoleContext'

const statuses: CellTenantStatus[] = ['active', 'readOnly', 'suspended', 'provisioning', 'deleting']

/**
 * The Tenants view of the cell console: one row per tenant of the cell, with a search by id or host and a filter by
 * status. Both run in the browser on the loaded list. It is read only.
 */
export function CellTenantsPage() {
  const t = useText()
  const { onRefused } = useCellConsole()
  const [tenants, setTenants] = useState<CellTenant[]>()
  const [failed, setFailed] = useState(false)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<CellTenantStatus>()

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
  }, [onRefused])

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
  ]

  return (
    <PageLayout title={t('shell.cell.tenants.title')}>
      {failed && <Alert data-testid="cell-tenants-error" type="error" title={t('shell.cell.tenants.loadFailed')} />}
      {visible && (
        <AxisTable<CellTenant>
          testId="cell-tenants"
          rowKey={(tenant) => tenant.id}
          dataSource={visible}
          columns={columns}
          emptyText={t('shell.cell.tenants.empty')}
          toolbar={{
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
    </PageLayout>
  )
}
