import { Descriptions, Flex, Input, Modal, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { CellJobTable } from '../platform/CellJobTable'
import {
  downloadCellTenantExport,
  fetchCellDeletedTenant,
  fetchCellJobs,
  fetchCellTenant,
  queueCellTenantJob,
  type CellDeletedTenant,
  type CellHostRequestState,
  type CellJob,
  type CellQuota,
  type CellTenantAction,
  type CellTenantDetail,
} from '../platform/cell'
import { EmptyDash } from '../platform/EmptyValue'
import { saveFile } from '../platform/exports'
import { PageLayout } from '../platform/PageLayout'
import { newIdempotencyKey } from '../platform/processes'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFollowCellJob } from '../platform/useFollowCellJob'
import { usePlatform } from './context'
import { useCellConsole } from './cellConsoleContext'
import { CellTenantHostDialog } from './CellTenantHostDialog'
import { CellTenantQuotasDialog } from './CellTenantQuotasDialog'
import { CellTenantUsage } from './CellTenantUsage'
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
  export: {
    button: 'shell.cell.tenant.export',
    confirm: 'shell.cell.tenant.exportConfirm',
    body: 'shell.cell.tenant.exportBody',
    ok: 'shell.cell.tenant.exportOk',
  },
  delete: {
    button: 'shell.cell.tenant.delete',
    confirm: 'shell.cell.tenant.deleteConfirm',
    body: 'shell.cell.tenant.deleteBody',
    ok: 'shell.cell.tenant.deleteOk',
  },
  'cancel-deletion': {
    button: 'shell.cell.tenant.cancelDeletion',
    confirm: 'shell.cell.tenant.cancelDeletionConfirm',
    body: 'shell.cell.tenant.cancelDeletionBody',
    ok: 'shell.cell.tenant.cancelDeletionOk',
  },
} as const

/**
 * The Tenant view of the cell console, in parts a rule apart, see docs/reference/shell.md#layout: Details with one tenant's
 * status, application, hosts and actions, then Host verification with the state of each host the operator asked for,
 * Quotas with the ones it overrides, Usage of the last 13 months by day in the table of the users page's "Usage" section, paged on the server from the tenant's own usage days route,
 * and Jobs in the shared job table. A `deleting` tenant shows its purge date after its status. An id that is no tenant
 * but a purged one shows only Deleted, the date of the purge, the date its deletion is complete and its jobs, with no
 * action, see docs/reference/cell-console.md#views. An id that is neither shows the not-found page. An `active` or `readOnly` tenant offers
 * Suspend and a `suspended` or `readOnly` one offers Resume. Any tenant but a `provisioning` one offers Export. An `active`,
 * `readOnly` or `suspended` tenant offers Delete, which stays off until the operator types the tenant id exactly, and a
 * `deleting` one offers Cancel deletion in place of Suspend, Resume and Delete. Each action is behind a confirmation. An
 * action queues a job with an `Idempotency-Key`, which stays for a retry after a network error or a 5xx and goes with a
 * final answer. The page then follows the job every 2 seconds until it ends and reloads the tenant and its jobs. With a
 * succeeded `export` job among the jobs, Download opens a warning that the ZIP is restricted data, and only accepting it
 * fetches the ZIP. Set quotas and Add a host open their own dialogs, show for every status and queue no job: the quota
 * table shows the quotas that the cell answered. Once Add a host is answered, the page loads the tenant again, so the
 * new host shows as pending.
 */
