import { Alert, Card, Descriptions, Flex, Modal, Typography } from 'antd'
import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { AxisButton } from '../platform/AxisButton'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { CellJobTable } from '../platform/CellJobTable'
import {
  fetchCellJobs,
  fetchCellTenant,
  queueCellTenantJob,
  type CellJob,
  type CellQuota,
  type CellTenantAction,
  type CellTenantDetail,
} from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { PageLayout } from '../platform/PageLayout'
import { newIdempotencyKey } from '../platform/processes'
import { useText } from '../platform/texts'
import { useFollowCellJob } from '../platform/useFollowCellJob'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { NotFoundPage } from './NotFoundPage'

interface QuotaRow extends CellQuota {
  key: string
}

/** The texts of each action, by their full keys. */
const actionTexts = {
  suspend: {
    button: 'shell.cell.tenant.suspend',
    confirm: 'shell.cell.tenant.suspendConfirm',
    body: 'shell.cell.tenant.suspendBody',
    ok: 'shell.cell.tenant.suspendOk',
  },
  resume: {
    button: 'shell.cell.tenant.resume',
    confirm: 'shell.cell.tenant.resumeConfirm',
    body: 'shell.cell.tenant.resumeBody',
    ok: 'shell.cell.tenant.resumeOk',
  },
} as const

/**
 * The Tenant view of the cell console: one tenant's status, application and hosts, its quotas with the ones it overrides,
 * and its jobs in the shared job table. An unknown id shows the not-found page. An `active` or `readOnly` tenant offers
 * Suspend and a `suspended` or `readOnly` one offers Resume, each behind a confirmation. An action queues a job with an
 * `Idempotency-Key`, which stays for a retry after a network error or a 5xx and goes with a final answer. The page then
 * follows the job every 2 seconds until it ends and reloads the tenant and its jobs.
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
  const [confirming, setConfirming] = useState<CellTenantAction | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  // The job to follow. Setting it again to the same id does not restart the check, so a queued job sets it once.
  const [following, setFollowing] = useState<string | null>(null)
  // Counts the jobs that ended, so the tenant and its jobs load again.
  const [reload, setReload] = useState(0)
  // The Idempotency-Key of each action's request that got no final answer, by tenant and action, so a retry cannot
  // queue a second job.
  const actionKeys = useRef(new Map<string, string>())

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
  }, [id, onRefused, reload])

  useFollowCellJob(following, {
    onJob: (job) => setJobs((current) => current?.map((item) => (item.id === job.id ? job : item))),
    onEnd: () => {
      setFollowing(null)
      setReload((count) => count + 1)
    },
    onRefused,
  })

  if (tenant === null) {
    return <NotFoundPage />
  }

  async function runAction(action: CellTenantAction) {
    const key = `${id}/${action}`
    setBusy(true)
    setActionError(null)
    try {
      // A key is kept only while its request has no final answer: a network error or a 5xx.
      const idempotencyKey = actionKeys.current.get(key) ?? newIdempotencyKey()
      actionKeys.current.set(key, idempotencyKey)
      let result
      try {
        result = await queueCellTenantJob(id, action, idempotencyKey)
      } catch (error: unknown) {
        console.warn(`Queuing the ${action} job of the cell tenant '${id}' failed`, error)
        setActionError(t('shell.cell.tenant.actionFailed'))
        return
      }
      actionKeys.current.delete(key)
      if (result.kind === 'ok') {
        setConfirming(null)
        setJobs((current) => [result.value, ...(current ?? []).filter((item) => item.id !== result.value.id)])
        setFollowing(result.value.id)
      } else if (result.kind === 'problem') {
        setActionError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } finally {
      setBusy(false)
    }
  }

  const canSuspend = tenant?.status === 'active' || tenant?.status === 'readOnly'
  const canResume = tenant?.status === 'suspended' || tenant?.status === 'readOnly'

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
            {(canSuspend || canResume) && (
              <Flex gap="small">
                {canSuspend && (
                  <AxisButton
                    role="secondary"
                    data-testid="cell-tenant-suspend"
                    disabled={following !== null}
                    onClick={() => {
                      setActionError(null)
                      setConfirming('suspend')
                    }}
                  >
                    {t(actionTexts.suspend.button)}
                  </AxisButton>
                )}
                {canResume && (
                  <AxisButton
                    role="secondary"
                    data-testid="cell-tenant-resume"
                    disabled={following !== null}
                    onClick={() => {
                      setActionError(null)
                      setConfirming('resume')
                    }}
                  >
                    {t(actionTexts.resume.button)}
                  </AxisButton>
                )}
              </Flex>
            )}
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
      {confirming && (
        <Modal
          open
          data-testid="cell-tenant-confirm"
          title={t(actionTexts[confirming].confirm).replace('{tenant}', id)}
          okText={t(actionTexts[confirming].ok)}
          okButtonProps={{ danger: confirming === 'suspend' }}
          cancelText={t('shell.users.roles.cancel')}
          confirmLoading={busy}
          onOk={() => void runAction(confirming)}
          onCancel={() => setConfirming(null)}
        >
          <Flex vertical gap="small">
            <Typography.Text>{t(actionTexts[confirming].body)}</Typography.Text>
            {actionError && <Alert data-testid="cell-tenant-action-error" type="error" title={actionError} />}
          </Flex>
        </Modal>
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
