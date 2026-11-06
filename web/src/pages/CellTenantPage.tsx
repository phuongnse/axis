import { Alert, Card, Descriptions } from 'antd'
import { useEffect, useState } from 'react'
import { useParams } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { CellJobTable } from '../platform/CellJobTable'
import { fetchCellJobs, fetchCellTenant, type CellJob, type CellQuota, type CellTenantDetail } from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { NotFoundPage } from './NotFoundPage'

interface QuotaRow extends CellQuota {
  key: string
}

/**
 * The Tenant view of the cell console: one tenant's status, application and hosts, its quotas with the ones it overrides,
 * and its jobs in the shared job table. An unknown id shows the not-found page. It is read only.
 */
export function CellTenantPage() {
  const t = useText()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const { id = '' } = useParams()
  const [tenant, setTenant] = useState<CellTenantDetail | null>()
  const [tenantFailed, setTenantFailed] = useState(false)
  const [jobs, setJobs] = useState<CellJob[]>()
  const [jobsFailed, setJobsFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchCellTenant(id, controller.signal).then(
      (result) => {
        if (result.kind === 'ok') {
          setTenant(result.value)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenant failed', error)
          setTenantFailed(true)
        }
      },
    )
    fetchCellJobs(controller.signal, id).then(
      (result) => {
        if (result.kind === 'ok') {
          setJobs(result.value)
        } else {
          onRefused(result.kind)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenant jobs failed', error)
          setJobsFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [id, onRefused])

  if (tenant === null) {
    return <NotFoundPage />
  }

  const quotas: QuotaRow[] = tenant ? Object.entries(tenant.quotas).map(([key, quota]) => ({ key, ...quota })) : []
  const quotaColumns: AxisColumn<QuotaRow>[] = [
    {
      key: 'key',
      kind: 'fixed',
      title: t('shell.cell.tenant.quota.key'),
      render: (_value, row) => t(`shell.cell.tenant.quota.${row.key}`),
    },
    {
      key: 'value',
      kind: 'fixed',
      title: t('shell.cell.tenant.quota.value'),
      render: (_value, row) =>
        row.value === null ? t('shell.cell.tenant.quota.noLimit') : row.value.toLocaleString(locale),
    },
    {
      key: 'source',
      kind: 'fixed',
      title: t('shell.cell.tenant.quota.source'),
      render: (_value, row) =>
        t(row.overridden ? 'shell.cell.tenant.quota.overridden' : 'shell.cell.tenant.quota.default'),
    },
  ]

  return (
    <PageLayout title={id}>
      {tenantFailed && <Alert data-testid="cell-tenant-error" type="error" title={t('shell.cell.tenant.loadFailed')} />}
      {tenant && (
        <>
          <Card>
            <Descriptions
              data-testid="cell-tenant"
              column={1}
              items={[
                {
                  key: 'status',
                  label: t('shell.cell.tenant.status'),
                  children: t(`shell.cell.tenants.status.${tenant.status}`),
                },
                {
                  key: 'application',
                  label: t('shell.cell.tenant.application'),
                  children: tenant.application ?? <EmptyDash />,
                },
                {
                  key: 'hosts',
                  label: t('shell.cell.tenant.hosts'),
                  children: tenant.hosts.length > 0 ? tenant.hosts.join(', ') : <EmptyDash />,
                },
              ]}
            />
          </Card>
          <Card title={t('shell.cell.tenant.quotas')}>
            <AxisTable<QuotaRow>
              testId="cell-tenant-quotas"
              rowKey={(row) => row.key}
              dataSource={quotas}
              columns={quotaColumns}
              emptyText={t('shell.cell.tenant.noQuotas')}
            />
          </Card>
        </>
      )}
      <Card title={t('shell.cell.tenant.jobs')}>
        {jobsFailed && (
          <Alert data-testid="cell-tenant-jobs-error" type="error" title={t('shell.cell.tenant.jobsLoadFailed')} />
        )}
        {jobs && <CellJobTable jobs={jobs} locale={locale} />}
      </Card>
    </PageLayout>
  )
}