export function CellTenantPage() {
  const t = useText()
  const { size } = useDesignTokens()
  const { locale } = usePlatform().current
  const { onRefused } = useCellConsole()
  const { id = '' } = useParams()
  const [tenant, setTenant] = useState<CellTenantDetail | null>()
  // The purged tenant, read only when the id is no tenant. `null` is an id that was never a tenant.
  const [deleted, setDeleted] = useState<CellDeletedTenant | null>()
  const [tenantFailed, setTenantFailed] = useState(false)
  const [jobs, setJobs] = useState<CellJob[]>()
  const [jobsFailed, setJobsFailed] = useState(false)
  const [openDialog, setOpenDialog] = useState<'quotas' | 'host' | null>(null)
  const [confirming, setConfirming] = useState<CellTenantAction | null>(null)
  // The text typed to confirm a deletion. It starts empty whenever a confirmation opens.
  const [typed, setTyped] = useState('')
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [downloading, setDownloading] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  // The job to follow. Setting it again to the same id does not restart the check, so a queued job sets it once.
  const [following, setFollowing] = useState<string | null>(null)
  // Counts the jobs that ended, so the tenant and its jobs load again.
  const [reload, setReload] = useState(0)
  // The Idempotency-Key of each action's request that got no final answer, by tenant and action, so a retry cannot
  // queue a second job.
  const actionKeys = useRef(new Map<string, string>())

  useEffect(() => {
    const controller = new AbortController()
    fetchCellTenant(id, controller.signal)
      .then((result) => {
        if (result.kind !== 'ok') {
          onRefused(result.kind)
          return
        }
        setTenant(result.value)
        if (result.value !== null) {
          setDeleted(undefined)
          return
        }
        return fetchCellDeletedTenant(id, controller.signal).then((purged) => {
          if (purged.kind === 'ok') {
            setDeleted(purged.value)
          } else {
            onRefused(purged.kind)
          }
        })
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the cell tenant failed', error)
          setTenantFailed(true)
        }
      })
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

  if (tenant === null && deleted === null) {
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

  async function runDownload(exportId: string) {
    setBusy(true)
    setDownloadError(null)
    try {
      let result
      try {
        result = await downloadCellTenantExport(id, exportId)
      } catch (error: unknown) {
        console.warn(`Downloading the export of the cell tenant '${id}' failed`, error)
        setDownloadError(t('shell.cell.tenant.actionFailed'))
        return
      }
      if (result.kind === 'ok') {
        saveFile(result.value.blob, result.value.fileName)
        setDownloading(false)
      } else if (result.kind === 'problem') {
        setDownloadError(t('shell.cell.tenant.actionRefused').replace('{title}', result.title))
      } else {
        onRefused(result.kind)
      }
    } finally {
      setBusy(false)
    }
  }

  const canSuspend = tenant?.status === 'active' || tenant?.status === 'readOnly'
  const canResume = tenant?.status === 'suspended' || tenant?.status === 'readOnly'
  const canExport = tenant !== undefined && tenant !== null && tenant.status !== 'provisioning'
  // The newest succeeded export job, as the jobs come newest first. Its id is the export id.
  const ready = jobs?.find((job) => job.kind === 'export' && job.state === 'succeeded')
  const canDelete = tenant?.status === 'active' || tenant?.status === 'readOnly' || tenant?.status === 'suspended'
  const canCancelDeletion = tenant?.status === 'deleting'

  function openConfirmation(action: CellTenantAction) {
    setActionError(null)
    setTyped('')
    setConfirming(action)
  }

  const when = (time: string) => new Date(time).toLocaleString(locale)
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

  const hostRequestColumns: AxisColumn<CellHostRequestState>[] = [
    {
      key: 'host',
      kind: 'freeText',
      title: t('shell.cell.tenant.hostRequest.host'),
      render: (_value, row) => row.host,
    },
    {
      key: 'state',
      kind: 'fixed',
      title: t('shell.cell.tenant.hostRequest.state'),
      render: (_value, row) => t(`shell.cell.tenant.hostRequest.state.${row.state}`),
    },
    {
      key: 'expiresAt',
      kind: 'fixed',
      title: t('shell.cell.tenant.hostRequest.expiresAt'),
      render: (_value, row) => (row.state === 'pending' ? when(row.expiresAt) : null),
    },
  ]

  return (
    <PageLayout title={id}>
      {tenantFailed && (
        <AxisAlert data-testid="cell-tenant-error" kind="error" message={t('shell.cell.tenant.loadFailed')} />
      )}
      <SectionParts>
        {tenant && (
          <SectionPart testName="cell-tenant-details" heading={{ title: t('shell.cell.tenant.details') }}>
            <Descriptions
              data-testid="cell-tenant"
              column={1}
              items={[
                {
                  key: 'status',
                  label: t('shell.cell.tenant.status'),
                  children: t(`shell.cell.tenants.status.${tenant.status}`),
                },
                ...(tenant.status === 'deleting' && tenant.deleteAfter
                  ? [
                      {
                        key: 'deleteAfter',
                        label: t('shell.cell.tenant.deleteAfter'),
                        children: when(tenant.deleteAfter),
                      },
                    ]
                  : []),
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
            <Flex gap="small">
              {canSuspend && (
                <AxisButton
                  role="secondary"
                  data-testid="cell-tenant-suspend"
                  disabled={following !== null}
                  onClick={() => openConfirmation('suspend')}
                >
                  {t(actionTexts.suspend.button)}
                </AxisButton>
              )}
              {canResume && (
                <AxisButton
                  role="secondary"
                  data-testid="cell-tenant-resume"
                  disabled={following !== null}
                  onClick={() => openConfirmation('resume')}
                >
                  {t(actionTexts.resume.button)}
                </AxisButton>
              )}
              {canExport && (
                <AxisButton
                  role="secondary"
                  data-testid="cell-tenant-export"
                  disabled={following !== null}
                  onClick={() => openConfirmation('export')}
                >
                  {t(actionTexts.export.button)}
                </AxisButton>
              )}
              {canDelete && (
                <AxisButton
                  role="secondary"
                  data-testid="cell-tenant-delete"
                  disabled={following !== null}
                  onClick={() => openConfirmation('delete')}
                >
                  {t(actionTexts.delete.button)}
                </AxisButton>
              )}
              {canCancelDeletion && (
                <AxisButton
                  role="secondary"
                  data-testid="cell-tenant-cancel-deletion"
                  disabled={following !== null}
                  onClick={() => openConfirmation('cancel-deletion')}
                >
                  {t(actionTexts['cancel-deletion'].button)}
                </AxisButton>
              )}
              {ready && (
                <AxisButton
                  role="secondary"
                  data-testid="cell-tenant-download"
                  disabled={following !== null}
                  onClick={() => {
                    setDownloadError(null)
                    setDownloading(true)
                  }}
                >
                  {t('shell.cell.tenant.download')}
                </AxisButton>
              )}
              <AxisButton role="secondary" data-testid="cell-tenant-set-quotas" onClick={() => setOpenDialog('quotas')}>
                {t('shell.cell.tenant.setQuotas')}
              </AxisButton>
              <AxisButton role="secondary" data-testid="cell-tenant-add-host" onClick={() => setOpenDialog('host')}>
                {t('shell.cell.tenant.addHost')}
              </AxisButton>
            </Flex>
          </SectionPart>
        )}
        {deleted && (
          <SectionPart testName="cell-tenant-details" heading={{ title: t('shell.cell.tenant.details') }}>
            <Descriptions
              data-testid="cell-tenant-deleted"
              column={1}
              items={[
                { key: 'status', label: t('shell.cell.tenant.status'), children: t('shell.cell.tenant.deleted') },
                { key: 'purgedAt', label: t('shell.cell.tenant.purgedAt'), children: when(deleted.deletedAt) },
                { key: 'completeAt', label: t('shell.cell.tenant.completeAt'), children: when(deleted.completeAt) },
              ]}
            />
          </SectionPart>
        )}
        {tenant && (
          <SectionPart testName="cell-tenant-host-requests" heading={{ title: t('shell.cell.tenant.hostRequests') }}>
            <AxisTable<CellHostRequestState>
              testId="cell-tenant-host-requests"
              rowKey={(row) => row.host}
              dataSource={tenant.hostRequests}
              columns={hostRequestColumns}
              emptyText={t('shell.cell.tenant.noHostRequests')}
            />
          </SectionPart>
        )}
        {tenant && (
          <SectionPart testName="cell-tenant-quotas" heading={{ title: t('shell.cell.tenant.quotas') }}>
            <AxisTable<QuotaRow>
              testId="cell-tenant-quotas"
              rowKey={(row) => row.key}
              dataSource={quotas}
              columns={quotaColumns}
              emptyText={t('shell.cell.tenant.noQuotas')}
            />
          </SectionPart>
        )}
        {tenant && (
          <SectionPart testName="cell-tenant-usage" heading={{ title: t('shell.cell.tenant.usage') }}>
            <CellTenantUsage tenant={id} locale={locale} />
          </SectionPart>
        )}
        <SectionPart testName="cell-tenant-jobs" heading={{ title: t('shell.cell.tenant.jobs') }}>
          {jobsFailed && (
            <AxisAlert
              data-testid="cell-tenant-jobs-error"
              kind="error"
              message={t('shell.cell.tenant.jobsLoadFailed')}
            />
          )}
          {jobs && <CellJobTable jobs={jobs} locale={locale} />}
        </SectionPart>
      </SectionParts>
      {tenant && openDialog === 'quotas' && (
        <CellTenantQuotasDialog
          tenant={id}
          quotas={tenant.quotas}
          onSaved={(quotas) => setTenant((current) => current && { ...current, quotas })}
          onRefused={onRefused}
          onClose={() => setOpenDialog(null)}
        />
      )}
      {openDialog === 'host' && (
        <CellTenantHostDialog
          tenant={id}
          onRefused={onRefused}
          onAdded={() => setReload((count) => count + 1)}
          onClose={() => setOpenDialog(null)}
        />
      )}
      {confirming && (
        <Modal
          open
          width={size.dialog.small}
          data-testid="cell-tenant-confirm"
          title={t(actionTexts[confirming].confirm).replace('{tenant}', id)}
          okText={t(actionTexts[confirming].ok)}
          okButtonProps={{
            danger: confirming === 'suspend' || confirming === 'delete',
            disabled: confirming === 'delete' && typed !== id,
          }}
          cancelText={t(
            confirming === 'cancel-deletion' ? 'shell.cell.tenant.cancelDeletionClose' : 'shell.users.roles.cancel',
          )}
          confirmLoading={busy}
          onOk={() => void runAction(confirming)}
          onCancel={() => setConfirming(null)}
        >
          <Flex vertical gap="small">
            <Typography.Text>{t(actionTexts[confirming].body)}</Typography.Text>
            {confirming === 'delete' && (
              <AxisField
                label={t('shell.cell.tenant.deleteTypeId').replace('{tenant}', id)}
                help={t('shell.cell.tenant.deleteTypeId.help')}
              >
                <Input
                  data-testid="cell-tenant-delete-id"
                  value={typed}
                  autoComplete="off"
                  onChange={(event) => setTyped(event.target.value)}
                />
              </AxisField>
            )}
            {actionError && <AxisAlert data-testid="cell-tenant-action-error" kind="error" message={actionError} />}
          </Flex>
        </Modal>
      )}
      {downloading && ready && (
        <Modal
          open
          width={size.dialog.small}
          data-testid="cell-tenant-download-warning"
          title={t('shell.cell.tenant.downloadConfirm').replace('{tenant}', id)}
          okText={t('shell.cell.tenant.downloadOk')}
          cancelText={t('shell.users.roles.cancel')}
          confirmLoading={busy}
          onOk={() => void runDownload(ready.id)}
          onCancel={() => setDownloading(false)}
        >
          <Flex vertical gap="small">
            <Typography.Text>{t('shell.cell.tenant.downloadBody')}</Typography.Text>
            {downloadError && (
              <AxisAlert data-testid="cell-tenant-download-error" kind="error" message={downloadError} />
            )}
          </Flex>
        </Modal>
      )}
    </PageLayout>
  )
}
