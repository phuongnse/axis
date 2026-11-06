import { Flex, Tag, Typography } from 'antd'
import { AxisTable, type AxisColumn } from './AxisTable'
import type { CellJob } from './cell'
import { EmptyDash } from './EmptyValue'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/** The table of the cell console's jobs, in the order given. The Jobs page shows the cell's, and a tenant's view can show the tenant's. */
export function CellJobTable({ jobs, locale }: { jobs: readonly CellJob[]; locale: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const stateColours: Record<CellJob['state'], string> = {
    queued: colour.textMuted,
    running: colour.infoText,
    succeeded: colour.successText,
    failed: colour.dangerText,
  }
  const columns: AxisColumn<CellJob>[] = [
    {
      key: 'createdAt',
      kind: 'fixed',
      title: t('shell.cell.jobs.createdAt'),
      render: (_value, job) => new Date(job.createdAt).toLocaleString(locale),
    },
    { key: 'kind', kind: 'fixed', title: t('shell.cell.jobs.kind'), dataIndex: 'kind' },
    { key: 'tenant', kind: 'fixed', title: t('shell.cell.jobs.tenant'), dataIndex: 'tenantId' },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.cell.jobs.state'),
      render: (_value, job) => (
        <Tag
          variant="outlined"
          style={{ color: stateColours[job.state], borderColor: stateColours[job.state], background: 'transparent' }}
        >
          {t(`shell.cell.jobs.state.${job.state}`)}
        </Tag>
      ),
    },
    {
      key: 'steps',
      kind: 'freeText',
      title: t('shell.cell.jobs.steps'),
      render: (_value, job) =>
        job.steps.length === 0 ? null : (
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
    />
  )
}
