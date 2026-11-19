import { RedoOutlined } from '@ant-design/icons'
import { Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn, type AxisTableView } from './AxisTable'
import { cellTenantAddress, type CellJob } from './cell'
import { EmptyDash } from './EmptyValue'
import { StatusTag, type StatusTone } from './StatusTag'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'
import { useEmptyStyle } from './useEmptyStyle'

/**
 * The table of the cell console's jobs, in the order given. The Jobs page shows the cell's, and a tenant's view can show the tenant's.
 * With a `view`, the table has the full toolbar of a table paged on the server: a header sort on Created, Kind, Tenant and State,
 * a filter on every column but Steps, Search, Columns and the pager, see `docs/reference/actions-and-controls.md#tables`, and
 * `loading`, `failure` and `onRefresh` give its loading, load failure and Refresh. Without one, the table shows its rows as given.
 * With `linkTenants`, each job's tenant links to the tenant's page, as the Jobs view does. A tenant's own page and the Create tenant
 * dialog leave it plain, because there the link would lead back to the same page or to a tenant that may not exist yet.
 * With `onRunAgain`, a failed job other than a `purge` job has the Run again row action: the publisher queues a failed purge again on its own.
 */
export function CellJobTable({
  jobs,
  locale,
  linkTenants,
  onRunAgain,
  view,
  loading,
  failure,
  onRefresh,
}: {
  jobs: readonly CellJob[]
  locale: string
  linkTenants?: boolean
  onRunAgain?: (job: CellJob) => void
  view?: AxisTableView
  loading?: boolean
  failure?: ReactNode
  onRefresh?: () => void
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const emptyStyle = useEmptyStyle()
  const stateTones: Record<CellJob['state'], StatusTone> = {
    queued: 'neutral',
    running: 'neutral',
    succeeded: 'success',
    failed: 'danger',
  }
  const states: CellJob['state'][] = ['queued', 'running', 'succeeded', 'failed']
  const kinds = ['suspend', 'resume', 'read-only', 'create', 'export', 'delete', 'cancel-deletion', 'purge']
  // The sorts and filters belong to a table with a view, which the server serves.
  const sorter = view ? { sorter: true } : {}
  const columns: AxisColumn<CellJob>[] = [
    {
      key: 'createdAt',
      kind: 'fixed',
      title: t('shell.cell.jobs.createdAt'),
      ...sorter,
      ...(view && { filter: { kind: 'date' } }),
      render: (_value, job) => new Date(job.createdAt).toLocaleString(locale),
    },
    {
      key: 'kind',
      kind: 'fixed',
      title: t('shell.cell.jobs.kind'),
      dataIndex: 'kind',
      ...sorter,
      ...(view && { filter: { kind: 'enum', options: kinds.map((value) => ({ value, label: value })) } }),
    },
    {
      key: 'tenant',
      kind: 'fixed',
      title: t('shell.cell.jobs.tenant'),
      dataIndex: 'tenantId',
      ...(linkTenants && {
        render: (_value: unknown, job: CellJob) => <Link to={cellTenantAddress(job.tenantId)}>{job.tenantId}</Link>,
      }),
      ...sorter,
      ...(view && { filter: { kind: 'text' } }),
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.cell.jobs.state'),
      ...sorter,
      ...(view && {
        filter: {
          kind: 'enum',
          options: states.map((value) => ({ value, label: t(`shell.cell.jobs.state.${value}`) })),
        },
      }),
      render: (_value, job) => (
        <StatusTag tone={stateTones[job.state]}>{t(`shell.cell.jobs.state.${job.state}`)}</StatusTag>
      ),
    },
    {
      key: 'steps',
      kind: 'freeText',
      title: t('shell.cell.jobs.steps'),
      render: (_value, job) =>
        job.steps.length === 0 ? (
          <span style={emptyStyle} data-testid="cell-job-no-steps">
            {t('shell.cell.jobs.noSteps')}
          </span>
        ) : (
          <Flex vertical>
            {job.steps.map((step) => (
              <div key={step.name} data-testid="cell-job-step">
                <span>
                  {step.name}: {t(`shell.cell.jobs.step.${step.state}`)}
                </span>
                {step.error && (
                  <Typography.Text
                    data-testid="cell-job-step-error"
                    style={{ display: 'block', color: colour.dangerText }}
                  >
                    {step.error}
                  </Typography.Text>
                )}
              </div>
            ))}
          </Flex>
        ),
    },
    {
      key: 'error',
      kind: 'freeText',
      title: t('shell.cell.jobs.error'),
      ...(view && { filter: { kind: 'text' } }),
      render: (_value, job) =>
        job.error ? (
          <Typography.Text data-testid="cell-job-error" style={{ color: colour.dangerText }}>
            {job.error}
          </Typography.Text>
        ) : (
          <EmptyDash />
        ),
    },
  ]

  return (
    <AxisTable<CellJob>
      testId="cell-jobs"
      rowKey={(job) => job.id}
      dataSource={jobs}
      columns={columns}
      emptyText={t('shell.cell.jobs.empty')}
      view={view}
      loading={loading}
      failure={failure}
      toolbar={onRefresh && { onRefresh }}
      rowActions={
        onRunAgain && {
          configured: 1,
          actions: (job) =>
            job.state === 'failed' && job.kind !== 'purge'
              ? [
                  {
                    key: 'runAgain',
                    labelKey: 'shell.cell.jobs.runAgain',
                    icon: <RedoOutlined />,
                    onClick: () => onRunAgain(job),
                  },
                ]
              : null,
        }
      }
    />
  )
}
